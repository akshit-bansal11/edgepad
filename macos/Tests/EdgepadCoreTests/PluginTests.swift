import XCTest

@testable import EdgepadCore

/// The repository's plugins/edgepad, the folder bundle.sh copies into the app.
enum RealBundle {
    static func source() throws -> DirectoryPluginSource {
        var directory = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        while true {
            let candidate = directory.appendingPathComponent("plugins/edgepad")
            let template = candidate.appendingPathComponent("hooks/hooks.claude.json").path
            if FileManager.default.fileExists(atPath: template) {
                return DirectoryPluginSource(root: candidate.path)
            }
            let parent = directory.deletingLastPathComponent()
            if parent.path == directory.path { throw CocoaError(.fileNoSuchFile) }
            directory = parent
        }
    }
}

/// A bundle in memory, for entries the real folder must never hold.
private final class MemoryBundle: PluginSource {
    private let files: [String: Data]

    init(_ files: [String: String]) {
        self.files = files.mapValues { Data($0.utf8) }
    }

    func entries() throws -> [String] { files.keys.sorted() }

    func read(_ entry: String, limit: Int) throws -> Data {
        guard let data = files[entry] else { throw CocoaError(.fileNoSuchFile) }
        guard data.count <= limit else { throw PluginBundleError(reason: "A plugin file is over 1 MiB") }
        return data
    }
}

/// Records every run and answers from `answer`; nothing is ever started.
private final class FakeRunner: ProcessRunner {
    struct Run: Equatable {
        let file: String
        let arguments: [String]
        let cwd: String
    }

    private let lock = NSLock()
    private var log: [Run] = []
    var answer: (String, [String]) -> ProcessResult = { _, _ in
        ProcessResult(exitCode: 0, lastLine: #"{"outcome":"ok"}"#)
    }

    var runs: [Run] {
        lock.lock()
        defer { lock.unlock() }
        return log
    }

    func run(_ file: String, _ arguments: [String], cwd: String, timeout: TimeInterval) async -> ProcessResult {
        lock.lock()
        log.append(Run(file: file, arguments: arguments, cwd: cwd))
        let reply = answer
        lock.unlock()
        return reply(file, arguments)
    }
}

final class PluginStampTests: XCTestCase {
    // An app path with a space, a quote, a backslash run and a non-ASCII letter: the stamp must keep it whole.
    private let appPath =
        "/opt/Pro gram/\"q\"/" + String(repeating: String(UnicodeScalar(92)), count: 2) + "Edgé/Edgepad"

    private func stamp(_ agent: String, _ folder: String, _ version: String = "5.0.0") throws {
        let bundle = try RealBundle.source()
        try PluginStamp.write(bundle, agent: agent, folder: folder, appPath: appPath, version: version)
    }
    private var dir = ""

