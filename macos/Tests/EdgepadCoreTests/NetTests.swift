import XCTest

@testable import EdgepadCore

final class NetTests: XCTestCase {
    private var folder = ""
    private var settings: IntegrationSettings!

    override func setUpWithError() throws {
        try super.setUpWithError()
        folder = (NSTemporaryDirectory() as NSString).appendingPathComponent("net-" + UUID().uuidString)
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        settings = IntegrationSettings(path: folder + "/integrations.txt")
        XCTAssertTrue(settings.setFlag(IntegrationSettings.stats, true))
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: folder)
        try super.tearDownWithError()
    }

    // ---- wire

    func testANetInfoSurvivesTheWire() {
        let info = NetInfo(
            kind: "wifi", band: "5", linkMbps: 866, signalPct: 72, localIp: "192.168.1.23", gateway: "192.168.1.1")
        let text = NetInfoWire.encode(info)
        XCTAssertEqual(text, "wifi|5|866|72|192.168.1.23|192.168.1.1")
        XCTAssertEqual(NetInfoWire.parse(text), info)
    }

    func testUnknownThingsAreWrittenAsUnknownsAndNothingElse() {
        XCTAssertEqual(NetInfoWire.encode(NetInfo.unknown), "none|?|-1|-1||")
        let odd = NetInfo(kind: "satellite", band: "60", linkMbps: -7, signalPct: 900, localIp: "", gateway: "")
        XCTAssertEqual(NetInfoWire.encode(odd), "other|?|-1|100||")
        XCTAssertNil(NetInfoWire.parse("wifi|5|1|2|3"))
        XCTAssertNil(NetInfoWire.parse("satellite|5|1|2||"))
    }

    func testAnAddressNeverCarriesASeparatorOrMoreThanFifteenCharacters() {
        let info = NetInfo(
            kind: "ethernet", band: "?", linkMbps: 1000, signalPct: -1, localIp: "10.0.0.1|evil/x:y",
            gateway: "255.255.255.255.255.255")
        let fields = NetInfoWire.encode(info).split(separator: "|", omittingEmptySubsequences: false)
        XCTAssertEqual(fields.count, 6)
        XCTAssertTrue(fields[4].count <= 15 && fields[5].count <= 15)
    }

    func testTheSignalIsTwoPercentPerDecibelFromMinusHundred() {
        XCTAssertEqual(NetInfoWire.signalPercent(rssi: -50), 100)
        XCTAssertEqual(NetInfoWire.signalPercent(rssi: -30), 100)
        XCTAssertEqual(NetInfoWire.signalPercent(rssi: -64), 72)
        XCTAssertEqual(NetInfoWire.signalPercent(rssi: -100), 0)
        XCTAssertEqual(NetInfoWire.signalPercent(rssi: -120), 0)
        XCTAssertEqual(NetInfoWire.signalPercent(rssi: 0), -1)
    }

    func testASpeedResultSurvivesTheWire() {
        let result = SpeedResult(
            state: "done", downKbps: 93500, upKbps: 21400, latencyMs: 11, jitterMs: 2, atUnixMin: 29_499_900,
            bytes: 163_400_000)
        let text = SpeedWire.encode(result)
        XCTAssertEqual(text, "done|93500|21400|11|2|29499900|163400000")
        XCTAssertEqual(SpeedWire.parse(text), result)
        let running = SpeedResult(
            state: "running", downKbps: -1, upKbps: -1, latencyMs: -1, jitterMs: -1, atUnixMin: 29_499_900, bytes: 0)
        XCTAssertEqual(SpeedWire.encode(running), "running|-1|-1|-1|-1|29499900|0")
        XCTAssertNil(SpeedWire.parse("odd|1|2|3|4|5|6"))
        XCTAssertNil(SpeedWire.parse("done|1|2|3|4|5"))
    }

    func testTheCalculatorsAgreeWithTheirDefinitions() {
        XCTAssertEqual(SpeedTest.kbps(bytes: 1_250_000, ms: 1000), 10000)
        XCTAssertEqual(SpeedTest.kbps(bytes: 1, ms: 0), -1)
        XCTAssertEqual(SpeedTest.median([5, 1, 3]), 3)
        XCTAssertEqual(SpeedTest.median([4, 1, 3, 2]), 2.5)
        XCTAssertEqual(SpeedTest.jitter([10, 12, 9]), 2.5)
        XCTAssertEqual(SpeedTest.jitter([10]), 0)
        XCTAssertEqual(SpeedTest.maxTotalBytes, 180_000_000)
    }

    func testTheDispatcherPassesSpeedTestOnAndDropsTheRest() {
        let dispatcher = Dispatcher(
            input: FakeInput(), speakers: FakeLevel(level: Level(percent: 50, muted: false)),
            microphone: FakeLevel(level: Level(percent: 50, muted: false)), brightness: FakeBrightness(level: 50),
            display: FakeDisplay(rates: [60], current: 0))
        dispatcher.handle(.setValue(control: ControlId.speedTest.rawValue, value: 1))
        XCTAssertEqual(dispatcher.dropped, 1)

        var asked: [Bool] = []
        dispatcher.speedTest = { asked.append($0) }
        dispatcher.handle(.setValue(control: ControlId.speedTest.rawValue, value: 1))
        dispatcher.handle(.setValue(control: ControlId.speedTest.rawValue, value: 0))
        dispatcher.handle(.setValue(control: ControlId.speedTest.rawValue, value: 2))
        XCTAssertEqual(asked, [true, false])
        XCTAssertEqual(dispatcher.dropped, 2)
    }

    // ---- NET_INFO is read and sent only while bit 5 is set, and only on change

    func testNetInfoIsReadAndSentOnlyWhileTheNetworkBitIsSetAndOnlyWhenItChanges() {
        let net = FakeNet()
        let rig = ReporterRig(net: net)

        rig.reporter.setWatch(StatsReporter.widgetsBit)
        rig.reporter.tick()
        XCTAssertEqual(net.reads, 0)
        XCTAssertEqual(rig.netTexts.count, 0)

        rig.reporter.setWatch(StatsReporter.widgetsBit | StatsReporter.netBit)
        rig.reporter.tick()
        rig.reporter.tick()
        XCTAssertEqual(net.reads, 2)
        XCTAssertEqual(rig.netTexts.count, 1)

        net.info = NetInfo(
            kind: "wifi", band: "5", linkMbps: 433, signalPct: 72, localIp: "192.168.1.23", gateway: "192.168.1.1")
        rig.reporter.tick()
        XCTAssertEqual(rig.netTexts.count, 2)

        rig.reporter.setWatch(StatsReporter.widgetsBit)
        rig.reporter.tick()
        XCTAssertEqual(net.reads, 3)

        rig.reporter.setWatch(StatsReporter.widgetsBit | StatsReporter.netBit)
        rig.reporter.tick()
        XCTAssertEqual(rig.netTexts.count, 3)
    }

    func testTheNetworkBitAloneStartsTheTimerAndASourceThatThrowsPublishesUnknowns() {
        let net = FakeNet()
        net.throwing = true
        let rig = ReporterRig(net: net)

        rig.reporter.setWatch(StatsReporter.netBit)
        rig.reporter.tick()

        XCTAssertTrue(rig.reporter.running)
        XCTAssertEqual(rig.netTexts, [NetInfoWire.encode(NetInfo.unknown)])
    }

    func testNothingIsReadWithoutASource() {
        let rig = ReporterRig(net: nil)
        rig.reporter.setWatch(StatsReporter.netBit)
        rig.reporter.tick()
        XCTAssertTrue(rig.netTexts.isEmpty)
    }

    // ---- the speed test, on a fake transport

    func testADownloadAndAnUploadAreMeasuredFromBytesAndTime() async throws {
        let clock = FakeClock()
        let transport = FakeSpeedServer(clock: clock)
        let test = SpeedTest(
            transport: transport, nowMs: { clock.now }, clock: { Date(timeIntervalSince1970: 1_769_994_000) })
        let reports = Reports()

        let task = try XCTUnwrap(test.start(report: reports.add))
        await task.value

        XCTAssertEqual(reports.all.count, 2)
        XCTAssertEqual(reports.all[0].state, "running")
        XCTAssertEqual(reports.all[0].bytes, 0)
        let result = reports.all[1]
        XCTAssertEqual(result.state, "capped")
        XCTAssertEqual(result.downKbps, 100_000)
        XCTAssertEqual(result.upKbps, 40_000)
        XCTAssertEqual(result.latencyMs, 12)
        XCTAssertEqual(result.jitterMs, 0)
        XCTAssertEqual(result.atUnixMin, 29_499_900)
        XCTAssertEqual(result.bytes, Int64(SpeedTest.maxTotalBytes))
        XCTAssertEqual(test.last, result)
        XCTAssertFalse(test.isRunning)
    }

    func testNoTestEverAsksForMoreThanItsCaps() async throws {
        let clock = FakeClock()
        let transport = FakeSpeedServer(clock: clock)
        let test = SpeedTest(transport: transport, nowMs: { clock.now })

        let task = try XCTUnwrap(test.start(report: { _ in }))
        await task.value

        XCTAssertEqual(transport.askedDown, SpeedTest.maxDownloadBytes)
        XCTAssertEqual(transport.askedUp, SpeedTest.maxUploadBytes)
        XCTAssertEqual(transport.probes, SpeedTest.latencySamples)
    }

    func testAFailedRequestEndsTheTestAsFailedWithWhatWasMeasured() async throws {
        let clock = FakeClock()
        let transport = FakeSpeedServer(clock: clock)
        transport.fails = true
        let test = SpeedTest(transport: transport, nowMs: { clock.now })

        let task = try XCTUnwrap(test.start(report: { _ in }))
        await task.value

        let result = try XCTUnwrap(test.last)
        XCTAssertEqual(result.state, "failed")
        XCTAssertEqual(result.latencyMs, 12)
        XCTAssertEqual(result.downKbps, -1)
        XCTAssertEqual(result.bytes, 0)
    }

    func testThePhaseLimitIsANormalEndNotAFailure() async throws {
        let transport = FakeSpeedServer(clock: FakeClock())
        transport.stalls = true
        let test = SpeedTest(transport: transport, phaseLimit: 0.05)

        let task = try XCTUnwrap(test.start(report: { _ in }))
        await task.value

        let result = try XCTUnwrap(test.last)
        XCTAssertEqual(result.state, "done")
        XCTAssertEqual(result.bytes, 0)
    }

    func testASecondStartIsIgnoredAndACancelEndsTheTestQuickly() async throws {
        let transport = FakeSpeedServer(clock: FakeClock())
        transport.stalls = true
        let test = SpeedTest(transport: transport)

        let task = try XCTUnwrap(test.start(report: { _ in }))
        XCTAssertNil(test.start(report: { _ in }))
        XCTAssertTrue(test.isRunning)
        for _ in 0..<500 where transport.stalled == 0 {
            try await Task.sleep(nanoseconds: 10_000_000)
        }
        XCTAssertGreaterThan(transport.stalled, 0)

        test.cancel()
        await task.value

        XCTAssertEqual(test.last?.state, "cancelled")
        XCTAssertFalse(test.isRunning)
    }

    func testABrokenReportDoesNotNeedAnythingFromTheTest() async throws {
        let clock = FakeClock()
        let test = SpeedTest(transport: FakeSpeedServer(clock: clock), nowMs: { clock.now })
        let task = try XCTUnwrap(test.start(report: { _ in }))
        await task.value
        XCTAssertNotNil(test.last)
    }

    // ---- the host

    func testThePhoneIsToldAboutNetInfoOnlyWhenTheMacCanReadIt() {
        let withNet = host(net: FakeNet(), speed: nil).host
        let withoutNet = host(net: nil, speed: nil).host
        XCTAssertTrue(withNet.features().contains(Feature.netinfo.rawValue))
        XCTAssertFalse(withoutNet.features().contains(Feature.netinfo.rawValue))
        withNet.dispose()
        withoutNet.dispose()
    }

    func testASpeedTestRunsOnlyOnAskAndItsLastResultIsResentToAPhoneThatOpensThePage() throws {
        let clock = FakeClock()
        let transport = FakeSpeedServer(clock: clock)
        let made = host(net: FakeNet(), speed: SpeedTest(transport: transport, nowMs: { clock.now }))

        made.host.onWatch(IntegrationHost.netBit)
        XCTAssertTrue(made.speeds().isEmpty)
        XCTAssertEqual(transport.probes, 0)

        made.host.onSpeedTest(true)
        try wait { made.speeds().contains { $0.hasPrefix("capped|") } }
        XCTAssertEqual(made.speeds().count, 2)

        made.host.onWatch(0)
        made.host.onWatch(IntegrationHost.netBit)
        XCTAssertEqual(made.speeds().count, 3)
        XCTAssertTrue(made.speeds()[2].hasPrefix("capped|"))
        made.host.dispose()
    }

    func testASpeedTestIsIgnoredWhenTheMacHasNoNetworkSource() {
        let transport = FakeSpeedServer(clock: FakeClock())
        let made = host(net: nil, speed: SpeedTest(transport: transport))

        made.host.onSpeedTest(true)

        XCTAssertEqual(transport.probes, 0)
        XCTAssertTrue(made.speeds().isEmpty)
        made.host.dispose()
    }

    private func wait(_ done: () -> Bool) throws {
        let until = Date().addingTimeInterval(30)
        while !done() && Date() < until {
            Thread.sleep(forTimeInterval: 0.01)
        }
        XCTAssertTrue(done())
    }

    private final class Sink {
        private let lock = NSLock()
        private var frames: [Frame] = []

        func add(_ frame: Frame) {
            lock.lock()
            frames.append(frame)
            lock.unlock()
        }

        func texts(_ kind: TextKind) -> [String] {
            lock.lock()
            defer { lock.unlock() }
            return frames.compactMap { frame in
                if case .text(let k, let value) = frame, k == kind.rawValue { return value }
                return nil
            }
        }
    }

    private struct HostRig {
        let host: IntegrationHost
        let sink: Sink

        func speeds() -> [String] { sink.texts(.speedResult) }
    }

    private func host(net: NetInfoSource?, speed: SpeedTest?) -> HostRig {
        let sink = Sink()
        let host = IntegrationHost(
            send: { sink.add($0) }, blobs: nil, settings: settings, parts: DashboardParts(), stats: StubStats(),
            onMain: { $0() }, startTimer: { _, _ in Stoppable {} }, net: net, speedTest: speed)
        return HostRig(host: host, sink: sink)
    }
}

