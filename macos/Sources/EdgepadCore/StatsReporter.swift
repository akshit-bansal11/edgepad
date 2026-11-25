import Foundation

/// Where the Mac's numbers come from: the app's `MacStats`. Every read may throw, and the reporter then publishes
/// unknowns. `readFast` leaves ping and loss at -1: the reporter fills them. The twin of the laptop core's
/// IStatsSource.
public protocol StatsSource: AnyObject {
    /// The once-a-second numbers: CPU, memory, GPU, network rates, temperatures, power.
    func readFast() throws -> StatsSample
    /// Names and memory sizes; read at start and rarely after.
    func readInfo() throws -> StatsInfo
    /// The mounted disks.
    func readDisks() throws -> [DiskEntry]
    /// Round trip to the default gateway in milliseconds, or -1 when it did not answer. Blocks for up to two seconds,
    /// so the reporter runs it on a queue of its own.
    func ping() -> Int
    /// Called when the reporter stops: let go of anything held between readings (handles, previous counters). The
    /// default does nothing; the next reading starts afresh.
    func idle()
}

/// Nothing held between readings by default.
extension StatsSource {
    public func idle() {}
}

/// A repeating timer, or a test's stand-in for one. `stop` is idempotent.
public final class Stoppable {
    private let gate = NSLock()
    private var onStop: (() -> Void)?

    public init(_ onStop: @escaping () -> Void) {
        self.onStop = onStop
    }

    public func stop() {
        gate.lock()
        let stop = onStop
        onStop = nil
        gate.unlock()
        stop?()
    }

    /// Runs `action` at once and then every `seconds` on `queue`, until stopped.
    public static func repeating(every seconds: TimeInterval, on queue: DispatchQueue, _ action: @escaping () -> Void)
        -> Stoppable
    {
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now(), repeating: seconds)
        timer.setEventHandler(handler: action)
        timer.resume()
        return Stoppable { timer.cancel() }
    }
}

/// CPU use from two readings of cumulative idle and total ticks.
public enum CpuMeter {
    /// Busy share 0..100 between two readings. A counter that went backwards (reset, wrap) or did not move reads as 0.
    public static func percent(prevIdle: Int64, prevTotal: Int64, idle: Int64, total: Int64) -> Int {
        let dTotal = total - prevTotal
        let dIdle = idle - prevIdle
        guard dTotal > 0, dIdle >= 0, dIdle <= dTotal else { return 0 }
        return Int((100.0 * Double(dTotal - dIdle) / Double(dTotal)).rounded(.toNearestOrAwayFromZero))
    }
}

/// Turns a cumulative byte counter into KiB per second.
public final class RateMeter {
    private var lastBytes: Int64?
    private var lastMs: Int64 = 0

    public init() {}

    /// The first reading, a counter that went down, and a clock that did not advance all give 0.
    public func update(_ bytes: Int64, nowMs: Int64) -> Int {
        var rate = 0
        if let before = lastBytes, bytes >= before, nowMs > lastMs {
            rate = Int(min(Int64(Int.max), (bytes - before) * 1000 / ((nowMs - lastMs) * 1024)))
        }
        lastBytes = bytes
        lastMs = nowMs
        return rate
    }
}

/// The share of the last 20 pings that failed.
public final class LossMeter {
    public static let window = 20

    private var failed = [Bool](repeating: false, count: LossMeter.window)
    private var next = 0
    private var count = 0

    public init() {}

    public func record(_ ok: Bool) {
        failed[next] = !ok
        next = (next + 1) % Self.window
        count = min(count + 1, Self.window)
    }

    /// 0..100, or -1 before any ping.
    public var percent: Int {
        guard count > 0 else { return -1 }
        let lost = failed.prefix(count).filter { $0 }.count
        return Int((100.0 * Double(lost) / Double(count)).rounded(.toNearestOrAwayFromZero))
    }
}

