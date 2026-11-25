import Foundation

/// TEXT SPEED_RESULT: `state` is a `SpeedState` token, the speeds are Kbit/s, the times ms, -1 when unknown.
/// `atUnixMin` is when the test began, in minutes since the Unix epoch. `bytes` is what the test actually moved
/// (download plus upload, 0 while it starts), so the phone can show how much data it used. The most one test moves is
/// `SpeedTest.maxTotalBytes`. The twin of the laptop core's SpeedResult.
public struct SpeedResult: Equatable {
    public let state: String
    public let downKbps: Int
    public let upKbps: Int
    public let latencyMs: Int
    public let jitterMs: Int
    public let atUnixMin: Int64
    public let bytes: Int64

    public init(
        state: String, downKbps: Int, upKbps: Int, latencyMs: Int, jitterMs: Int, atUnixMin: Int64, bytes: Int64
    ) {
        self.state = state
        self.downKbps = downKbps
        self.upKbps = upKbps
        self.latencyMs = latencyMs
        self.jitterMs = jitterMs
        self.atUnixMin = atUnixMin
        self.bytes = bytes
    }
}

/// The text of `TextKind.speedResult`.
public enum SpeedWire {
    public static func encode(_ r: SpeedResult) -> String {
        [
            r.state, number(Int64(r.downKbps)), number(Int64(r.upKbps)), number(Int64(r.latencyMs)),
            number(Int64(r.jitterMs)), number(r.atUnixMin), number(r.bytes),
        ].joined(separator: "|")
    }

    /// The record for a text, or nil when it is not seven fields with a known state. For tests; the phone has its own
    /// parser.
    public static func parse(_ text: String) -> SpeedResult? {
        let f = text.split(separator: "|", omittingEmptySubsequences: false).map(String.init)
        guard f.count == 7, SpeedState(rawValue: f[0]) != nil, let down = Int(f[1]), let up = Int(f[2]),
            let latency = Int(f[3]), let jitter = Int(f[4]), let at = Int64(f[5]), let bytes = Int64(f[6])
        else { return nil }
        return SpeedResult(
            state: f[0], downKbps: down, upKbps: up, latencyMs: latency, jitterMs: jitter, atUnixMin: at, bytes: bytes)
    }

    private static func number(_ value: Int64) -> String {
        String(value < 0 ? -1 : value)
    }
}

/// Moves the test's bytes. The app's is `CloudflareSpeedTransport`; tests pass a fake. Both throw on anything that is
/// not a success.
public protocol SpeedTransport: AnyObject, Sendable {
    /// Downloads `bytes` bytes (0 is the latency probe), calling `read` with the size of each read.
    func download(bytes: Int, read: @escaping @Sendable (Int) -> Void) async throws
    /// Uploads `bytes` zero bytes.
    func upload(bytes: Int) async throws
}

/// A counter several workers add to.
private final class Tally: @unchecked Sendable {
    private let gate = NSLock()
    private var count = 0

    var value: Int {
        gate.lock()
        defer { gate.unlock() }
        return count
    }

    func add(_ amount: Int) {
        gate.lock()
        count += amount
        gate.unlock()
    }

    /// Adds `amount` and returns what the count was before.
    func take(_ amount: Int) -> Int {
        gate.lock()
        defer { gate.unlock() }
        let before = count
        count += amount
        return before
    }
}

/// One speed test at a time, started only by the phone's SPEED_TEST 1 and never on a schedule. Latency is the median of
/// ten empty downloads, jitter the mean absolute difference of consecutive ones. Download and upload each run four
/// parallel streams for at most `phaseLimit` or until `maxDownloadBytes` / `maxUploadBytes` have been asked for,
/// whichever is first. Cancelling stops every request within a read. The twin of the laptop core's SpeedTest.
public final class SpeedTest: @unchecked Sendable {
    /// The most one test downloads.
    public static let maxDownloadBytes = 25 * 1000 * 1000
    /// The most one test uploads.
    public static let maxUploadBytes = 10 * 1000 * 1000
    /// The most one test moves in all, the number the phone shows as its data cost.
    public static let maxTotalBytes = maxDownloadBytes + maxUploadBytes
    /// Each direction's time limit, in seconds.
    public static let defaultPhaseLimit: TimeInterval = 10

    static let streams = 4
    static let latencySamples = 10
    /// The most the ten latency probes may take in all, in seconds.
    static let latencyLimit: TimeInterval = 15
    static let downloadChunk = 5_000_000
    static let uploadChunk = 2_000_000

    private let transport: SpeedTransport
    private let nowMs: () -> Double
    private let clock: () -> Date
    private let phaseLimit: TimeInterval
    private let gate = NSLock()
    private var running: Task<Void, Never>?
    private var disposed = false
    private var lastResult: SpeedResult?

