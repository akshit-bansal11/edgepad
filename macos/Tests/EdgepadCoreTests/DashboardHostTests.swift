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

    func testAFailedWeatherFetchIsAskedAgainOnceAfterTheBackOff() throws {
        let clock = TestClock()
        let http = FakeHttp { _ in nil }
        let parts = DashboardParts(weather: WeatherClient(http: http, clock: { clock.now }))
        XCTAssertTrue(settings.setFlag(IntegrationSettings.weather, true))
        XCTAssertTrue(settings.set(IntegrationSettings.city, "Berlin"))
        var actions: [() -> Void] = []
        let host = IntegrationHost(
            send: { _ in }, blobs: nil, settings: settings, parts: parts, onMain: { $0() },
            startTimer: { action, _ in
                actions.append(action)
                return Stoppable {}
            }, weatherRetryAfter: 0.3)

        host.onWatch(IntegrationHost.weatherBit)
        let first = try XCTUnwrap(actions.first)
        first()
        XCTAssertTrue(wait { http.seen.count >= 1 })
        Thread.sleep(forTimeInterval: 0.1)
        clock.advance(WeatherClient.retryAfter + 1)

        XCTAssertTrue(wait { http.seen.count >= 2 })
        clock.advance(WeatherClient.retryAfter + 1)
        Thread.sleep(forTimeInterval: 0.8)
        XCTAssertEqual(http.seen.count, 2)
        host.dispose()
    }

    private func wait(_ condition: () -> Bool) -> Bool {
        let deadline = Date().addingTimeInterval(5)
        while Date() < deadline {
            if condition() { return true }
            Thread.sleep(forTimeInterval: 0.01)
        }
        return false
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

    private func gitHubParts(_ http: FakeHttp, signedIn: @escaping () -> Bool) -> DashboardParts {
        let secrets = MemorySecrets()
        _ = secrets.set(GitHubApp.tokenName, "ghu_SecretTokenValue1234567890abcdef")
        _ = secrets.set(GitHubApp.clientName, GitHubApp.clientId)
        let clock = TestClock()
        XCTAssertTrue(settings.setFlag(IntegrationSettings.developer, true))
        XCTAssertTrue(settings.setFlag(IntegrationSettings.gitHub, true))
        return DashboardParts(
            gitHub: GitHubApi(store: secrets, http: http, clock: { clock.now }), gitHubSignedIn: signedIn)
    }

    private func gitHubReplies() -> FakeHttp {
        FakeHttp { request in
            FakeHttp.json200(request.url?.path == "/graphql" ? GitHubParseTests.normal : #"[{"id":"1"}]"#)
        }
    }

    func testASignInOnTheGitHubPageRefreshesOnceAndNotAgainWithinAFewSeconds() {
        let http = gitHubReplies()
        let host = newHost(gitHubParts(http, signedIn: { true }))
        host.onWatch(IntegrationHost.gitHubBit)
        XCTAssertEqual(timers, 1)

        host.signedInChanged()
        XCTAssertTrue(wait { http.seen.count >= 1 })
        Thread.sleep(forTimeInterval: 0.1)
        host.signedInChanged()
        Thread.sleep(forTimeInterval: 0.1)

        XCTAssertEqual(http.seen.count, 1)
        XCTAssertEqual(timers, 1)
        host.dispose()
    }

    func testASignInWhileTheGitHubPageIsNotWatchedAsksNothingAndStartsNoTimer() {
        let http = gitHubReplies()
        let host = newHost(gitHubParts(http, signedIn: { true }))
        host.onWatch(IntegrationHost.widgetsBit)

        host.signedInChanged()
        Thread.sleep(forTimeInterval: 0.1)

        XCTAssertEqual(http.seen.count, 0)
        XCTAssertEqual(timers, 0)
        XCTAssertTrue(sent.isEmpty)
        host.dispose()
    }

    func testASignOutWhileWatchingSaysNoPermissionAndAsksNothing() throws {
        let http = gitHubReplies()
        let host = newHost(gitHubParts(http, signedIn: { false }))
        host.onWatch(IntegrationHost.gitHubBit)

        host.signedInChanged()
        Thread.sleep(forTimeInterval: 0.1)

        XCTAssertEqual(http.seen.count, 0)
        XCTAssertEqual(sent.count, 1)
        guard case .text(let kind, let value) = try XCTUnwrap(sent.first) else {
            return XCTFail("not a text frame")
        }
        XCTAssertEqual(kind, TextKind.unavailable.rawValue)
        XCTAssertEqual(value, Feature.github.rawValue + ":" + UnavailableReason.noPermission.rawValue)
        host.dispose()
    }

    private final class RecordingOpener: UrlOpening {
        private let lock = NSLock()
        private var urls: [URL] = []

        var opened: [String] { lock.withLock { urls.map { $0.absoluteString } } }

        func open(_ url: URL) -> Bool {
            lock.withLock { urls.append(url) }
            return true
        }
    }

    func testOpenGitOpensOnlyWhatThisMacFetchedForTheCurrentRevisionAndIndex() async {
        let secrets = MemorySecrets()
        _ = secrets.set(GitHubApp.tokenName, "ghu_SecretTokenValue1234567890abcdef")
        _ = secrets.set(GitHubApp.clientName, GitHubApp.clientId)
        let clock = TestClock()
        let api = GitHubApi(store: secrets, http: gitHubReplies(), clock: { clock.now })
        XCTAssertTrue(settings.setFlag(IntegrationSettings.developer, true))
        XCTAssertTrue(settings.setFlag(IntegrationSettings.gitHub, true))
        let opener = RecordingOpener()
        let host = newHost(DashboardParts(gitHub: api, gitHubSignedIn: { true }, urlOpener: opener))
        XCTAssertFalse(host.openGit("github/1/0"))

        _ = await api.refresh()

        // Index 0 and 1 are the repositories, 2 the first item of "mine".
        XCTAssertTrue(host.openGit("github/1/2"))
        XCTAssertTrue(wait { opener.opened.count == 1 })
        XCTAssertEqual(opener.opened, ["https://github.com/me/my_repo-2/pull/3"])

        // Index 6 is a line without an address, 7 is the one allow-listed install address, 8 is past the end.
        for wire in ["github/2/0", "github/1/6", "github/1/8", "gitlab/1/0", "https://evil.example/"] {
            XCTAssertFalse(host.openGit(wire), wire)
        }
        try? await Task.sleep(nanoseconds: 100_000_000)
        XCTAssertEqual(opener.opened.count, 1)

        XCTAssertTrue(host.openGit("github/1/7"))
        XCTAssertTrue(wait { opener.opened.count == 2 })
        XCTAssertEqual(opener.opened.last, GitHubApp.installUrl)
        host.dispose()
    }

    func testOpenGitAllowsAFewOpensASecondAndRefusesTheRestUntilTheWindowPasses() async {
        let secrets = MemorySecrets()
        _ = secrets.set(GitHubApp.tokenName, "ghu_SecretTokenValue1234567890abcdef")
        _ = secrets.set(GitHubApp.clientName, GitHubApp.clientId)
        let clock = TestClock()
        let api = GitHubApi(store: secrets, http: gitHubReplies(), clock: { clock.now })
        _ = await api.refresh()
        XCTAssertTrue(settings.setFlag(IntegrationSettings.developer, true))
        XCTAssertTrue(settings.setFlag(IntegrationSettings.gitHub, true))
        let opener = RecordingOpener()
        let host = newHost(DashboardParts(gitHub: api, gitHubSignedIn: { true }, urlOpener: opener))

        XCTAssertTrue(host.openGit("github/1/2"))
        XCTAssertTrue(host.openGit("github/1/2"))
        XCTAssertTrue(host.openGit("github/1/2"))
        XCTAssertFalse(host.openGit("github/1/2"))
        XCTAssertTrue(wait { opener.opened.count == 3 })
        host.dispose()
    }

    func testAHostMadeAgainOnTheSamePartsKeepsTheOpenLimit() async {
        let secrets = MemorySecrets()
        _ = secrets.set(GitHubApp.tokenName, "ghu_SecretTokenValue1234567890abcdef")
        _ = secrets.set(GitHubApp.clientName, GitHubApp.clientId)
        let clock = TestClock()
        let api = GitHubApi(store: secrets, http: gitHubReplies(), clock: { clock.now })
        _ = await api.refresh()
        XCTAssertTrue(settings.setFlag(IntegrationSettings.developer, true))
        XCTAssertTrue(settings.setFlag(IntegrationSettings.gitHub, true))
        let opener = RecordingOpener()
        let parts = DashboardParts(gitHub: api, gitHubSignedIn: { true }, urlOpener: opener)

        let first = newHost(parts)
        for _ in 0..<3 {
            XCTAssertTrue(first.openGit("github/1/2"))
        }
        first.dispose()
        let second = newHost(parts)

        XCTAssertFalse(second.openGit("github/1/2"))
        XCTAssertTrue(wait { opener.opened.count == 3 })
        second.dispose()
    }

    func testTheThrottleCountsOpensPerWindowAndRemembersRefreshTimes() {
        let throttle = DashboardThrottle()
        let start = Date(timeIntervalSince1970: 1_795_089_600)
        for _ in 0..<3 {
            XCTAssertTrue(throttle.allowOpen(start))
        }
        XCTAssertFalse(throttle.allowOpen(start.addingTimeInterval(9)))
        XCTAssertTrue(throttle.allowOpen(start.addingTimeInterval(10)))

        XCTAssertNil(throttle.lastRefresh("github"))
        throttle.refreshed("github", at: start)
        XCTAssertEqual(throttle.lastRefresh("github"), start)
        throttle.forgetRefresh("github")
        XCTAssertNil(throttle.lastRefresh("github"))
    }

    func testOpenGitDoesNothingWhileGitHubIsOffOrThereIsNoOpener() async {
        let secrets = MemorySecrets()
        _ = secrets.set(GitHubApp.tokenName, "ghu_SecretTokenValue1234567890abcdef")
        _ = secrets.set(GitHubApp.clientName, GitHubApp.clientId)
        let clock = TestClock()
        let api = GitHubApi(store: secrets, http: gitHubReplies(), clock: { clock.now })
        _ = await api.refresh()
        XCTAssertTrue(settings.setFlag(IntegrationSettings.developer, true))
        XCTAssertTrue(settings.setFlag(IntegrationSettings.gitHub, true))
        let opener = RecordingOpener()
        let host = newHost(DashboardParts(gitHub: api, gitHubSignedIn: { true }, urlOpener: opener))

        XCTAssertTrue(settings.setFlag(IntegrationSettings.gitHub, false))
        XCTAssertFalse(host.openGit("github/1/0"))

        XCTAssertTrue(settings.setFlag(IntegrationSettings.gitHub, true))
        let bare = newHost(DashboardParts(gitHub: api, gitHubSignedIn: { true }))
        XCTAssertFalse(bare.openGit("github/1/0"))
        try? await Task.sleep(nanoseconds: 100_000_000)
        XCTAssertTrue(opener.opened.isEmpty)
        host.dispose()
        bare.dispose()
    }

    func testTheDispatcherHandsAGitOpenTextToTheSessionAndDropsItWithoutOne() {
        let dispatcher = Dispatcher(
            input: FakeInput(), speakers: FakeLevel(level: Level(percent: 50, muted: false)),
            microphone: FakeLevel(level: Level(percent: 50, muted: false)), brightness: FakeBrightness(level: 50),
            display: FakeDisplay(rates: [60], current: 0))
        let frame = Frame.text(kind: TextKind.gitOpen.rawValue, value: "github/1/0")
        dispatcher.handle(frame)
        XCTAssertEqual(dispatcher.dropped, 1)

        var heard: [String] = []
        dispatcher.gitOpen = { wire in
            heard.append(wire)
            return wire.hasSuffix("/0")
        }
        dispatcher.handle(frame)
        dispatcher.handle(.text(kind: TextKind.gitOpen.rawValue, value: "github/1/1"))
        XCTAssertEqual(heard, ["github/1/0", "github/1/1"])
        XCTAssertEqual(dispatcher.dropped, 2)
    }

    func testAgentsAreNamedAndTheirSessionsSentOnStartAndOnChange() {
        settings.setFlag(IntegrationSettings.claude, true)
        settings.setFlag(IntegrationSettings.developer, true)
        let (parts, _) = agentParts()
        let host = newHost(parts)

        XCTAssertEqual(host.features(), ["claude", "developer"])
        host.start()
        XCTAssertEqual(sent, [.text(kind: TextKind.sessions.rawValue, value: "")])
        parts.sessions?.apply(
            HookEvent(agent: AgentToken.claude.rawValue, sessionId: "s", event: .prompt, cwd: "/w/app"))
        XCTAssertEqual(sent.last, .text(kind: TextKind.sessions.rawValue, value: "claude:busy:-1:app"))
        host.dispose()
        parts.sessions?.apply(HookEvent(agent: AgentToken.claude.rawValue, sessionId: "s", event: .stop, cwd: ""))
        XCTAssertEqual(sent.count, 2)
    }

    func testAnAgentIsNamedOnlyOnceItsHookWasHeard() {
        settings.setFlag(IntegrationSettings.claude, true)
        settings.setFlag(IntegrationSettings.developer, true)
        settings.setFlag(IntegrationSettings.codex, true)
        let store = UsageStore(path: folder + "/heard.json", now: Date())
        let parts = DashboardParts(sessions: SessionBoard(), agentHeard: { store.lastHeard($0) >= 0 })
        let host = newHost(parts)
        XCTAssertEqual(host.features(), ["developer"])

        store.touch("claude", now: Date())
        XCTAssertEqual(host.features(), ["claude", "developer"])

        // Codex heard but not yet trusted in /hooks is not named.
        store.touch("codex", now: Date())
        XCTAssertEqual(host.features(), ["claude", "developer"])
        settings.setFlag(IntegrationSettings.codexTrusted, true)
        XCTAssertEqual(host.features(), ["claude", "codex", "developer"])

        store.forget("codex")
        XCTAssertEqual(host.features(), ["claude", "developer"])
        host.dispose()
    }

    func testAnAnswerReachesTheBrokerAndItsEndIsSent() {
        settings.setFlag(IntegrationSettings.claude, true)
        settings.setFlag(IntegrationSettings.developer, true)
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
        settings.setFlag(IntegrationSettings.developer, true)
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

    func testALinkLetGoWithoutDisposeStopsCountingItsPhone() {
        let (parts, _) = agentParts()
        var link: DashboardLink? = DashboardLink(phones: phones, makeHost: { [unowned self] in self.newHost(parts) })
        XCTAssertTrue(phones.any)

        link = nil

        XCTAssertNil(link)
        XCTAssertFalse(phones.any)
    }

    func testAnAskThatEndedBeforeItsTurnIsNeverSent() {
        settings.setFlag(IntegrationSettings.claude, true)
        settings.setFlag(IntegrationSettings.developer, true)
        let (parts, _) = agentParts()
        var queued: [() -> Void] = []
        var blobFrames: [Frame] = []
        let blobs = BlobSender(link: .network, schedule: { _, work in work() }, lastInput: { -.infinity })
        blobs.output = {
            blobFrames.append($0)
            return true
        }
        let host = IntegrationHost(
            send: { [unowned self] in self.sent.append($0) }, blobs: blobs, settings: settings, parts: parts,
            onMain: { queued.append($0) })

        host.ask(PermPrompt(id: "0123456789ab", complete: true, body: "claude\nBash\nls"))
        host.gone("0123456789ab")
        host.ask(PermPrompt(id: "ba9876543210", complete: true, body: "claude\nBash\nls"))
        for work in queued {
            work()
        }

        let begins = blobFrames.filter {
            if case .blobBegin = $0 { return true }
            return false
        }
        XCTAssertEqual(begins, [.blobBegin(kind: BlobKind.permAsk.rawValue, size: 14, label: "ba9876543210/1")])
        XCTAssertEqual(sent, [.text(kind: TextKind.permGone.rawValue, value: "0123456789ab")])
        host.dispose()
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
        dispatcher.handle(.setValue(control: ControlId.dashWatch.rawValue, value: 63))
        dispatcher.handle(.setValue(control: ControlId.dashWatch.rawValue, value: 64))
        dispatcher.handle(.text(kind: TextKind.permAnswer.rawValue, value: "0123456789ab/a"))
        dispatcher.handle(.text(kind: TextKind.permAnswer.rawValue, value: "0123456789ab/d"))
        XCTAssertEqual(masks, [31, 63])
        XCTAssertEqual(wires, ["0123456789ab/a", "0123456789ab/d"])
        XCTAssertEqual(dispatcher.dropped, 4)
    }
}
