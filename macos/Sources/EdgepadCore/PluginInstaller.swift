import Foundation

/// How a vendor CLI run ended. `lastLine` is the last non-empty line of the first 64 KiB of stdout (stderr is
/// discarded). A run that could not start has exit code -1; one that ran past its time was killed and is `timedOut`.
public struct ProcessResult: Equatable {
    public let exitCode: Int32
    public let lastLine: String
    public let timedOut: Bool

    public init(exitCode: Int32, lastLine: String, timedOut: Bool = false) {
        self.exitCode = exitCode
        self.lastLine = lastLine
        self.timedOut = timedOut
    }
}

/// Runs one program with fixed arguments and waits for it. The plugin installer's only way to start a process.
public protocol ProcessRunner: AnyObject {
    /// Runs `file` (an absolute path) with `arguments` as separate arguments, never through a shell.
    func run(_ file: String, _ arguments: [String], cwd: String, timeout: TimeInterval) async -> ProcessResult
}

/// The real runner: `Process.arguments` (no quoting by hand, no shell), stdin closed so nothing can wait for an answer,
/// `NO_COLOR=1`, stdout read up to 64 KiB and the rest read away, stderr dropped, and the process terminated at the
/// timeout. Never throws for a program that is missing or will not start: that is exit code -1.
public final class SystemProcessRunner: ProcessRunner {
    public static let maxOutputBytes = 64 * 1024

    public init() {}

    public func run(_ file: String, _ arguments: [String], cwd: String, timeout: TimeInterval) async -> ProcessResult {
        await withCheckedContinuation { continuation in
            Self.start(file, arguments, cwd, timeout) { continuation.resume(returning: $0) }
        }
    }

    private static func start(
        _ file: String, _ arguments: [String], _ cwd: String, _ timeout: TimeInterval,
        _ done: @escaping (ProcessResult) -> Void
    ) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: file)
        process.arguments = arguments
        process.currentDirectoryURL = URL(fileURLWithPath: cwd)
        var environment = ProcessInfo.processInfo.environment
        environment["NO_COLOR"] = "1"
        process.environment = environment
        let output = Pipe()
        process.standardInput = FileHandle.nullDevice
        process.standardOutput = output
        process.standardError = FileHandle.nullDevice
        let killed = Once()
        let finished = DispatchGroup()
        finished.enter()
        process.terminationHandler = { _ in finished.leave() }
        do {
            try process.run()
        } catch {
            Log.write("\((file as NSString).lastPathComponent) could not be started")
            done(ProcessResult(exitCode: -1, lastLine: ""))
            return
        }
        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + timeout) {
            guard process.isRunning else { return }
            killed.set()
            process.terminate()
        }
        DispatchQueue.global(qos: .utility).async {
            var kept = Data()
            let reader = output.fileHandleForReading
            while true {
                let chunk = reader.availableData
                if chunk.isEmpty { break }
                if kept.count < maxOutputBytes {
                    kept.append(chunk.prefix(maxOutputBytes - kept.count))
                }
            }
            finished.wait()
            if killed.isSet {
                Log.write("\((file as NSString).lastPathComponent) took longer than \(Int(timeout)) s and was stopped")
                done(ProcessResult(exitCode: -1, lastLine: "", timedOut: true))
                return
            }
            let text = String(decoding: kept, as: UTF8.self)
            done(ProcessResult(exitCode: process.terminationStatus, lastLine: lastLine(text)))
        }
    }

    /// The last line with something on it, trimmed; empty when there is none.
    public static func lastLine(_ text: String) -> String {
        for line in text.unicodeScalars.split(separator: "\n").reversed() {
            let trimmed = String(String.UnicodeScalarView(line)).trimmingCharacters(in: .whitespacesAndNewlines)
            if !trimmed.isEmpty { return trimmed }
        }
        return ""
    }
}

/// Where an agent's integration stands, for the Integrations window.
public enum PluginState: Equatable {
    /// Not connected.
    case notConnected
    /// Installed and (Codex) trusted.
    case connected
    /// Codex installed the plugin; the person still has to trust its hooks in /hooks or Settings > Hooks.
    case awaitingTrust
    /// The copy was re-stamped (app updated, Node moved): open sessions need a restart or /reload-plugins.
    case restartSessions
    /// The CLI could not be run, or did not report success: `PluginResult.commands` holds what to run by hand.
    case manualSteps
    /// No Node.js 20.1 or newer on PATH.
    case nodeMissing
    /// The agent's CLI is not on PATH. `PluginResult.commands` still names the folder, for Codex Desktop.
    case cliNotFound
    /// Codex is older than 0.159.
    case updateCodex
    /// The plugin copy could not be written (disk refused, damaged bundle). Logged.
    case failed
}