/// Sends the phone the Mac's numbers, and only while the phone draws them. `setWatch` takes DASH_WATCH: bit 5 (the
/// Network page) also sends NET_INFO, read each tick and sent only when it changed, when there is a `NetInfoSource`.
/// Bit 0 (widgets) or bit 1 (System screen) starts a one-second timer, a mask with none of bits 0, 1 and 5 stops it,
/// and with the timer stopped nothing
/// is read, pinged or sent. Ping runs every third tick on a queue of its own (never two at once, so a stuck one never
/// blocks a tick), disks every tenth and only for bit 1, names every thirtieth. A text identical to the last one sent
/// is not sent again. An unreadable source publishes unknowns and is logged once. The twin of the laptop core's
/// StatsReporter.
public final class StatsReporter {
    public static let widgetsBit = 1
    public static let screenBit = 2
    public static let netBit = 32

    private static let pingEvery = 3
    private static let disksEvery = 10
    private static let infoEvery = 30

    private let source: StatsSource
    private let net: NetInfoSource?
    private let send: (Frame) -> Void
    private let startTimer: (@escaping () -> Void) -> Stoppable
    private let runPing: (@escaping () -> Void) -> Void
    private let runDisks: (@escaping () -> Void) -> Void
    private let gate = NSRecursiveLock()
    private let loss = LossMeter()
    private var timer: Stoppable?
    private var mask = 0
    private var generation = 0
    private var pingIn = 0
    private var disksIn = 0
    private var infoIn = 0
    private var lastPing = -1
    private var pingRunning = false
    private var ticking = false
    private var disksRunning = false
    private var logged = false
    private var disposed = false
    private var lastStats: String?
    private var lastInfo: String?
    private var lastDisks: String?
    private var lastNet: String?

    /// - Parameters:
    ///   - net: The Mac's connection facts, when it has them; read only while bit 5 is set.
    ///   - startTimer: Runs the action every second until stopped, on another queue, never inline. Tests pass their
    ///     own.
    ///   - runPing: Runs the blocking ping off the timer's queue.
    ///   - runDisks: Runs the disk read, which may hang, off the timer's queue.
    public init(
        source: StatsSource,
        send: @escaping (Frame) -> Void,
        startTimer: ((@escaping () -> Void) -> Stoppable)? = nil,
        runPing: @escaping (@escaping () -> Void) -> Void = { DispatchQueue.global(qos: .utility).async(execute: $0) },
        runDisks: @escaping (@escaping () -> Void) -> Void = { DispatchQueue.global(qos: .utility).async(execute: $0) },
        net: NetInfoSource? = nil
    ) {
        self.source = source
        self.net = net
        self.send = send
        let queue = DispatchQueue(label: "edgepad.stats", qos: .utility)
        self.startTimer = startTimer ?? { Stoppable.repeating(every: 1, on: queue, $0) }
        self.runPing = runPing
        self.runDisks = runDisks
    }

    /// True while the one-second timer exists.
    public var running: Bool {
        gate.lock()
        defer { gate.unlock() }
        return timer != nil
    }

    /// Applies the phone's DASH_WATCH. Anything outside 0..63 is the caller's to drop; only bits 0, 1 and 5 matter.
    public func setWatch(_ watch: Int) {
        gate.lock()
        defer { gate.unlock() }
        guard !disposed else { return }
        let wanted = watch & (Self.widgetsBit | Self.screenBit | Self.netBit)
        let before = mask
        mask = wanted
        if wanted == 0 {
            stop()
            return
        }
        if timer == nil {
            generation += 1
            (pingIn, disksIn, infoIn) = (0, 0, 0)
            (lastStats, lastInfo, lastDisks, lastNet) = (nil, nil, nil, nil)
            timer = startTimer { [weak self] in self?.tick() }
        } else {
            if before & Self.screenBit == 0 && wanted & Self.screenBit != 0 {
                disksIn = 0
                lastDisks = nil
            }
            if before & Self.netBit == 0 && wanted & Self.netBit != 0 {
                lastNet = nil  // a phone that just opened the page has none yet
            }
        }
    }

    public func dispose() {
        gate.lock()
        defer { gate.unlock() }
        disposed = true
        stop()
    }