// ---- fakes

private final class ReporterRig {
    let reporter: StatsReporter
    private let lock = NSLock()
    private var frames: [Frame] = []

    init(net: NetInfoSource?) {
        var box: ReporterRig?
        reporter = StatsReporter(
            source: StubStats(), send: { box?.record($0) }, startTimer: { _ in Stoppable {} },
            runPing: { $0() }, runDisks: { $0() }, net: net)
        box = self
    }

    var netTexts: [String] {
        lock.lock()
        defer { lock.unlock() }
        return frames.compactMap { frame in
            if case .text(let k, let value) = frame, k == TextKind.netInfo.rawValue { return value }
            return nil
        }
    }

    private func record(_ frame: Frame) {
        lock.lock()
        frames.append(frame)
        lock.unlock()
    }
}

private final class StubStats: StatsSource {
    func readFast() throws -> StatsSample {
        StatsSample(
            cpu: 1, ram: 2, gpu: 3, vram: 4, down: 5, up: 6, ping: -1, loss: -1, cpuTemp: -1, gpuTemp: -1,
            gpuPower: -1)
    }

    func readInfo() throws -> StatsInfo {
        StatsInfo(cpuName: "cpu", ramMiB: 1, gpuName: "gpu", vramMiB: 1, netName: "net")
    }