/// An outcome and, for `.manualSteps` and `.cliNotFound`, the exact lines for the Copy box.
public struct PluginResult: Equatable {
    public let state: PluginState
    public let commands: [String]

    public init(_ state: PluginState, _ commands: [String] = []) {
        self.state = state
        self.commands = commands
    }
}

/// A tool's version, major.minor.patch.
public struct ToolVersion: Comparable {
    public let major: Int
    public let minor: Int
    public let patch: Int

    public init(_ major: Int, _ minor: Int, _ patch: Int = 0) {
        self.major = major
        self.minor = minor
        self.patch = patch
    }

    public static func < (lhs: ToolVersion, rhs: ToolVersion) -> Bool {
        (lhs.major, lhs.minor, lhs.patch) < (rhs.major, rhs.minor, rhs.patch)
    }
}

/// Connect and Disconnect for the Claude Code and Codex plugins. Connect stamps the plugin copy (`PluginStamp`) under
/// `<plugins>/<agent>` and then runs the vendor's own non-interactive CLI on it; it never edits a file under ~/.claude
/// or ~/.codex and never bypasses Codex's hook trust. Programs are found only in absolute PATH folders (never the
/// working directory), run by absolute path with fixed words and one variable argument, our own folder, which must be
/// letters, digits, space and `_ . : \ / -`; anything else, any failure and any output that does not say ok falls back
/// to `.manualSteps` with the commands to paste. The twin of the laptop core's PluginInstaller.
public final class PluginInstaller {
    public static let marketplaceName = "edgepad"
    public static let pluginId = "edgepad@edgepad"
    public static let timeout: TimeInterval = 60
    public static let minNode = ToolVersion(20, 1)
    public static let minCodex = ToolVersion(0, 159)

    private static let agents = [AgentToken.claude.rawValue, AgentToken.codex.rawValue]

    private let bundle: PluginSource
    private let settings: IntegrationSettings
    private let pluginsRoot: String
    private let version: String
    private let runner: ProcessRunner
    private let path: () -> String?

    /// - Parameters:
    ///   - pluginsRoot: The folder holding the per-agent copies; `defaultRoot()` in the app.
    ///   - version: This app's version, stamped as the plugin's and stored to notice an update.
    ///   - path: For tests: the PATH value. The process's PATH by default.
    public init(
        bundle: PluginSource, settings: IntegrationSettings, pluginsRoot: String, version: String,
        runner: ProcessRunner = SystemProcessRunner(),
        path: @escaping () -> String? = { ProcessInfo.processInfo.environment["PATH"] }
    ) {
        self.bundle = bundle
        self.settings = settings
        self.pluginsRoot = pluginsRoot
        self.version = version
        self.runner = runner
        self.path = path
    }

    /// `~/Library/Application Support/Edgepad/plugins`.
    public static func defaultRoot() -> String {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support"
        return support + "/Edgepad/plugins"
    }

    /// The marketplace folder of an agent's copy: what `plugin marketplace add` is pointed at.
    public func folderOf(_ agent: String) -> String {
        (pluginsRoot as NSString).appendingPathComponent(agent)
    }

    /// Connected, awaitingTrust (Codex not yet trusted) or notConnected, from the saved switches.
    public func status(_ agent: String) -> PluginState {
        if !settings.flag(agent) { return .notConnected }
        if agent == AgentToken.codex.rawValue && !settings.flag(IntegrationSettings.codexTrusted) {
            return .awaitingTrust
        }
        return .connected
    }

    /// The exact lines for the Copy box: the connect (or disconnect) commands with our folder quoted.
    public func commands(_ agent: String, connect: Bool = true) -> [String] {
        let cli = agent == AgentToken.claude.rawValue ? "claude" : "codex"
        return Self.arguments(agent, connect: connect, folder: folderOf(agent)).map { args in
            cli + " " + args.map { $0.contains(" ") ? "\"\($0)\"" : $0 }.joined(separator: " ")
        }
    }