    /// - Parameters:
    ///   - transport: Moves the bytes; tests pass a fake.
    ///   - nowMs: A monotonic clock in ms; tests move their own.
    ///   - clock: The wall clock for the result's timestamp.
    ///   - phaseLimit: Each direction's time limit in seconds; tests shorten it.
    public init(
        transport: SpeedTransport = CloudflareSpeedTransport(),
        nowMs: @escaping () -> Double = { ProcessInfo.processInfo.systemUptime * 1000 },
        clock: @escaping () -> Date = Date.init,
        phaseLimit: TimeInterval = SpeedTest.defaultPhaseLimit
    ) {
        self.transport = transport
        self.nowMs = nowMs
        self.clock = clock
        self.phaseLimit = phaseLimit
    }

    /// The last test's outcome (never a running one), or nil before the first.
    public var last: SpeedResult? {
        gate.lock()
        defer { gate.unlock() }
        return lastResult
    }

    public var isRunning: Bool {
        gate.lock()
        defer { gate.unlock() }
        return running != nil
    }

    /// Starts a test and returns the task that ends with it, or nil when one already runs. `report` gets a running
    /// result at once and the outcome at the end.
    @discardableResult
    public func start(report: @escaping (SpeedResult) -> Void) -> Task<Void, Never>? {
        gate.lock()
        defer { gate.unlock() }
        guard !disposed, running == nil else { return nil }
        let at = Int64(clock().timeIntervalSince1970) / 60
        // Made under the lock: the task's own last step takes it too, so it cannot clear `running` before it is set.
        let task = Task { [self] in
            let begun = SpeedState.running.rawValue
            report(
                SpeedResult(
                    state: begun, downKbps: -1, upKbps: -1, latencyMs: -1, jitterMs: -1, atUnixMin: at, bytes: 0))
            let result = await measure(at: at)
            finish(result)
            report(result)
        }
        running = task
        return task
    }

    // Not async, so the lock is taken in a synchronous context.
    private func finish(_ result: SpeedResult) {
        gate.withLock {
            lastResult = result
            running = nil
        }
    }

    /// Stops the running test; it ends as cancelled. Nothing happens when none runs.
    public func cancel() {
        gate.lock()
        running?.cancel()
        gate.unlock()
    }

    public func dispose() {
        gate.lock()
        disposed = true
        running?.cancel()
        gate.unlock()
    }

    /// Kbit/s for `bytes` moved in `ms`; -1 when no time passed.
    static func kbps(bytes: Int, ms: Double) -> Int {
        ms <= 0 ? -1 : Int(min(Double(Int32.max), (Double(bytes) * 8 / ms).rounded()))
    }

    /// The middle sample (the mean of the two middle ones for an even count), in the samples' unit.
    static func median(_ samples: [Double]) -> Double {
        let sorted = samples.sorted()
        guard !sorted.isEmpty else { return 0 }
        let mid = sorted.count / 2
        return sorted.count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2
    }

    /// The mean absolute difference of consecutive samples; 0 for fewer than two.
    static func jitter(_ samples: [Double]) -> Double {
        guard samples.count >= 2 else { return 0 }
        var sum = 0.0
        for i in 1..<samples.count {
            sum += abs(samples[i] - samples[i - 1])
        }
        return sum / Double(samples.count - 1)
    }

    private func measure(at: Int64) async -> SpeedResult {
        var latency = -1
        var jitter = -1
        var down = -1
        var up = -1
        var state = SpeedState.done
        let downBytes = Tally()
        let upBytes = Tally()
        let transport = self.transport
        do {
            let samples = try await probeLatency()
            latency = Int(Self.median(samples).rounded())
            jitter = Int(Self.jitter(samples).rounded())

            let downMs = try await runStreams(cap: Self.maxDownloadBytes, chunk: Self.downloadChunk) { ask in
                try await transport.download(bytes: ask, read: { downBytes.add($0) })
            }
            down = Self.kbps(bytes: downBytes.value, ms: downMs)

            let upMs = try await runStreams(cap: Self.maxUploadBytes, chunk: Self.uploadChunk) { ask in
                try await transport.upload(bytes: ask)
                upBytes.add(ask)
            }
            up = Self.kbps(bytes: upBytes.value, ms: upMs)

            if downBytes.value >= Self.maxDownloadBytes || upBytes.value >= Self.maxUploadBytes {
                state = .capped
            }
        } catch {
            if Task.isCancelled {
                state = .cancelled
            } else {
                Log.write("speed test failed")
                state = .failed
            }
        }
        return SpeedResult(
            state: state.rawValue, downKbps: down, upKbps: up, latencyMs: latency, jitterMs: jitter, atUnixMin: at,
            bytes: Int64(downBytes.value + upBytes.value))
    }

    /// The latency samples, in ms, all of them inside `latencyLimit` or the test fails: a hung probe never stalls it.
    private func probeLatency() async throws -> [Double] {
        let limitNs = UInt64(Self.latencyLimit * 1_000_000_000)
        let transport = self.transport
        let probe: [Double]? = try await withThrowingTaskGroup(of: [Double]?.self) { group in
            group.addTask {
                try await Task.sleep(nanoseconds: limitNs)
                return nil
            }
            group.addTask { [self] in
                var samples: [Double] = []
                for _ in 0..<Self.latencySamples {
                    let t = nowMs()
                    try await transport.download(bytes: 0, read: { _ in })
                    samples.append(nowMs() - t)
                }
                return samples
            }
            let first = try await group.next() ?? nil
            group.cancelAll()
            return first
        }
        guard let probe else { throw SpeedFailure.timedOut }
        return probe
    }