    func readDisks() throws -> [DiskEntry] { [] }

    func ping() -> Int { 5 }
}

private struct NetFailure: Error {}

private final class FakeNet: NetInfoSource {
    private let lock = NSLock()
    private var count = 0
    var info = NetInfo(
        kind: "wifi", band: "5", linkMbps: 866, signalPct: 72, localIp: "192.168.1.23", gateway: "192.168.1.1")
    var throwing = false

    var reads: Int {
        lock.lock()
        defer { lock.unlock() }
        return count
    }

    func read() throws -> NetInfo {
        lock.lock()
        count += 1
        lock.unlock()
        if throwing { throw NetFailure() }
        return info
    }
}

private final class FakeClock: @unchecked Sendable {
    private let lock = NSLock()
    private var ms = 0.0

    var now: Double {
        lock.lock()
        defer { lock.unlock() }
        return ms
    }

    func advance(_ by: Double) {
        lock.lock()
        ms += by
        lock.unlock()
    }
}

private final class Reports: @unchecked Sendable {
    private let lock = NSLock()
    private var results: [SpeedResult] = []

    var all: [SpeedResult] {
        lock.lock()
        defer { lock.unlock() }
        return results
    }

    func add(_ result: SpeedResult) {
        lock.lock()
        results.append(result)
        lock.unlock()
    }
}