    /// Stamps the copy and installs it with the vendor CLI. Saves the switch and what was stamped only on success.
    public func connect(_ agent: String) async -> PluginResult {
        let folder = folderOf(agent)
        guard Self.safePath(folder) else { return PluginResult(.manualSteps, commands(agent)) }
        guard let node = await findNode() else { return PluginResult(.nodeMissing) }
        guard Self.safePath(node) else { return PluginResult(.manualSteps, commands(agent)) }
        guard stamp(agent, node) else { return PluginResult(.failed) }
        guard let cli = Self.findCli(agent, path: path()) else { return PluginResult(.cliNotFound, commands(agent)) }
        let codex = agent == AgentToken.codex.rawValue
        if codex {
            let reply = await runner.run(cli, ["--version"], cwd: folder, timeout: Self.timeout)
            guard reply.exitCode == 0, let found = Self.codexVersion(reply.lastLine), found >= Self.minCodex else {
                return PluginResult(.updateCodex)
            }
        }
        for args in Self.arguments(agent, connect: true, folder: folder) {
            let reply = await runner.run(cli, args, cwd: folder, timeout: Self.timeout)
            guard Self.succeeded(reply, strict: !codex) else { return PluginResult(.manualSteps, commands(agent)) }
        }
        if codex {
            settings.setFlag(IntegrationSettings.codexTrusted, false)
        }
        settings.set(IntegrationSettings.pluginVersionKey(agent), version)
        settings.set(IntegrationSettings.pluginNodeKey(agent), node)
        settings.setFlag(agent, true)
        return PluginResult(codex ? .awaitingTrust : .connected)
    }

    /// Turns the switch off at once (the hook server then defers), uninstalls with the vendor CLI and deletes the copy.
    /// When the CLI cannot do it, the copy stays (its hooks only defer now) and the commands to run by hand come back.
    public func disconnect(_ agent: String) async -> PluginResult {
        settings.setFlag(agent, false)
        let folder = folderOf(agent)
        let manual = PluginResult(.manualSteps, commands(agent, connect: false))
        guard Self.safePath(folder), let cli = Self.findCli(agent, path: path()) else { return manual }
        var isFolder: ObjCBool = false
        let exists = FileManager.default.fileExists(atPath: folder, isDirectory: &isFolder) && isFolder.boolValue
        let cwd = exists ? folder : (cli as NSString).deletingLastPathComponent
        for args in Self.arguments(agent, connect: false, folder: folder) {
            let reply = await runner.run(cli, args, cwd: cwd, timeout: Self.timeout)
            guard Self.succeeded(reply, strict: agent == AgentToken.claude.rawValue) else { return manual }
        }
        do {
            try PluginStamp.remove(folder)
        } catch {
            Log.write("The \(agent) plugin folder could not be removed")
        }
        settings.set(IntegrationSettings.pluginVersionKey(agent), "")
        settings.set(IntegrationSettings.pluginNodeKey(agent), "")
        if agent == AgentToken.codex.rawValue {
            settings.setFlag(IntegrationSettings.codexTrusted, false)
        }
        return PluginResult(.notConnected)
    }

    /// At app start and when the window opens: a connected agent whose copy was stamped by another app version or for a
    /// Node that is no longer there is re-stamped in place, with no CLI call, and reported `.restartSessions`;
    /// `.nodeMissing` when no Node is left. Agents with nothing to do are not listed.
    public func ensureCurrent() async -> [(agent: String, state: PluginState)] {
        var notices: [(agent: String, state: PluginState)] = []
        for agent in Self.agents where settings.flag(agent) {
            let stampedNode = settings.text(IntegrationSettings.pluginNodeKey(agent))
            if settings.text(IntegrationSettings.pluginVersionKey(agent)) == version && !stampedNode.isEmpty
                && FileManager.default.fileExists(atPath: stampedNode)
            {
                continue
            }
            guard let node = await findNode(), Self.safePath(node) else {
                notices.append((agent, .nodeMissing))
                continue
            }
            guard Self.safePath(folderOf(agent)), stamp(agent, node) else {
                notices.append((agent, .failed))
                continue
            }
            settings.set(IntegrationSettings.pluginVersionKey(agent), version)
            settings.set(IntegrationSettings.pluginNodeKey(agent), node)
            notices.append((agent, .restartSessions))
        }
        return notices
    }

    /// The first absolute PATH folder holding `node` whose `--version` is 20.1 or newer.
    public func findNode() async -> String? {
        for candidate in Self.candidates(path(), ["node"]) {
            let cwd = (candidate as NSString).deletingLastPathComponent
            let reply = await runner.run(candidate, ["--version"], cwd: cwd, timeout: Self.timeout)
            if reply.exitCode == 0, let found = Self.nodeVersion(reply.lastLine), found >= Self.minNode {
                return candidate
            }
        }
        return nil
    }

    /// The agent's CLI, `claude` or `codex`, in the first absolute PATH folder that has it.
    public static func findCli(_ agent: String, path: String?) -> String? {
        candidates(path, [agent == AgentToken.claude.rawValue ? "claude" : "codex"]).first
    }