    /// Runs `streams` workers that each take the next chunk of the `cap` until it is all asked for or the phase limit
    /// passes (which is a normal end, not a failure). Returns the elapsed ms. A worker's failure stops the rest and is
    /// rethrown; a cancel throws `CancellationError`.
    private func runStreams(cap: Int, chunk: Int, one: @escaping @Sendable (Int) async throws -> Void) async throws
        -> Double
    {
        let asked = Tally()
        let start = nowMs()
        let limitNs = UInt64(phaseLimit * 1_000_000_000)
        try await withThrowingTaskGroup(of: Bool.self) { group in
            group.addTask {
                try await Task.sleep(nanoseconds: limitNs)
                return true
            }
            for _ in 0..<Self.streams {
                group.addTask {
                    while true {
                        let before = asked.take(chunk)
                        if before >= cap {
                            return false
                        }
                        try await one(min(chunk, cap - before))
                    }
                }
            }
            var workers = Self.streams
            while workers > 0, let timedOut = try await group.next() {
                if timedOut {
                    break
                }
                workers -= 1
            }
            group.cancelAll()
        }
        try Task.checkCancellation()
        return nowMs() - start
    }
}

/// Moves the bytes over HTTPS to `speed.cloudflare.com` and nowhere else. A redirect is never followed, and any reply
/// that is not a 200 fails the request. Nothing about a request is logged.
public final class CloudflareSpeedTransport: SpeedTransport {
    /// The only host this class talks to.
    public static let host = "speed.cloudflare.com"

    private let zeros = Data(count: SpeedTest.uploadChunk)

    public init() {}

    public func download(bytes: Int, read: @escaping @Sendable (Int) -> Void) async throws {
        guard let url = URL(string: "https://\(Self.host)/__down?bytes=\(bytes)") else {
            throw SpeedFailure.badAddress
        }
        try Task.checkCancellation()
        let delegate = DownloadDelegate(read: read)
        let session = URLSession(configuration: .ephemeral, delegate: delegate, delegateQueue: nil)
        defer { session.finishTasksAndInvalidate() }
        let task = session.dataTask(with: URLRequest(url: url))
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
                delegate.begin(continuation)
                task.resume()
            }
        } onCancel: {
            task.cancel()
        }
    }

    public func upload(bytes: Int) async throws {
        guard let url = URL(string: "https://\(Self.host)/__up") else { throw SpeedFailure.badAddress }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        let body = bytes >= zeros.count ? zeros : Data(zeros.prefix(bytes))
        let session = URLSession(configuration: .ephemeral)
        defer { session.finishTasksAndInvalidate() }
        let (_, response) = try await session.upload(for: request, from: body, delegate: NoRedirects())
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw SpeedFailure.badStatus }
    }
}

/// Why a speed test leg failed: no usable URL, an answer other than 200, or no answer in time.
enum SpeedFailure: Error {
    case badAddress
    case badStatus
    case timedOut
}

/// Counts each read of one download, refuses anything but a 200 (a redirect comes back as its own status and fails
/// here) and ends the continuation once.
private final class DownloadDelegate: NSObject, URLSessionDataDelegate, @unchecked Sendable {
    private let read: @Sendable (Int) -> Void
    private let gate = NSLock()
    private var continuation: CheckedContinuation<Void, Error>?
    private var failure: Error?
    // A cancel can end the task before `begin`: the end is kept and `begin` resumes at once, so nothing hangs.
    private var ended = false
    private var endCause: Error?

    init(read: @escaping @Sendable (Int) -> Void) {
        self.read = read
    }

    func begin(_ continuation: CheckedContinuation<Void, Error>) {
        gate.lock()
        let early = ended
        let cause = endCause
        if !early {
            self.continuation = continuation
        }
        gate.unlock()
        guard early else { return }
        if let cause {
            continuation.resume(throwing: cause)
        } else {
            continuation.resume()
        }
    }

    func urlSession(
        _ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse,
        completionHandler: @escaping (URLSession.ResponseDisposition) -> Void
    ) {
        if (response as? HTTPURLResponse)?.statusCode == 200 {
            completionHandler(.allow)
        } else {
            gate.lock()
            failure = SpeedFailure.badStatus
            gate.unlock()
            completionHandler(.cancel)
        }
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        read(data.count)
    }

    func urlSession(
        _ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void
    ) {
        completionHandler(nil)
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        gate.lock()
        let end = continuation
        continuation = nil
        let cause = failure ?? error
        ended = true
        endCause = cause
        gate.unlock()
        if let cause {
            end?.resume(throwing: cause)
        } else {
            end?.resume()
        }
    }
}