/// A server at 12 ms latency, 100 Mbit/s down and 40 Mbit/s up, in a clock the test moves itself.
private final class FakeSpeedServer: SpeedTransport, @unchecked Sendable {
    private let clock: FakeClock
    private let lock = NSLock()
    private var down = 0
    private var up = 0
    private var probeCount = 0
    private var stalledCount = 0
    var fails = false
    var stalls = false

    init(clock: FakeClock) {
        self.clock = clock
    }

    var askedDown: Int { locked { down } }
    var askedUp: Int { locked { up } }
    var probes: Int { locked { probeCount } }
    var stalled: Int { locked { stalledCount } }

    func download(bytes: Int, read: @escaping @Sendable (Int) -> Void) async throws {
        if bytes == 0 {
            locked { probeCount += 1 }
            clock.advance(12)
            return
        }
        if fails { throw NetFailure() }
        locked { down += bytes }
        if stalls { try await stall() }
        clock.advance(Double(bytes) * 8 / 100_000)
        read(bytes)
    }

    func upload(bytes: Int) async throws {
        locked { up += bytes }
        if stalls { try await stall() }
        clock.advance(Double(bytes) * 8 / 40_000)
    }

    private func stall() async throws {
        locked { stalledCount += 1 }
        try await Task.sleep(nanoseconds: 60_000_000_000)
    }

    private func locked<T>(_ body: () -> T) -> T {
        lock.lock()
        defer { lock.unlock() }
        return body()
    }
}
