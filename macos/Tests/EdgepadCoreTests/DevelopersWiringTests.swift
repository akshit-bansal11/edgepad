import XCTest

@testable import EdgepadCore

/// The For developers window's rules that need no window: its rows, its developer-mode gate, the token save and the
/// host wiring it relies on.
final class DevelopersWiringTests: XCTestCase {
    private var folder = ""
    private var settings: IntegrationSettings!

    override func setUpWithError() throws {
        try super.setUpWithError()
        folder = (NSTemporaryDirectory() as NSString).appendingPathComponent("devwin-" + UUID().uuidString)
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        settings = IntegrationSettings(path: folder + "/" + IntegrationSettings.fileName)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: folder)
        try super.tearDownWithError()
    }

    private func provider(_ id: String) throws -> DevProvider {
        try XCTUnwrap(DevProviders.find(id))
    }

    private func input(_ change: (inout DevRowInput) -> Void) -> DevRowInput {
        var input = DevRowInput()
        input.developerOn = true
        change(&input)
        return input
    }

    // ---- the registry and the gate

    func testTheRegistryListsFourAiToolsAndFiveGitHosts() {
        XCTAssertEqual(DevProviders.of(.ai).map(\.id), ["claude", "codex", "gemini", "opencode"])
        XCTAssertEqual(DevProviders.of(.git).map(\.id), ["github", "gitlab", "gitea", "bitbucket", "gitee"])
    }

    func testEveryDeveloperKeyNeedsDeveloperMode() {
        for provider in DevProviders.all {
            settings.setFlag(provider.id, true)
            settings.setFlag(IntegrationSettings.developer, false)
            XCTAssertFalse(settings.active(provider.id), provider.id)
            settings.setFlag(IntegrationSettings.developer, true)
            XCTAssertTrue(settings.active(provider.id), provider.id)
        }
        settings.setFlag(IntegrationSettings.developer, false)
        settings.setFlag(IntegrationSettings.stats, true)
        XCTAssertTrue(settings.active(IntegrationSettings.stats))
    }

    // ---- rows

    func testAnAiRowOffersConnectThenDisconnect() throws {
        let claude = try provider("claude")
        let off = DevRowModel.view(claude, input { $0.agent = .notConnected })
        XCTAssertEqual(off.button, .connect)
        XCTAssertTrue(off.buttonEnabled)
        XCTAssertTrue(off.canHowTo)
        XCTAssertFalse(off.dot)

        let onInput = input {
            $0.connected = true
            $0.agent = .connected
        }
        let on = DevRowModel.view(claude, onInput)
        XCTAssertEqual(on.button, .disconnect)
        XCTAssertTrue(on.dot)
        XCTAssertFalse(on.canHowTo)
        XCTAssertEqual(on.line, .agent(.connected))
    }

    func testABusyAiRowSaysWorkingAndLocksItsButton() throws {
        let busy = DevRowModel.view(try provider("gemini"), input { $0.busy = true })
        XCTAssertEqual(busy.line, .working)
        XCTAssertFalse(busy.buttonEnabled)
        XCTAssertFalse(busy.canHowTo)
    }

    func testWithDeveloperModeOffEveryButtonIsDisabled() {
        for provider in DevProviders.all {
            var off = DevRowInput()
            off.developerOn = false
            XCTAssertFalse(DevRowModel.view(provider, off).buttonEnabled, provider.id)
        }
    }

    func testGitLabIsComingSoonWithNoSteps() throws {
        let view = DevRowModel.view(try provider("gitlab"), input { _ in })
        XCTAssertEqual(view.button, .comingSoon)
        XCTAssertFalse(view.buttonEnabled)
        XCTAssertFalse(view.canHowTo)
        for provider in DevProviders.all {
            XCTAssertEqual(DevRowModel.hasSteps(provider.id), provider.id != "gitlab", provider.id)
        }
    }

    func testGitHubRowFollowsItsSignIn() throws {
        let github = try provider("github")
        let idle = DevRowModel.view(github, input { _ in })
        XCTAssertEqual(idle.button, .signIn)
        XCTAssertTrue(idle.canHowTo)

        let unset = DevRowModel.view(github, input { $0.configured = false })
        XCTAssertEqual(unset.line, .notConfigured)
        XCTAssertFalse(unset.buttonEnabled)

        let asking = DevRowModel.view(github, input { $0.signingIn = true })
        XCTAssertEqual(asking.line, .waiting)
        XCTAssertEqual(asking.button, .cancel)
        XCTAssertTrue(asking.buttonEnabled)

        let codeInput = input {
            $0.signingIn = true
            $0.codeShown = true
        }
        let code = DevRowModel.view(github, codeInput)
        XCTAssertEqual(code.line, .notConnected)

        let signedInput = input {
            $0.signedIn = true
            $0.account = "ana"
        }
        let signed = DevRowModel.view(github, signedInput)
        XCTAssertEqual(signed.line, .signedIn("ana"))
        XCTAssertEqual(signed.button, .signOut)
        XCTAssertTrue(signed.dot)
        XCTAssertFalse(signed.canHowTo)
    }

    func testATokenRowOffersAddThenSignOut() throws {
        let gitea = try provider("gitea")
        let add = DevRowModel.view(gitea, input { _ in })
        XCTAssertEqual(add.button, .add)
        XCTAssertTrue(add.canHowTo)
        XCTAssertFalse(DevRowModel.view(gitea, input { $0.saving = true }).buttonEnabled)

        let signedInput = input {
            $0.signedIn = true
            $0.account = "ana"
            $0.saving = true
        }
        let signed = DevRowModel.view(gitea, signedInput)
        XCTAssertEqual(signed.button, .signOut)
        XCTAssertTrue(signed.buttonEnabled)
        XCTAssertTrue(signed.dot)
    }

    func testEveryFailedSaveHasASentenceAndOnlySavedHasNone() {
        XCTAssertNil(DevRowModel.note(.saved))
        let failures: [GitSaveStatus] = [
            .badToken, .badUrl, .badUser, .unauthorized, .forbidden, .notFound, .network, .failed,
        ]
        for status in failures {
            XCTAssertNotNil(DevRowModel.note(status), "\(status)")
        }
        XCTAssertEqual(DevRowModel.note(.badToken), .invalid)
        XCTAssertEqual(DevRowModel.note(.badUser), .invalid)
        XCTAssertEqual(DevRowModel.note(.unauthorized), .unauthorized)
    }

    // ---- saving a token

    private func devHosts(_ http: FakeHttp, _ store: MemorySecrets) -> DevTokenHosts {
        DevTokenHosts(
            hosts: [
                GiteaHost(store: store, settings: settings, http: http),
                BitbucketHost(store: store, settings: settings, http: http),
                GiteeHost(store: store, settings: settings, http: http),
            ], settings: settings)
    }

    func testASavedTokenTurnsTheHostOnAndSignOutTurnsItOff() async {
        let http = FakeHttp { request in
            request.url?.path.hasSuffix("/api/v1/user") == true ? FakeHttp.json200(#"{"login":"ana"}"#) : nil
        }
        let store = MemorySecrets()
        let hosts = devHosts(http, store)
        XCTAssertFalse(hosts.isSignedIn("gitea"))

        let saved = await hosts.save("gitea", token: "abcdefgh12345678", url: "", user: "")
        XCTAssertEqual(saved.status, .saved)
        XCTAssertTrue(hosts.isSignedIn("gitea"))
        XCTAssertTrue(settings.flag("gitea"))
        XCTAssertEqual(hosts.login("gitea"), "ana")
        XCTAssertEqual(store.get(GitTokens.tokenName("gitea")), "abcdefgh12345678")

        hosts.signOut("gitea")
        XCTAssertFalse(hosts.isSignedIn("gitea"))
        XCTAssertFalse(settings.flag("gitea"))
        XCTAssertEqual(hosts.login("gitea"), "")
        XCTAssertNil(store.get(GitTokens.tokenName("gitea")))
    }

    func testARefusedTokenSavesNothingAndSwitchesNothingOn() async {
        let refused = FakeHttp { _ in HttpReply(status: 401, headers: [:], body: Data()) }
        let store = MemorySecrets()
        let hosts = devHosts(refused, store)

        let result = await hosts.save("gitea", token: "abcdefgh12345678", url: "", user: "")
        XCTAssertEqual(result.status, .unauthorized)
        XCTAssertFalse(settings.flag("gitea"))
        XCTAssertNil(store.get(GitTokens.tokenName("gitea")))
        let unknown = await hosts.save("gitlab", token: "abcdefgh12345678", url: "", user: "")
        XCTAssertEqual(unknown.status, .failed)
    }

    // ---- the host

    private func host(_ parts: DashboardParts, timers: @escaping () -> Void = {}) -> IntegrationHost {
        IntegrationHost(
            send: { _ in }, blobs: nil, settings: settings, parts: parts, onMain: { $0() },
            startTimer: { _, _ in
                timers()
                return Stoppable {}
            })
    }

    func testGeminiAndOpenCodeNameThemselvesOnceHeardAndOnlyWithDeveloperMode() {
        for tool in [AgentToken.gemini, AgentToken.opencode] {
            settings.setFlag(tool.rawValue, true)
            let parts = DashboardParts(sessions: SessionBoard(), agentHeard: { _ in true })
            let host = host(parts)
            let feature = DevProviders.find(tool.rawValue)?.featureToken ?? ""
            XCTAssertFalse(host.features().contains(feature), tool.rawValue)
            settings.setFlag(IntegrationSettings.developer, true)
            XCTAssertTrue(host.features().contains(feature), tool.rawValue)
            host.dispose()
            settings.setFlag(IntegrationSettings.developer, false)
        }
        settings.setFlag(IntegrationSettings.developer, true)
        let silent = host(DashboardParts(sessions: SessionBoard(), agentHeard: { _ in false }))
        XCTAssertFalse(silent.features().contains(Feature.gemini.rawValue))
        silent.dispose()
    }

    func testASavedTokenHostNamesItselfOnlyWhileItsSwitchAndDeveloperModeAreOn() {
        let store = MemorySecrets()
        _ = store.set(GitTokens.tokenName("gitea"), "abcdefgh12345678")
        settings.setFlag("gitea", true)
        let gitea = GiteaHost(store: store, settings: settings, http: FakeHttp { _ in nil })
        let host = host(DashboardParts(gitHosts: [gitea]))
        XCTAssertFalse(host.features().contains(Feature.gitea.rawValue))

        settings.setFlag(IntegrationSettings.developer, true)
        XCTAssertTrue(host.features().contains(Feature.gitea.rawValue))

        settings.setFlag(IntegrationSettings.developer, false)
        XCTAssertFalse(host.features().contains(Feature.gitea.rawValue))
        host.dispose()
    }

    func testTheGitTimerStartsForATokenHostOnlyWithDeveloperMode() {
        let store = MemorySecrets()
        _ = store.set(GitTokens.tokenName("gitee"), "abcdefgh12345678")
        settings.setFlag("gitee", true)
        let gitee = GiteeHost(store: store, settings: settings, http: FakeHttp { _ in nil })
        var started = 0
        let host = host(DashboardParts(gitHosts: [gitee]), timers: { started += 1 })
        host.onWatch(IntegrationHost.gitHubBit)
        XCTAssertEqual(started, 0)

        settings.setFlag(IntegrationSettings.developer, true)
        host.onWatch(0)
        host.onWatch(IntegrationHost.gitHubBit)
        XCTAssertEqual(started, 1)
        host.dispose()
    }
}
