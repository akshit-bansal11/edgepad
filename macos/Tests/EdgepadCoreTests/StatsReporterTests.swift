import XCTest

@testable import EdgepadCore

final class StatsReporterTests: XCTestCase {
    func testCpuPercentIsTheBusyShareAndNeverNegative() {
        let cases: [(Int64, Int64, Int64, Int64, Int)] = [
            (100, 1000, 150, 1100, 50), (100, 1000, 200, 1100, 0), (100, 1000, 100, 1100, 100),
            (100, 1000, 100, 1000, 0), (100, 1000, 50, 900, 0), (100, 1000, 120, 1100, 80), (100, 1000, 300, 1100, 0),
        ]
        for (prevIdle, prevTotal, idle, total, expected) in cases {
            XCTAssertEqual(
                CpuMeter.percent(prevIdle: prevIdle, prevTotal: prevTotal, idle: idle, total: total), expected)
        }
    }

    func testRateIsKibPerSecondAndTheFirstReadingIsZero() {
        let meter = RateMeter()
        XCTAssertEqual(meter.update(1_000_000, nowMs: 1000), 0)
        XCTAssertEqual(meter.update(1_000_000 + 102_400, nowMs: 2000), 100)
        XCTAssertEqual(meter.update(1_000_000 + 102_400 + 51_200, nowMs: 3000), 50)
    }

    func testRateIsZeroWhenTheCounterWrapsOrTheClockStands() {
        let meter = RateMeter()
        _ = meter.update(5000, nowMs: 1000)
        XCTAssertEqual(meter.update(100, nowMs: 2000), 0)
        XCTAssertEqual(meter.update(200_000, nowMs: 2000), 0)
        XCTAssertEqual(meter.update(299_000, nowMs: 3000), 96)
    }

    func testLossIsUnknownThenTheShareOfTheLastTwentyPings() {
        let meter = LossMeter()
        XCTAssertEqual(meter.percent, -1)
        meter.record(false)
        XCTAssertEqual(meter.percent, 100)
        meter.record(true)
        XCTAssertEqual(meter.percent, 50)
        for _ in 0..<LossMeter.window {
            meter.record(true)
        }
        XCTAssertEqual(meter.percent, 0)
        meter.record(false)
        XCTAssertEqual(meter.percent, 5)
    }

    func testNothingRunsBeforeTheFirstNonZeroMask() {
        let rig = Rig()
        rig.reporter.setWatch(0)
        rig.reporter.setWatch(4 | 8 | 16)
        rig.reporter.tick()

        XCTAssertEqual(rig.timersStarted, 0)
        XCTAssertFalse(rig.reporter.running)
        XCTAssertTrue(rig.sent.isEmpty)
        XCTAssertEqual(rig.source.fastReads, 0)
    }

    func testAMaskOfZeroStopsTheTicks() {
        let rig = Rig()
        rig.reporter.setWatch(1)
        rig.reporter.tick()
        let reads = rig.source.fastReads

        rig.reporter.setWatch(0)
        rig.reporter.tick()

        XCTAssertEqual(rig.timersStopped, 1)
        XCTAssertFalse(rig.reporter.running)
        XCTAssertEqual(rig.source.fastReads, reads)
    }

    func testWatchingOnlyWidgetsNeverReadsDisks() {
        let rig = Rig()
        rig.reporter.setWatch(StatsReporter.widgetsBit)
        for _ in 0..<12 {
            rig.reporter.tick()
        }

        XCTAssertEqual(rig.source.diskReads, 0)
        XCTAssertEqual(rig.count(.disks), 0)
        XCTAssertGreaterThan(rig.count(.stats), 0)
        XCTAssertGreaterThan(rig.count(.statsInfo), 0)
    }

    func testTheScreenReadsDisksAtOnceThenEveryTenthTick() {
        let rig = Rig()
        rig.reporter.setWatch(StatsReporter.screenBit)
        for _ in 0..<21 {
            rig.reporter.tick()
        }

        XCTAssertEqual(rig.source.diskReads, 3)
        XCTAssertEqual(rig.count(.disks), 1)
    }

    func testGainingTheScreenRereadsDisksAtOnce() {
        let rig = Rig()
        rig.reporter.setWatch(StatsReporter.widgetsBit)
        rig.reporter.tick()

        rig.reporter.setWatch(StatsReporter.widgetsBit | StatsReporter.screenBit)
        rig.reporter.tick()

        XCTAssertEqual(rig.source.diskReads, 1)
    }