    override func setUpWithError() throws {
        dir = (NSTemporaryDirectory() as NSString).appendingPathComponent("edgepad-stamp-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: dir)
    }

    private func folder(_ agent: String) -> String {
        ((dir as NSString).appendingPathComponent("plug ins é") as NSString).appendingPathComponent(agent)
    }

    private func hooks(_ folder: String) throws -> [String: Any] {
        let path = (folder as NSString).appendingPathComponent("edgepad/hooks/hooks.json")
        let data = try Data(contentsOf: URL(fileURLWithPath: path))
        let root = try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
        return try XCTUnwrap(root["hooks"] as? [String: Any])
    }

    private func firstHook(_ hooks: [String: Any], _ event: String) throws -> [String: Any] {
        let groups = try XCTUnwrap(hooks[event] as? [[String: Any]])
        let inner = try XCTUnwrap(groups.first?["hooks"] as? [[String: Any]])
        return try XCTUnwrap(inner.first)
    }

    private func files(_ folder: String) -> [String] {
        (FileManager.default.enumerator(atPath: folder)?.allObjects as? [String] ?? []).filter { relative in
            var isFolder: ObjCBool = false
            let path = (folder as NSString).appendingPathComponent(relative)
            return FileManager.default.fileExists(atPath: path, isDirectory: &isFolder) && !isFolder.boolValue
        }
    }

    func testClaudeHooksKeepTheAppAndRootPathsWhole() throws {
        let folder = folder(AgentToken.claude.rawValue)
        try PluginStamp.write(
            try RealBundle.source(), agent: AgentToken.claude.rawValue, folder: folder, appPath: appPath,
            version: "5.0.0")

        let hook = try firstHook(try hooks(folder), "PermissionRequest")
        XCTAssertEqual(hook["command"] as? String, appPath)
        let args = try XCTUnwrap(hook["args"] as? [String])
        XCTAssertEqual(args, ["--hook", "claude", "perm"])
        XCTAssertTrue(PluginStamp.rootOf(folder).hasPrefix("/"))

        let relay = try String(
            contentsOfFile: (folder as NSString).appendingPathComponent("edgepad/hooks/relay.ts"), encoding: .utf8)
        XCTAssertTrue(relay.contains("\"" + PluginStamp.jsonEscaped(appPath) + "\""))

        let manifestPath = (folder as NSString).appendingPathComponent("edgepad/.claude-plugin/plugin.json")
        let manifest = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: manifestPath)))
        XCTAssertEqual((manifest as? [String: Any])?["version"] as? String, "5.0.0")
    }

    func testCodexHooksCarryTheQuotedCommandLine() throws {
        let folder = folder(AgentToken.codex.rawValue)
        try PluginStamp.write(
            try RealBundle.source(), agent: AgentToken.codex.rawValue, folder: folder, appPath: appPath,
            version: "5.0.0")

        let hook = try firstHook(try hooks(folder), "PermissionRequest")
        let expected = "\"\(appPath)\" --hook codex perm"
        XCTAssertEqual(hook["command"] as? String, expected)
    }

    func testEveryPlaceholderIsReplaced() throws {
        for agent in AgentToken.allCases.map(\.rawValue) {
            let folder = folder(agent)
            try stamp(agent, folder)
            for file in files(folder) {
                let path = (folder as NSString).appendingPathComponent(file)
                let text = try String(contentsOfFile: path, encoding: .utf8)
                for placeholder in [PluginStamp.app, PluginStamp.root, PluginStamp.version, "__NODE__"] {
                    XCTAssertFalse(text.contains(placeholder), "\(agent) \(file)")
                }
            }
        }
    }

    func testEachAgentGetsOnlyItsOwnFiles() throws {
        let claude = folder(AgentToken.claude.rawValue)
        let codex = folder(AgentToken.codex.rawValue)
        try stamp("claude", claude)
        try stamp("codex", codex)

        let claudeFiles = files(claude)
        let codexFiles = files(codex)
        XCTAssertTrue(claudeFiles.contains(PluginStamp.marketplace))
        XCTAssertTrue(codexFiles.contains(PluginStamp.marketplace))
        XCTAssertTrue(claudeFiles.contains("edgepad/hooks/relay.ts"))
        XCTAssertFalse(codexFiles.contains("edgepad/hooks/relay.ts"))
        XCTAssertFalse(codexFiles.contains("edgepad/scripts/scan-claude.mjs"))
        XCTAssertFalse(codexFiles.contains("edgepad/.claude-plugin/plugin.json"))
        XCTAssertFalse(claudeFiles.contains { $0.hasPrefix("edgepad/.codex-plugin/") })
        XCTAssertFalse(claudeFiles.contains { $0.hasSuffix("hooks.claude.json") || $0.hasSuffix("hooks.codex.json") })
    }

    func testAStampIsAlwaysFreshSoATamperedCopyReverts() throws {
        let folder = folder("claude")
        try stamp("claude", folder)
        let planted = (folder as NSString).appendingPathComponent("edgepad/planted.mjs")
        try "evil".write(toFile: planted, atomically: true, encoding: .utf8)

        try stamp("claude", folder)

        XCTAssertFalse(FileManager.default.fileExists(atPath: planted))
    }

    func testAnEntryLeavingTheFolderIsRefusedAndTheOldCopyStays() throws {
        let folder = folder("claude")
        let good = ["hooks/hooks.claude.json": "{}", "marketplace.template.json": "{}"]
        try PluginStamp.write(MemoryBundle(good), agent: "claude", folder: folder, appPath: appPath, version: "1")
        for evil in ["../evil.json", "hooks/../../evil.json", "/etc/evil.json", "a/../../../evil.json"] {
            var files = good
            files[evil] = "{}"
            XCTAssertThrowsError(
                try PluginStamp.write(
                    MemoryBundle(files), agent: "claude", folder: folder, appPath: appPath, version: "1"),
                evil)
            let kept = (folder as NSString).appendingPathComponent("edgepad")
            XCTAssertTrue(FileManager.default.fileExists(atPath: kept))
        }
    }

    func testAnOversizeEntryOrAMissingTemplateIsRefused() {
        let big = String(repeating: "x", count: PluginStamp.maxEntryBytes + 1)
        let folder = folder("codex")
        XCTAssertThrowsError(
            try PluginStamp.write(
                MemoryBundle(["hooks/hooks.codex.json": "{}", "scripts/big.mjs": big]), agent: "codex", folder: folder,
                appPath: appPath, version: "1"))
        XCTAssertThrowsError(
            try PluginStamp.write(
                MemoryBundle(["hooks/hooks.claude.json": "{}"]), agent: "codex", folder: folder, appPath: appPath,
                version: "1"))
    }

    func testTheEscapeRoundTripsThroughJson() throws {
        for value in [appPath, "plain", "tab\there", "ünï/cødé"] {
            let json = "[\"" + PluginStamp.jsonEscaped(value) + "\"]"
            let parsed = try JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String]
            XCTAssertEqual(parsed, [value])
        }
    }
}