    /// `v20.1.0` to 20.1.0; nil for anything else.
    public static func nodeVersion(_ text: String) -> ToolVersion? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.hasPrefix("v") else { return nil }
        return parse(String(trimmed.dropFirst()))
    }

    /// `codex-cli 0.159.0` (a `-` or `+` suffix of letters, digits, dots and dashes allowed) to 0.159.0; nil for
    /// anything else.
    public static func codexVersion(_ text: String) -> ToolVersion? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let prefix = "codex-cli "
        guard trimmed.hasPrefix(prefix) else { return nil }
        var rest = String(trimmed.dropFirst(prefix.count))
        if let cut = rest.firstIndex(where: { $0 == "-" || $0 == "+" }) {
            let suffix = rest[rest.index(after: cut)...]
            let allowed = suffix.unicodeScalars.allSatisfy {
                GitHubApp.isAsciiLetterOrDigit($0) || $0 == "." || $0 == "-"
            }
            guard allowed else { return nil }
            rest = String(rest[..<cut])
        }
        return parse(rest)
    }

    /// Letters, digits, space and `_ . : \ / -`, plus `~` as the core allows it (for Windows short names): nothing a
    /// shell reads specially in an argument that is always an absolute path.
    public static func safePath(_ text: String) -> Bool {
        !text.isEmpty
            && text.unicodeScalars.allSatisfy { scalar in
                switch scalar.properties.generalCategory {
                case .uppercaseLetter, .lowercaseLetter, .titlecaseLetter, .modifierLetter, .otherLetter,
                    .decimalNumber:
                    return true
                default:
                    return " _.:\\/-~".unicodeScalars.contains(scalar)
                }
            }
    }

    /// Exit 0 and, for Claude (strict), a last line that is JSON with `outcome` "ok"; for Codex a last line that is
    /// JSON whose `outcome`, if any, is "ok".
    public static func succeeded(_ result: ProcessResult, strict: Bool) -> Bool {
        guard result.exitCode == 0, !result.timedOut,
            let root = JsonRead.object(Data(result.lastLine.utf8), maxDepth: 64)
        else { return false }
        guard let outcome = root["outcome"] else { return !strict }
        return outcome as? String == "ok"
    }

    /// The argument vectors Connect or Disconnect runs, in order.
    public static func arguments(_ agent: String, connect: Bool, folder: String) -> [[String]] {
        switch (agent == AgentToken.claude.rawValue, connect) {
        case (true, true):
            return [
                ["plugin", "marketplace", "add", folder, "--json"],
                ["plugin", "install", pluginId, "--scope", "user", "--json"],
            ]
        case (true, false):
            return [
                ["plugin", "uninstall", pluginId, "--scope", "user", "--json"],
                ["plugin", "marketplace", "remove", marketplaceName, "--json"],
            ]
        case (false, true):
            return [["plugin", "marketplace", "add", folder, "--json"], ["plugin", "add", pluginId, "--json"]]
        case (false, false):
            return [
                ["plugin", "remove", pluginId, "--json"],
                ["plugin", "marketplace", "remove", marketplaceName, "--json"],
            ]
        }
    }

    // Every absolute PATH folder, in order, that holds one of the names as a file; relative entries (and so the working
    // directory) never count.
    private static func candidates(_ pathVariable: String?, _ names: [String]) -> [String] {
        var found: [String] = []
        for raw in (pathVariable ?? "").split(separator: ":") {
            let quote = CharacterSet(charactersIn: "\"")
            let folder = raw.trimmingCharacters(in: .whitespaces).trimmingCharacters(in: quote)
            guard folder.hasPrefix("/") else { continue }
            for name in names {
                let file = (folder as NSString).appendingPathComponent(name)
                var isFolder: ObjCBool = false
                if FileManager.default.fileExists(atPath: file, isDirectory: &isFolder), !isFolder.boolValue {
                    found.append(file)
                }
            }
        }
        return found
    }

    /// Exactly three dot-separated parts of 1..4, 1..4 and 1..6 ASCII digits.
    private static func parse(_ text: String) -> ToolVersion? {
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        let widths = [4, 4, 6]
        guard parts.count == 3 else { return nil }
        var numbers: [Int] = []
        for (part, width) in zip(parts, widths) {
            guard (1...width).contains(part.utf8.count), part.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }),
                let number = Int(part)
            else { return nil }
            numbers.append(number)
        }
        return ToolVersion(numbers[0], numbers[1], numbers[2])
    }

    private func stamp(_ agent: String, _ node: String) -> Bool {
        do {
            try PluginStamp.write(bundle, agent: agent, folder: folderOf(agent), nodePath: node, version: version)
            return true
        } catch {
            Log.write("The \(agent) plugin could not be written")
            return false
        }
    }
}

/// A flag set once from one queue and read from another.
private final class Once {
    private let gate = NSLock()
    private var value = false

    var isSet: Bool {
        gate.lock()
        defer { gate.unlock() }
        return value
    }

    func set() {
        gate.lock()
        defer { gate.unlock() }
        value = true
    }
}