    func testAnIdenticalReadingIsNotSentAgain() {
        let rig = Rig()
        rig.reporter.setWatch(StatsReporter.widgetsBit)
        rig.reporter.tick()
        rig.reporter.tick()
        rig.reporter.tick()

        XCTAssertEqual(rig.count(.stats), 1)
        XCTAssertEqual(rig.count(.statsInfo), 1)

        rig.source.cpu = 77
        rig.reporter.tick()
        XCTAssertEqual(rig.count(.stats), 2)
    }

    func testARestartedWatchSendsEverythingAgain() {
        let rig = Rig()
        rig.reporter.setWatch(StatsReporter.widgetsBit)
        rig.reporter.tick()
        rig.reporter.setWatch(0)
        rig.reporter.setWatch(StatsReporter.widgetsBit)
        rig.reporter.tick()

        XCTAssertEqual(rig.count(.stats), 2)
        XCTAssertEqual(rig.count(.statsInfo), 2)
    }

    func testAThrowingSourcePublishesUnknownsAndTicksOn() {
        let rig = Rig()
        rig.source.throwing = true
        rig.reporter.setWatch(StatsReporter.widgetsBit | StatsReporter.screenBit)
        rig.reporter.tick()
        rig.reporter.tick()

        XCTAssertEqual(rig.values(.stats), ["-1/-1/-1/-1/-1/-1/-1/-1/-1/-1/-1"])
        XCTAssertEqual(rig.count(.statsInfo), 0)
        XCTAssertEqual(rig.count(.disks), 0)

        rig.source.throwing = false
        rig.reporter.tick()
        XCTAssertEqual(rig.count(.stats), 2)
    }

    func testAStuckPingNeverBlocksATickAndNeverOverlaps() {
        let rig = Rig(runPing: { DispatchQueue.global(qos: .utility).async(execute: $0) })
        rig.source.gate = DispatchSemaphore(value: 0)
        rig.reporter.setWatch(StatsReporter.widgetsBit)
        for _ in 0..<10 {
            rig.reporter.tick()
        }
        XCTAssertTrue(wait { rig.source.pingCalls >= 1 })
        XCTAssertEqual(rig.source.pingCalls, 1)
        XCTAssertEqual(rig.count(.stats), 1)

        rig.source.cpu = 5
        rig.reporter.tick()
        XCTAssertEqual(rig.count(.stats), 2)

        rig.source.pingResult = 12
        rig.source.gate?.signal()
        XCTAssertTrue(
            wait {
                rig.reporter.tick()
                return rig.values(.stats).contains { $0.split(separator: "/")[6] == "12" }
            })
    }

    func testAFailedPingCountsAsLoss() {
        let rig = Rig()
        rig.source.pingResult = -1
        rig.reporter.setWatch(StatsReporter.widgetsBit)
        rig.reporter.tick()

        XCTAssertTrue(
            wait {
                rig.reporter.tick()
                return rig.values(.stats).contains { $0.split(separator: "/")[7] == "100" }
            })
    }

    func testAStuckDiskReadNeverBlocksATickAndNeverOverlaps() {
        let rig = Rig(runDisks: { DispatchQueue.global().async(execute: $0) })
        rig.source.diskGate = DispatchSemaphore(value: 0)
        rig.reporter.setWatch(StatsReporter.screenBit)
        for _ in 0..<12 {
            rig.reporter.tick()
        }
        XCTAssertTrue(wait { rig.source.diskReads >= 1 })
        XCTAssertEqual(rig.source.diskReads, 1)
        XCTAssertEqual(rig.count(.disks), 0)

        rig.source.diskGate?.signal()
        XCTAssertTrue(wait { rig.count(.disks) == 1 })
    }

    func testATickIsSkippedWhileThePreviousOneStillReads() {
        let rig = Rig()
        rig.source.fastGate = DispatchSemaphore(value: 0)
        rig.reporter.setWatch(StatsReporter.widgetsBit)
        let done = DispatchSemaphore(value: 0)
        DispatchQueue.global().async {
            rig.reporter.tick()
            done.signal()
        }
        XCTAssertTrue(wait { rig.source.fastReads == 1 })

        rig.reporter.tick()
        rig.reporter.setWatch(0)

        XCTAssertEqual(rig.source.fastReads, 1)
        rig.source.fastGate?.signal()
        XCTAssertEqual(done.wait(timeout: .now() + 5), .success)
        XCTAssertEqual(rig.count(.stats), 0)
    }