final class PluginInstallerTests: XCTestCase {
    private var dir = ""
    private var bin = ""
    private var settings: IntegrationSettings!
    private let runner = FakeRunner()

    override func setUpWithError() throws {
        dir = (NSTemporaryDirectory() as NSString).appendingPathComponent("edgepad-installer-\(UUID().uuidString)")
        bin = (dir as NSString).appendingPathComponent("bin")
        try FileManager.default.createDirectory(atPath: bin, withIntermediateDirectories: true)
        for tool in ["claude", "codex"] {
            let file = (bin as NSString).appendingPathComponent(tool)
            try "#!/bin/sh".write(toFile: file, atomically: true, encoding: .utf8)
        }
        settings = IntegrationSettings(path: (dir as NSString).appendingPathComponent(IntegrationSettings.fileName))
        runner.answer = { file, arguments in
            if arguments == ["--version"] {
                return ProcessResult(exitCode: 0, lastLine: "codex-cli 0.159.0")
            }
            return ProcessResult(exitCode: 0, lastLine: #"{"outcome":"ok"}"#)
        }
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: dir)
    }

    private func installer(root: String? = nil, path: String? = nil, version: String = "5.0.0") throws
        -> PluginInstaller
    {
        let plugins = root ?? (dir as NSString).appendingPathComponent("plugins")
        let pathValue = path ?? "relative/bin:" + bin
        return PluginInstaller(
            bundle: try RealBundle.source(), settings: settings, pluginsRoot: plugins, version: version,
            appPath: app, runner: runner, path: { pathValue })
    }

    private var app: String { (dir as NSString).appendingPathComponent("Edgepad.app/Contents/MacOS/Edgepad") }

    func testClaudeConnectRunsTheExactCommandsAndSavesTheSwitch() async throws {
        let installer = try installer()
        let folder = installer.folderOf("claude")

        let result = await installer.connect("claude")

        XCTAssertEqual(result, PluginResult(.connected))
        let claude = (bin as NSString).appendingPathComponent("claude")
        XCTAssertEqual(
            runner.runs.filter { $0.file == claude },
            [
                .init(file: claude, arguments: ["plugin", "marketplace", "add", folder, "--json"], cwd: folder),
                .init(
                    file: claude, arguments: ["plugin", "install", "edgepad@edgepad", "--scope", "user", "--json"],
                    cwd: folder),
            ])
        XCTAssertTrue(settings.flag("claude"))
        XCTAssertEqual(settings.text(IntegrationSettings.pluginVersionKey("claude")), "5.0.0")
        XCTAssertEqual(settings.text(IntegrationSettings.pluginAppKey("claude")), app)
        XCTAssertEqual(installer.status("claude"), .connected)
    }

    func testCodexConnectChecksTheVersionThenWaitsForTrust() async throws {
        let installer = try installer()
        let folder = installer.folderOf("codex")
        let codex = (bin as NSString).appendingPathComponent("codex")

        let result = await installer.connect("codex")

        XCTAssertEqual(result, PluginResult(.awaitingTrust))
        XCTAssertEqual(
            runner.runs.filter { $0.file == codex }.map(\.arguments),
            [
                ["--version"], ["plugin", "marketplace", "add", folder, "--json"],
                ["plugin", "add", "edgepad@edgepad", "--json"],
            ])
        XCTAssertEqual(installer.status("codex"), .awaitingTrust)
        settings.setFlag(IntegrationSettings.codexTrusted, true)
        XCTAssertEqual(installer.status("codex"), .connected)
    }

