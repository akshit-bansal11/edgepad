import XCTest

@testable import EdgepadCore

private final class FixedStats: StatsSource {
    func readFast() throws -> StatsSample {
        StatsSample(
            cpu: 10, ram: 20, gpu: -1, vram: -1, down: 0, up: 0, ping: -1, loss: -1, cpuTemp: -1, gpuTemp: -1,
            gpuPower: -1)
    }

    func readInfo() throws -> StatsInfo {
        StatsInfo(cpuName: "M9", ramMiB: 1024, gpuName: "M9", vramMiB: -1, netName: "en0")
    }

    func readDisks() throws -> [DiskEntry] { [] }

    func ping() -> Int { -1 }
}

final class DashboardHostTests: XCTestCase {
    private var folder = ""
    private var settings: IntegrationSettings!
    private var sent: [Frame] = []
    private var timers = 0
    private let phones = LinkedPhones()

    override func setUpWithError() throws {
        try super.setUpWithError()
        folder = (NSTemporaryDirectory() as NSString).appendingPathComponent("dash-" + UUID().uuidString)
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        settings = IntegrationSettings(path: folder + "/integrations.txt")
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: folder)
        try super.tearDownWithError()
    }

    private func newHost(_ parts: DashboardParts, stats: StatsSource? = nil) -> IntegrationHost {
        IntegrationHost(
            send: { [unowned self] in self.sent.append($0) }, blobs: nil, settings: settings, parts: parts,
            stats: stats, onMain: { $0() },
            startTimer: { [unowned self] _, _ in
                self.timers += 1
                return Stoppable {}
            })
    }

    private func agentParts() -> (DashboardParts, PermissionBroker) {
        let store = UsageStore(path: folder + "/usage.json", now: Date())
        let sessions = SessionBoard()
        let phones = phones
        let broker = PermissionBroker(board: sessions, phoneLinked: { phones.any }, after: { _, _ in })
        let parts = DashboardParts(
            sessions: sessions, limits: LimitsBoard(store: store), spend: SpendBoard(store: store), broker: broker)
        return (parts, broker)
    }

    private func ls() -> PermRequest {
        PermRequest(token: "T", agent: "claude", session: "s", cwd: "", tool: "Bash", input: ToolInput(command: "ls"))
    }

    func testWithEverySwitchOffNothingIsNamedOrStarted() {
        let host = newHost(DashboardParts(), stats: FixedStats())

        XCTAssertEqual(host.features(), [])
        XCTAssertEqual(host.unavailable(), [])
        host.start()
        host.onWatch(31)
        XCTAssertEqual(timers, 0)
        XCTAssertTrue(sent.isEmpty)
        XCTAssertFalse(host.answer("0123456789ab/a"))
    }

    func testStatsStartOnceForTheWidgetsAndAreNamedOnlyWithASource() {
        settings.setFlag(IntegrationSettings.stats, true)
        XCTAssertEqual(newHost(DashboardParts()).unavailable(), ["stats:unsupported"])
        let host = newHost(DashboardParts(), stats: FixedStats())
        XCTAssertEqual(host.features(), ["stats"])

        host.onWatch(IntegrationHost.widgetsBit)
        host.onWatch(IntegrationHost.widgetsBit)
        XCTAssertEqual(timers, 1)
        host.dispose()
    }

    func testAgentsAreNamedAndTheirSessionsSentOnStartAndOnChange() {
        settings.setFlag(IntegrationSettings.claude, true)
        let (parts, _) = agentParts()
        let host = newHost(parts)

        XCTAssertEqual(host.features(), ["claude"])
        host.start()
        XCTAssertEqual(sent, [.text(kind: TextKind.sessions.rawValue, value: "")])
        parts.sessions?.apply(
            HookEvent(agent: AgentToken.claude.rawValue, sessionId: "s", event: .prompt, cwd: "/w/app"))
        XCTAssertEqual(sent.last, .text(kind: TextKind.sessions.rawValue, value: "claude:busy:app"))
        host.dispose()
        parts.sessions?.apply(HookEvent(agent: AgentToken.claude.rawValue, sessionId: "s", event: .stop, cwd: ""))
        XCTAssertEqual(sent.count, 2)
    }

    func testAnAnswerReachesTheBrokerAndItsEndIsSent() {
        settings.setFlag(IntegrationSettings.claude, true)
        let (parts, broker) = agentParts()
        let link = DashboardLink(phones: phones, makeHost: { [unowned self] in self.newHost(parts) })
        var asked: PermPrompt?
        let previous = broker.asked
        broker.asked = { prompt in
            asked = prompt
            previous?(prompt)
        }
        var outcome: PermDecision?
        broker.request(ls()) { outcome = $0 }
        let id = asked?.id ?? ""

        XCTAssertFalse(link.answer("bad"))
        XCTAssertTrue(link.answer(id + "/a"))
        XCTAssertEqual(outcome, .allow)
        XCTAssertEqual(sent.last, .text(kind: TextKind.permGone.rawValue, value: id))
        link.dispose()
    }

    func testAPhoneThatGoesIsUncountedBeforeItsHostSoWaitingAsksDefer() {
        settings.setFlag(IntegrationSettings.claude, true)
        let (parts, broker) = agentParts()
        let link = DashboardLink(phones: phones, makeHost: { [unowned self] in self.newHost(parts) })
        XCTAssertTrue(phones.any)
        var outcome: PermDecision?
        broker.request(ls()) { outcome = $0 }
        XCTAssertNil(outcome)

        link.rebuild()
        XCTAssertNil(outcome)
        link.dispose()
        XCTAssertFalse(phones.any)
        XCTAssertEqual(outcome, .deferred)
    }

    func testDashWatchAndPermAnswerAreDroppedWithoutAHandlerAndOutOfRange() {
        let dispatcher = Dispatcher(
            input: FakeInput(), speakers: FakeLevel(level: Level(percent: 50, muted: false)),
            microphone: FakeLevel(level: Level(percent: 50, muted: false)), brightness: FakeBrightness(level: 50),
            display: FakeDisplay(rates: [60], current: 0))
        dispatcher.handle(.setValue(control: ControlId.dashWatch.rawValue, value: 8))
        dispatcher.handle(.text(kind: TextKind.permAnswer.rawValue, value: "0123456789ab/a"))
        XCTAssertEqual(dispatcher.dropped, 2)

        var masks: [Int] = []
        var wires: [String] = []
        dispatcher.dashWatch = { masks.append($0) }
        dispatcher.permAnswer = { wire in
            wires.append(wire)
            return wire.hasSuffix("/a")
        }
        dispatcher.handle(.setValue(control: ControlId.dashWatch.rawValue, value: 31))
        dispatcher.handle(.setValue(control: ControlId.dashWatch.rawValue, value: 32))
        dispatcher.handle(.text(kind: TextKind.permAnswer.rawValue, value: "0123456789ab/a"))
        dispatcher.handle(.text(kind: TextKind.permAnswer.rawValue, value: "0123456789ab/d"))
        XCTAssertEqual(masks, [31])
        XCTAssertEqual(wires, ["0123456789ab/a", "0123456789ab/d"])
        XCTAssertEqual(dispatcher.dropped, 4)
    }
}
