import XCTest

@testable import EdgepadCore

final class HookConfigInstallerTests: XCTestCase {
    private static let userSettings = """
        {
          "theme": "dark",
          "hooks": {
            "BeforeAgent": [
              { "matcher": "*", "hooks": [ { "name": "mine", "type": "command", "command": "echo hi" } ] }
            ],
            "AfterTool": [
              { "hooks": [ { "name": "other", "type": "command", "command": "echo tool" } ] }
            ]
          },
          "mcpServers": { "a": { "command": "x", "args": [ 1, 3 ] } }
        }
        """

    private let app = "/opt/edgepad/edgepad"
    private var dir = ""
    private var settings: IntegrationSettings!

    override func setUpWithError() throws {
        dir = (NSTemporaryDirectory() as NSString).appendingPathComponent("edgepad-hookcfg-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        settings = IntegrationSettings(path: (dir as NSString).appendingPathComponent(IntegrationSettings.fileName))
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: dir)
    }

    private func make(version: String = "5.0.0", app: String? = nil) throws -> HookConfigInstaller {
        HookConfigInstaller(
            bundle: try RealBundle.source(), settings: settings, home: (dir as NSString).appendingPathComponent("home"),
            version: version, appPath: app ?? self.app)
    }

    private func read(_ file: String) throws -> [String: Any] {
        let data = try Data(contentsOf: URL(fileURLWithPath: file))
        return try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    private func text(_ file: String) throws -> String {
        try String(contentsOfFile: file, encoding: .utf8)
    }

    private func write(_ file: String, _ content: String) throws {
        try FileManager.default.createDirectory(
            atPath: (file as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        try content.write(toFile: file, atomically: true, encoding: .utf8)
    }

    private func exists(_ file: String) -> Bool {
        FileManager.default.fileExists(atPath: file)
    }

    // Every hook entry named edgepad, over every event and group.
    private func ours(_ root: [String: Any]) -> [[String: Any]] {
        var found: [[String: Any]] = []
        for (_, groups) in root["hooks"] as? [String: Any] ?? [:] {
            for group in groups as? [[String: Any]] ?? [] {
                for hook in group["hooks"] as? [[String: Any]] ?? [] where hook["name"] as? String == "edgepad" {
                    found.append(hook)
                }
            }
        }
        return found
    }

    func testGeminiConnectOnAMissingFileWritesOnlyOurFiveEntries() throws {
        let installer = try make()

        XCTAssertEqual(installer.connect(AgentToken.gemini.rawValue).state, .connected)

        let root = try read(installer.geminiFile)
        let hooks = try XCTUnwrap(root["hooks"] as? [String: Any])
        let names = ["AfterAgent", "BeforeAgent", "Notification", "SessionEnd", "SessionStart"]
        XCTAssertEqual(hooks.keys.sorted(), names)
        XCTAssertEqual(ours(root).count, 5)
        XCTAssertEqual(ours(root)[0]["command"] as? String, "\"\(app)\" --hook gemini session")
        XCTAssertTrue(settings.flag(AgentToken.gemini.rawValue))
        XCTAssertFalse(exists(installer.geminiFile + HookConfigInstaller.backupSuffix))
    }

    func testGeminiConnectKeepsEveryOtherKeyAndBacksUpTheOldFile() throws {
        let installer = try make()
        try write(installer.geminiFile, Self.userSettings)

        _ = installer.connect(AgentToken.gemini.rawValue)

        let root = try read(installer.geminiFile)
        XCTAssertEqual(root["theme"] as? String, "dark")
        let hooks = try XCTUnwrap(root["hooks"] as? [String: Any])
        let tool = try XCTUnwrap(hooks["AfterTool"] as? [[String: Any]])
        XCTAssertEqual((tool[0]["hooks"] as? [[String: Any]])?[0]["command"] as? String, "echo tool")
        let before = try XCTUnwrap(hooks["BeforeAgent"] as? [[String: Any]])
        XCTAssertEqual(before.count, 2)
        XCTAssertEqual((before[0]["hooks"] as? [[String: Any]])?[0]["name"] as? String, "mine")
        XCTAssertNotNil(root["mcpServers"])
        XCTAssertEqual(try text(installer.geminiFile + HookConfigInstaller.backupSuffix), Self.userSettings)
    }

    func testGeminiConnectTwiceIsTheSameFile() throws {
        let installer = try make()
        try write(installer.geminiFile, Self.userSettings)
        _ = installer.connect(AgentToken.gemini.rawValue)
        let once = try text(installer.geminiFile)

        _ = installer.connect(AgentToken.gemini.rawValue)

        XCTAssertEqual(try text(installer.geminiFile), once)
        XCTAssertEqual(ours(try read(installer.geminiFile)).count, 5)
    }

    func testGeminiDisconnectLeavesTheUsersEntriesAndNothingOfOurs() throws {
        let installer = try make()
        try write(installer.geminiFile, Self.userSettings)
        _ = installer.connect(AgentToken.gemini.rawValue)

        XCTAssertEqual(installer.disconnect(AgentToken.gemini.rawValue).state, .notConnected)

        let after = try read(installer.geminiFile)
        let original = try XCTUnwrap(
            JSONSerialization.jsonObject(with: Data(Self.userSettings.utf8)) as? [String: Any])
        XCTAssertTrue(NSDictionary(dictionary: original).isEqual(to: after))
        XCTAssertFalse(settings.flag(AgentToken.gemini.rawValue))
    }

    // Keys out of order, a duplicate key, a number spelled 2.50, odd spacing and a tab: none of it is Edgepad's.
    private static let untidy =
        "{\n\t\"zeta\": 1,\n  \"alpha\" :  2.50,\n  \"zeta\": 3,\n"
        + "  \"hooks\": {\n    \"AfterTool\": [ {\"hooks\":[{\"name\":\"other\",\"command\":\"x\"}]} ],\n"
        + "    \"BeforeAgent\": [\n      {\"hooks\":[{\"name\":\"mine\",\"command\":\"y\"}]}\n    ]\n  },\n"
        + "  \"url\": \"http://a/b\"\n}\n"

    func testGeminiConnectThenDisconnectIsByteForByteForTheUsersFile() throws {
        let installer = try make()
        try write(installer.geminiFile, Self.untidy)

        XCTAssertEqual(installer.connect(AgentToken.gemini.rawValue).state, .connected)

        let connected = try text(installer.geminiFile)
        XCTAssertTrue(connected.contains("\"alpha\" :  2.50"))
        XCTAssertTrue(connected.contains("\"zeta\": 1,\n  \"alpha\""))
        XCTAssertTrue(connected.contains("\"zeta\": 3,"))
        XCTAssertTrue(connected.contains("http://a/b"))
        XCTAssertEqual(ours(try read(installer.geminiFile)).count, 5)

        XCTAssertEqual(installer.disconnect(AgentToken.gemini.rawValue).state, .notConnected)

        XCTAssertEqual(try text(installer.geminiFile), Self.untidy)
    }

    func testGeminiConnectOnAFileWithNoHooksAddsOneMemberAndDisconnectRestoresTheBytes() throws {
        let installer = try make()
        let original = "{\n  \"b\": [1, 2.50],\n  \"a\": null\n}\n"
        try write(installer.geminiFile, original)

        _ = installer.connect(AgentToken.gemini.rawValue)
        XCTAssertTrue(try text(installer.geminiFile).hasPrefix("{\n  \"b\": [1, 2.50],\n  \"a\": null,\n  \"hooks\""))

        _ = installer.disconnect(AgentToken.gemini.rawValue)
        XCTAssertEqual(try text(installer.geminiFile), original)
    }

    func testAnUnchangedConnectDoesNotTouchTheFileAndTheFirstBackupIsKept() throws {
        let installer = try make()
        try write(installer.geminiFile, Self.untidy)
        _ = installer.connect(AgentToken.gemini.rawValue)
        let backup = installer.geminiFile + HookConfigInstaller.backupSuffix
        XCTAssertEqual(try text(backup), Self.untidy)

        // The same app again changes nothing; another app path rewrites our entries but never the first backup.
        _ = installer.connect(AgentToken.gemini.rawValue)
        let once = try text(installer.geminiFile)
        let moved = try make(app: "/opt/other/edgepad")
        XCTAssertEqual(moved.connect(AgentToken.gemini.rawValue).state, .connected)

        XCTAssertNotEqual(try text(installer.geminiFile), once)
        XCTAssertTrue(try text(installer.geminiFile).contains("/opt/other/edgepad"))
        XCTAssertFalse(try text(installer.geminiFile).contains(app))
        XCTAssertEqual(ours(try read(installer.geminiFile)).count, 5)
        XCTAssertEqual(try text(backup), Self.untidy)
    }

    func testGeminiRefusesTwoHooksKeys() throws {
        let installer = try make()
        let content = "{ \"hooks\": {}, \"hooks\": {} }"
        try write(installer.geminiFile, content)

        XCTAssertEqual(installer.connect(AgentToken.gemini.rawValue).state, .failed)
        XCTAssertEqual(try text(installer.geminiFile), content)
    }

    func testGeminiDisconnectWithNoFileIsFine() throws {
        let installer = try make()

        XCTAssertEqual(installer.disconnect(AgentToken.gemini.rawValue).state, .notConnected)
        XCTAssertFalse(exists(installer.geminiFile))
    }

    func testGeminiRefusesAConfigItCannotRewriteSafelyAndLeavesItAlone() throws {
        let bad = [
            "{ \"theme\": \"dark\", }", "{ // note\n \"theme\": \"dark\" }", "[1,2]", "not json",
            "{ \"hooks\": 3 }", "{ \"hooks\": { \"BeforeAgent\": {} } }",
        ]
        for content in bad {
            let installer = try make()
            try write(installer.geminiFile, content)

            XCTAssertEqual(installer.connect(AgentToken.gemini.rawValue).state, .failed, content)

            XCTAssertEqual(try text(installer.geminiFile), content)
            XCTAssertFalse(settings.flag(AgentToken.gemini.rawValue))
            XCTAssertFalse(exists(installer.geminiFile + HookConfigInstaller.backupSuffix))
        }
    }

    func testGeminiDisconnectOfAnUnparseableFileReportsAndKeepsItAndTheSwitchOff() throws {
        let installer = try make()
        settings.setFlag(AgentToken.gemini.rawValue, true)
        try write(installer.geminiFile, "{ broken")

        XCTAssertEqual(installer.disconnect(AgentToken.gemini.rawValue).state, .failed)

        XCTAssertEqual(try text(installer.geminiFile), "{ broken")
        XCTAssertFalse(settings.flag(AgentToken.gemini.rawValue))
    }

    func testGeminiRefusesAnAppPathThatCouldBreakTheCommandLine() throws {
        let installer = try make(app: "/opt/a\"b; rm -rf /")

        XCTAssertEqual(installer.connect(AgentToken.gemini.rawValue).state, .failed)
        XCTAssertFalse(exists(installer.geminiFile))
    }

    func testOpenCodeConnectWritesTheStampedPluginAndDisconnectRemovesIt() throws {
        let installer = try make()

        XCTAssertEqual(installer.connect(AgentToken.opencode.rawValue).state, .connected)

        let content = try text(installer.openCodeFile)
        XCTAssertTrue(content.hasPrefix("// " + HookConfigInstaller.openCodeMarker))
        XCTAssertFalse(content.contains("__APP__"))
        XCTAssertFalse(content.contains("__VERSION__"))
        XCTAssertTrue(content.contains("version 5.0.0"))
        XCTAssertTrue(content.contains("const APP = \"\(app)\""))

        XCTAssertEqual(installer.disconnect(AgentToken.opencode.rawValue).state, .notConnected)
        XCTAssertFalse(exists(installer.openCodeFile))
    }

    func testOpenCodeNeverOverwritesOrDeletesAFileThatIsNotOurs() throws {
        let installer = try make()
        try write(installer.openCodeFile, "// my own plugin\n")

        XCTAssertEqual(installer.connect(AgentToken.opencode.rawValue).state, .failed)
        _ = installer.disconnect(AgentToken.opencode.rawValue)

        XCTAssertEqual(try text(installer.openCodeFile), "// my own plugin\n")
    }

    func testEnsureCurrentRewritesAfterAnUpdateAndIsQuietOtherwise() throws {
        _ = try make().connect(AgentToken.opencode.rawValue)
        XCTAssertTrue(try make().ensureCurrent().isEmpty)

        let notices = try make(version: "5.1.0").ensureCurrent()

        XCTAssertEqual(notices.count, 1)
        XCTAssertEqual(notices[0].agent, AgentToken.opencode.rawValue)
        XCTAssertEqual(notices[0].state, .restartSessions)
        XCTAssertTrue(try text(try make().openCodeFile).contains("version 5.1.0"))
    }

    func testClaudeAndCodexStampsCarryNeitherTemplate() throws {
        for agent in [AgentToken.claude.rawValue, AgentToken.codex.rawValue] {
            let folder = (dir as NSString).appendingPathComponent("stamp/" + agent)
            try PluginStamp.write(try RealBundle.source(), agent: agent, folder: folder, appPath: app, version: "5.0.0")

            var files: [String] = []
            let walker = FileManager.default.enumerator(atPath: folder)
            while let relative = walker?.nextObject() as? String { files.append(relative) }
            XCTAssertFalse(files.isEmpty)
            XCTAssertFalse(files.contains { $0.hasSuffix("hooks.gemini.json") || $0.contains("opencode") })
        }
    }

    func testGeminiEventsMapToTheBoard() {
        func event(_ name: String, _ type: String? = nil) -> HookEvent? {
            HookEvent.fromGemini(name, type, sessionId: "g1", cwd: "/p")
        }
        XCTAssertEqual(event("SessionStart")?.event, .start)
        XCTAssertEqual(event("BeforeAgent")?.event, .prompt)
        XCTAssertEqual(event("Notification", "ToolPermission")?.event, .needsYou)
        XCTAssertEqual(event("AfterAgent")?.event, .stop)
        XCTAssertEqual(event("SessionEnd")?.event, .end)
        XCTAssertNil(event("Notification", "Other"))
        XCTAssertNil(event("BeforeTool"))
        XCTAssertEqual(event("SessionStart")?.agent, AgentToken.gemini.rawValue)
    }

    func testOpenCodeEventsMapToTheBoard() {
        func event(_ name: String) -> HookEvent? {
            HookEvent.fromOpenCode(name, sessionId: "o1", cwd: "/p")
        }
        XCTAssertEqual(event("session.created")?.event, .start)
        XCTAssertEqual(event("session.busy")?.event, .prompt)
        XCTAssertEqual(event("permission.asked")?.event, .needsYou)
        XCTAssertEqual(event("session.idle")?.event, .stop)
        XCTAssertEqual(event("session.deleted")?.event, .end)
        XCTAssertNil(event("message.updated"))
        XCTAssertEqual(event("session.idle")?.agent, AgentToken.opencode.rawValue)
    }

    func testTheClientSendsGeminiAndOpenCodeSessionsOnlyAndWhitelistsTheirNames() {
        let gemini = Data(
            #"{"hook_event_name":"Notification","notification_type":"ToolPermission","session_id":"g","cwd":"/p"}"#
                .utf8)
        let sent = HookClient.request(agent: "gemini", kind: HookProtocol.session, input: gemini)
        XCTAssertEqual((sent?["d"] as? [String: Any])?["notification_type"] as? String, "ToolPermission")

        let opencode = Data(#"{"hook_event_name":"session.busy","session_id":"o","cwd":"/p"}"#.utf8)
        XCTAssertNotNil(HookClient.request(agent: "opencode", kind: HookProtocol.session, input: opencode))

        let unknown = Data(#"{"hook_event_name":"BeforeTool","session_id":"g","cwd":"/p"}"#.utf8)
        XCTAssertNil(HookClient.request(agent: "gemini", kind: HookProtocol.session, input: unknown))

        let output = HookClient.run(
            ["--hook", "gemini", HookProtocol.perm], stdin: { Data(#"{"tool_name":"Bash"}"#.utf8) }, file: nil,
            connect: { _ in nil }, replyWait: 0)
        XCTAssertNil(output)
    }
}