    func testAnOldOrUnreadableCodexAsksForAnUpdate() async throws {
        for answer in ["codex-cli 0.158.9", "garbage", ""] {
            runner.answer = { file, arguments in
                if arguments == ["--version"] {
                    return ProcessResult(exitCode: 0, lastLine: answer)
                }
                return ProcessResult(exitCode: 0, lastLine: #"{"outcome":"ok"}"#)
            }
            let result = try await installer().connect("codex")
            XCTAssertEqual(result, PluginResult(.updateCodex), answer)
            XCTAssertFalse(settings.flag("codex"))
        }
    }

    func testAFailedOutcomeWithExitZeroIsManualSteps() async throws {
        runner.answer = { _, arguments in
            return ProcessResult(exitCode: 0, lastLine: #"{"outcome":"failed","failureCode":"x"}"#)
        }
        let installer = try installer()

        let result = await installer.connect("claude")

        XCTAssertEqual(result.state, .manualSteps)
        XCTAssertEqual(result.commands, installer.commands("claude"))
        XCTAssertFalse(settings.flag("claude"))
    }

    func testAnUnsafeFolderRunsNothing() async throws {
        let unsafe = (dir as NSString).appendingPathComponent("plug\"ins")
        let result = try await installer(root: unsafe).connect("claude")
        XCTAssertEqual(result.state, .manualSteps)
        XCTAssertTrue(runner.runs.isEmpty)
    }

    func testNoNodeIsEverLookedFor() async throws {
        _ = try await installer().connect("claude")
        XCTAssertFalse(runner.runs.contains { $0.file.hasSuffix("node") || $0.arguments.contains("node") })
        let hooks = try String(
            contentsOfFile: (dir as NSString).appendingPathComponent("plugins/claude/edgepad/hooks/hooks.json"),
            encoding: .utf8)
        XCTAssertTrue(hooks.contains(PluginStamp.jsonEscaped(app)))
    }

    func testAnUnsafeAppPathRunsNothing() async throws {
        let odd = PluginInstaller(
            bundle: try RealBundle.source(), settings: settings, pluginsRoot: dir, version: "1",
            appPath: "/a$b/Edgepad", runner: runner, path: { self.bin })
        let result = await odd.connect("claude")
        XCTAssertEqual(result.state, .manualSteps)
        XCTAssertTrue(runner.runs.isEmpty)
    }

    func testRelativePathFoldersNeverCount() {
        XCTAssertNil(PluginInstaller.findCli("claude", path: "bin:./bin:relative"))
        let claude = (bin as NSString).appendingPathComponent("claude")
        XCTAssertEqual(PluginInstaller.findCli("claude", path: "bin:" + bin), claude)
        XCTAssertNil(PluginInstaller.findCli("claude", path: nil))
    }

    func testTheCommandsForTheCopyBoxQuoteTheFolder() throws {
        let installer = try installer(root: (dir as NSString).appendingPathComponent("my plugins"))
        let folder = installer.folderOf("claude")
        XCTAssertEqual(
            installer.commands("claude"),
            [
                "claude plugin marketplace add \"\(folder)\" --json",
                "claude plugin install edgepad@edgepad --scope user --json",
            ])
        XCTAssertEqual(
            installer.commands("codex", connect: false),
            ["codex plugin remove edgepad@edgepad --json", "codex plugin marketplace remove edgepad --json"])
    }

    func testDisconnectUninstallsDeletesTheCopyAndClearsTheSwitch() async throws {
        let installer = try installer()
        _ = await installer.connect("claude")
        let folder = installer.folderOf("claude")
        XCTAssertTrue(FileManager.default.fileExists(atPath: folder))

        let result = await installer.disconnect("claude")

        XCTAssertEqual(result, PluginResult(.notConnected))
        XCTAssertFalse(FileManager.default.fileExists(atPath: folder))
        XCTAssertFalse(settings.flag("claude"))
        XCTAssertEqual(settings.text(IntegrationSettings.pluginVersionKey("claude")), "")
        XCTAssertEqual(
            Array(runner.runs.suffix(2).map(\.arguments)),
            [
                ["plugin", "uninstall", "edgepad@edgepad", "--scope", "user", "--json"],
                ["plugin", "marketplace", "remove", "edgepad", "--json"],
            ])
    }

    func testEnsureCurrentRestampsOnAVersionChangeOrAMovedAppAndOtherwiseDoesNothing() async throws {
        _ = try await installer(version: "5.0.0").connect("claude")
        let before = runner.runs.count

        let same = try await installer(version: "5.0.0").ensureCurrent()
        XCTAssertTrue(same.isEmpty)
        XCTAssertEqual(runner.runs.count, before)

        let updated = try await installer(version: "5.0.1").ensureCurrent()
        XCTAssertEqual(updated.map(\.state), [.restartSessions])
        XCTAssertEqual(settings.text(IntegrationSettings.pluginVersionKey("claude")), "5.0.1")
        XCTAssertFalse(runner.runs.dropFirst(before).contains { $0.file.hasSuffix("claude") })

        settings.set(IntegrationSettings.pluginAppKey("claude"), "/old/place/Edgepad")
        let moved = try await installer(version: "5.0.1").ensureCurrent()
        XCTAssertEqual(moved.map(\.state), [.restartSessions])
        XCTAssertEqual(settings.text(IntegrationSettings.pluginAppKey("claude")), app)
    }

    func testEnsureCurrentAsksCodexToTrustAgainOnlyWhenItsHooksChanged() async throws {
        _ = try await installer(version: "5.0.0").connect("codex")
        settings.setFlag(IntegrationSettings.codexTrusted, true)

        // An update that stamps the same hooks file keeps the trust.
        let same = try await installer(version: "5.0.1").ensureCurrent()
        XCTAssertEqual(same.map(\.state), [.restartSessions])
        XCTAssertTrue(settings.flag(IntegrationSettings.codexTrusted))

        // A hooks file that comes out different is skipped by Codex until trusted again.
        let installer = try installer(version: "5.0.2")
        let hooks = installer.folderOf("codex") + "/edgepad/hooks/hooks.json"
        try Data("{}".utf8).write(to: URL(fileURLWithPath: hooks))
        let changed = await installer.ensureCurrent()
        XCTAssertEqual(changed.map(\.state), [.awaitingTrust])
        XCTAssertFalse(settings.flag(IntegrationSettings.codexTrusted))
        XCTAssertEqual(installer.status("codex"), .awaitingTrust)
    }

    func testVersionsParseStrictly() {
        XCTAssertEqual(PluginInstaller.codexVersion("codex-cli 0.159.0"), ToolVersion(0, 159, 0))
        XCTAssertEqual(PluginInstaller.codexVersion("codex-cli 1.0.0"), ToolVersion(1, 0, 0))
        XCTAssertEqual(PluginInstaller.codexVersion("codex-cli 0.160.0-alpha.1"), ToolVersion(0, 160, 0))
        XCTAssertNil(PluginInstaller.codexVersion("codex-cli 0.159"))
        XCTAssertNil(PluginInstaller.codexVersion("garbage"))
        XCTAssertLessThan(ToolVersion(0, 158, 9), PluginInstaller.minCodex)
        XCTAssertGreaterThanOrEqual(ToolVersion(0, 159, 0), PluginInstaller.minCodex)
    }

    func testOnlyQuietCharactersMakeASafePath() {
        XCTAssertTrue(PluginInstaller.safePath("/Users/me/Library/Application Support/Edgepad/plugins/claude"))
        XCTAssertTrue(PluginInstaller.safePath("/Users/zoë/plug-ins_1.2"))
        XCTAssertTrue(PluginInstaller.safePath("/Users/RUNNER~1/plugins"))
        for bad in ["", "/a\"b", "/a%b", "/a^b", "/a&b", "/a!b", "/a(b", "/a)b", "/a;b", "/a$b", "/a`b"] {
            XCTAssertFalse(PluginInstaller.safePath(bad), bad)
        }
    }

    func testSuccessNeedsExitZeroAndAnOkOutcome() {
        let ok = #"{"outcome":"ok"}"#
        XCTAssertTrue(PluginInstaller.succeeded(ProcessResult(exitCode: 0, lastLine: ok), strict: true))
        XCTAssertFalse(PluginInstaller.succeeded(ProcessResult(exitCode: 1, lastLine: ok), strict: true))
        XCTAssertFalse(PluginInstaller.succeeded(ProcessResult(exitCode: 0, lastLine: "{}"), strict: true))
        XCTAssertTrue(PluginInstaller.succeeded(ProcessResult(exitCode: 0, lastLine: "{}"), strict: false))
        XCTAssertFalse(PluginInstaller.succeeded(ProcessResult(exitCode: 0, lastLine: "done"), strict: false))
        let late = ProcessResult(exitCode: 0, lastLine: #"{"outcome":"ok"}"#, timedOut: true)
        XCTAssertFalse(PluginInstaller.succeeded(late, strict: false))
    }

    func testTheRealRunnerReadsTheLastLineAndStopsAtTheTimeout() async {
        let runner = SystemProcessRunner()
        let echo = await runner.run("/bin/echo", ["first", "last"], cwd: "/", timeout: 10)
        XCTAssertEqual(echo, ProcessResult(exitCode: 0, lastLine: "first last"))

        let slow = await runner.run("/bin/sleep", ["30"], cwd: "/", timeout: 0.5)
        XCTAssertTrue(slow.timedOut)

        let missing = await runner.run("/nonexistent/tool", [], cwd: "/", timeout: 1)
        XCTAssertEqual(missing.exitCode, -1)
    }
}