    /// One second's work. Internal to the timer and to tests.
    func tick() {
        gate.lock()
        // A tick that finds the previous one still reading is skipped, never queued behind it.
        guard !disposed, mask != 0, !ticking else {
            gate.unlock()
            return
        }
        ticking = true
        let mine = generation
        startPingIfDue()
        let ping = lastPing
        let lost = loss.percent
        infoIn -= 1
        let infoDue = infoIn <= 0
        if infoDue {
            infoIn = Self.infoEvery
        }
        var disksDue = false
        if mask & Self.screenBit != 0 {
            disksIn -= 1
            if disksIn <= 0 {
                disksIn = Self.disksEvery
                disksDue = !disksRunning
                disksRunning = disksRunning || disksDue
            }
        }
        let netSource = mask & Self.netBit != 0 ? net : nil
        let fast = mask & (Self.widgetsBit | Self.screenBit) != 0
        gate.unlock()

        // Readings happen outside the lock, so a slow source never blocks setWatch, dispose or a ping result.
        let stats = readStats(ping: ping, loss: lost, fast: fast)
        let info = infoDue ? guarded({ try source.readInfo() }) : nil
        let netText = netSource.map { source in
            NetInfoWire.encode(guarded({ try source.read() }) ?? NetInfo.unknown)
        }
        if disksDue {
            readDisksAway(generation: mine)
        }

        gate.lock()
        defer { gate.unlock() }
        ticking = false
        guard !disposed, mine == generation else { return }
        sendIfChanged(.stats, stats, &lastStats)
        if let info {
            sendIfChanged(.statsInfo, DashWire.encodeStatsInfo(info), &lastInfo)
        }
        if let netText {
            sendIfChanged(.netInfo, netText, &lastNet)
        }
    }

    // Disks can hang (a volume that stopped answering): they are read on a worker and sent when they arrive, and no
    // second read starts while one is out, so a stuck one costs one worker and nothing else. The tick never waits.
    private func readDisksAway(generation mine: Int) {
        let source = self.source
        runDisks { [weak self] in
            let disks = self?.guarded({ try source.readDisks() })
            guard let self else { return }
            self.gate.lock()
            defer { self.gate.unlock() }
            self.disksRunning = false
            if let disks, !self.disposed, mine == self.generation, self.mask & Self.screenBit != 0 {
                self.sendIfChanged(.disks, DashWire.encodeDisks(disks), &self.lastDisks)
            }
        }
    }

    private func stop() {
        generation += 1
        timer?.stop()
        timer = nil
        source.idle()
    }

    // The Network page alone (not fast) reads no CPU or GPU: only the gateway ping and loss, which it shows, go out.
    private func readStats(ping: Int, loss lost: Int, fast: Bool) -> String {
        let sample: StatsSample
        do {
            guard fast else {
                return DashWire.encodeStats(
                    StatsSample(
                        cpu: -1, ram: -1, gpu: -1, vram: -1, down: -1, up: -1, ping: ping, loss: lost, cpuTemp: -1,
                        gpuTemp: -1, gpuPower: -1))
            }
            let s = try source.readFast()
            sample = StatsSample(
                cpu: s.cpu, ram: s.ram, gpu: s.gpu, vram: s.vram, down: s.down, up: s.up, ping: ping,
                loss: lost, cpuTemp: s.cpuTemp, gpuTemp: s.gpuTemp, gpuPower: s.gpuPower)
        } catch {
            logOnce()
            sample = StatsSample(
                cpu: -1, ram: -1, gpu: -1, vram: -1, down: -1, up: -1, ping: -1, loss: -1, cpuTemp: -1, gpuTemp: -1,
                gpuPower: -1)
        }
        return DashWire.encodeStats(sample)
    }

    private func startPingIfDue() {
        pingIn -= 1
        guard pingIn <= 0 else { return }
        pingIn = Self.pingEvery
        guard !pingRunning else { return }
        pingRunning = true
        let mine = generation
        let source = self.source
        runPing { [weak self] in
            let ms = source.ping()
            guard let self else { return }
            self.gate.lock()
            defer { self.gate.unlock() }
            self.pingRunning = false
            if mine == self.generation {
                self.loss.record(ms >= 0)
                self.lastPing = ms >= 0 ? ms : -1
            }
        }
    }

    private func sendIfChanged(_ kind: TextKind, _ text: String, _ last: inout String?) {
        guard text != last else { return }
        last = text
        send(.text(kind: kind.rawValue, value: text))
    }

    private func guarded<T>(_ read: () throws -> T) -> T? {
        do {
            return try read()
        } catch {
            logOnce()
            return nil
        }
    }

    private func logOnce() {
        gate.lock()
        defer { gate.unlock() }
        if !logged {
            logged = true
            Log.write("stats source failed (no data)")
        }
    }
}