    func testStoppingTheReporterTellsTheSourceItIsIdle() {
        let rig = Rig()
        rig.reporter.setWatch(StatsReporter.widgetsBit)
        XCTAssertEqual(rig.source.idleCalls, 0)

        rig.reporter.setWatch(0)
        XCTAssertEqual(rig.source.idleCalls, 1)

        rig.reporter.setWatch(StatsReporter.widgetsBit)
        rig.reporter.dispose()
        XCTAssertEqual(rig.source.idleCalls, 2)
    }

    func testMacStatsShapedNumbersReachTheWire() {
        let rig = Rig()
        rig.reporter.setWatch(StatsReporter.screenBit)
        rig.reporter.tick()

        XCTAssertEqual(rig.values(.disks), ["C:50:500:250"])
        XCTAssertEqual(rig.values(.statsInfo), ["cpu|16384|gpu|8192|net"])
    }

    private func wait(_ condition: () -> Bool) -> Bool {
        let deadline = Date().addingTimeInterval(5)
        while Date() < deadline {
            if condition() { return true }
            Thread.sleep(forTimeInterval: 0.01)
        }
        return false
    }

    private final class Rig {
        let source = FakeStats()
        private let lock = NSLock()
        private var frames: [Frame] = []
        private(set) var timersStarted = 0
        private(set) var timersStopped = 0
        var reporter: StatsReporter!

        // Ping and disks run inline by default, so every tick sees the same readings.
        init(
            runPing: @escaping (@escaping () -> Void) -> Void = { $0() },
            runDisks: @escaping (@escaping () -> Void) -> Void = { $0() }
        ) {
            reporter = StatsReporter(
                source: source,
                send: { [weak self] frame in self?.record(frame) },
                startTimer: { [weak self] _ in
                    self?.timersStarted += 1
                    return Stoppable { self?.timersStopped += 1 }
                },
                runPing: runPing,
                runDisks: runDisks)
        }

        var sent: [Frame] {
            lock.lock()
            defer { lock.unlock() }
            return frames
        }

        func values(_ kind: TextKind) -> [String] {
            sent.compactMap { frame in
                if case .text(let k, let value) = frame, k == kind.rawValue { return value }
                return nil
            }
        }

        func count(_ kind: TextKind) -> Int {
            values(kind).count
        }

        private func record(_ frame: Frame) {
            lock.lock()
            defer { lock.unlock() }
            frames.append(frame)
        }
    }
}

private struct SourceFailure: Error {}

private final class FakeStats: StatsSource {
    private let lock = NSLock()
    var cpu = 10
    var throwing = false
    var gate: DispatchSemaphore?
    var fastGate: DispatchSemaphore?
    var diskGate: DispatchSemaphore?
    private(set) var idleCalls = 0
    var pingResult = 30
    private(set) var fastReads = 0
    private(set) var diskReads = 0
    private var calls = 0

    var pingCalls: Int {
        lock.lock()
        defer { lock.unlock() }
        return calls
    }

    func readFast() throws -> StatsSample {
        fastReads += 1
        fastGate?.wait()
        if throwing { throw SourceFailure() }
        return StatsSample(
            cpu: cpu, ram: 20, gpu: 30, vram: 40, down: 50, up: 60, ping: -1, loss: -1, cpuTemp: 70, gpuTemp: 80,
            gpuPower: 90)
    }

    func readInfo() throws -> StatsInfo {
        if throwing { throw SourceFailure() }
        return StatsInfo(cpuName: "cpu", ramMiB: 16384, gpuName: "gpu", vramMiB: 8192, netName: "net")
    }

    func readDisks() throws -> [DiskEntry] {
        diskReads += 1
        diskGate?.wait()
        if throwing { throw SourceFailure() }
        return [DiskEntry(label: "C", usedPct: 50, totalGiB: 500, freeGiB: 250)]
    }

    func idle() {
        idleCalls += 1
    }

    func ping() -> Int {
        lock.lock()
        calls += 1
        lock.unlock()
        gate?.wait()
        return pingResult
    }
}
