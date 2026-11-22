import Foundation

/// Connect and Disconnect for Gemini CLI and OpenCode, the two tools with no plugin CLI: Edgepad writes its own entry
/// into the tool's config and takes only that entry out again. Gemini CLI: five hook entries named `edgepad` merged
/// into `~/.gemini/settings.json`; every other key, hook and group is kept. OpenCode: the file `edgepad.ts` in
/// `~/.config/opencode/plugins/`, which a file without our first-line marker is never overwritten or deleted. A config
/// that is not strict JSON (comments, a trailing comma, a root that is not an object, a `hooks` of the wrong shape) is
/// refused and reported, never rewritten. The old file is copied to `settings.json.edgepad-backup` before every write,
/// and the new one replaces it whole, keeping the mode of the file it replaces. The twin of the core's
/// HookConfigInstaller. Unlike the core's, a rewritten Gemini file has its keys sorted and numbers in Foundation's
/// spelling (2.50 becomes 2.5), and a duplicate key is not refused: JSONSerialization keeps the last one.
public final class HookConfigInstaller {
    public static let geminiTemplate = "hooks/hooks.gemini.json"
    public static let openCodeFolder = "opencode/"
    public static let openCodeTemplate = "opencode/edgepad.ts"
    public static let hookName = "edgepad"
    public static let openCodeMarker = "Edgepad OpenCode plugin"
    public static let backupSuffix = ".edgepad-backup"

    private static let maxBytes = 1024 * 1024
    private static let geminiCommand = "--hook gemini session"
    private static let agents = [AgentToken.gemini.rawValue, AgentToken.opencode.rawValue]

    private struct Refused: Error {
        let reason: String
    }

    private let bundle: PluginSource
    private let settings: IntegrationSettings
    private let home: String
    private let version: String
    private let appPath: String

    public init(bundle: PluginSource, settings: IntegrationSettings, home: String, version: String, appPath: String) {
        self.bundle = bundle
        self.settings = settings
        self.home = home
        self.version = version
        self.appPath = appPath
    }

    /// The Gemini CLI user settings file.
    public var geminiFile: String {
        (home as NSString).appendingPathComponent(".gemini/settings.json")
    }

    /// The OpenCode plugin file Edgepad writes.
    public var openCodeFile: String {
        (home as NSString).appendingPathComponent(".config/opencode/plugins/edgepad.ts")
    }

    /// Connected or not connected, from the saved switch.
    public func status(_ agent: String) -> PluginState {
        settings.flag(agent) ? .connected : .notConnected
    }

    /// Writes our entry: `.connected`, or `.failed` (logged) when the config is refused or the disk said no.
    public func connect(_ agent: String) -> PluginResult {
        guard PluginInstaller.safePath(appPath), Self.agents.contains(agent) else { return PluginResult(.failed) }
        do {
            if agent == AgentToken.gemini.rawValue {
                try connectGemini()
            } else {
                try connectOpenCode()
            }
        } catch {
            Log.write("The \(agent) integration could not be written: \(error)")
            return PluginResult(.failed)
        }
        settings.set(IntegrationSettings.pluginVersionKey(agent), version)
        settings.set(IntegrationSettings.pluginAppKey(agent), appPath)
        settings.setFlag(agent, true)
        return PluginResult(.connected)
    }

    /// Turns the switch off at once (the hook server then ignores the tool), then removes only our entry. `.failed`
    /// when the file could not be read or changed.
    public func disconnect(_ agent: String) -> PluginResult {
        settings.setFlag(agent, false)
        do {
            if agent == AgentToken.gemini.rawValue {
                try disconnectGemini()
            } else if agent == AgentToken.opencode.rawValue {
                try disconnectOpenCode()
            } else {
                return PluginResult(.failed)
            }
        } catch {
            Log.write("The \(agent) integration could not be removed: \(error)")
            return PluginResult(.failed)
        }
        settings.set(IntegrationSettings.pluginVersionKey(agent), "")
        settings.set(IntegrationSettings.pluginAppKey(agent), "")
        return PluginResult(.notConnected)
    }

    /// A connected tool whose entry was written by another app version, or for an app at another path, is written
    /// again and reported `.restartSessions`, or `.failed`.
    public func ensureCurrent() -> [(agent: String, state: PluginState)] {
        var notices: [(agent: String, state: PluginState)] = []
        for agent in Self.agents {
            guard settings.flag(agent),
                settings.text(IntegrationSettings.pluginVersionKey(agent)) != version
                    || settings.text(IntegrationSettings.pluginAppKey(agent)) != appPath
            else { continue }
            notices.append((agent, connect(agent).state == .connected ? .restartSessions : .failed))
        }
        return notices
    }

    // MARK: Gemini CLI

    private func connectGemini() throws {
        let template = try JSONSerialization.jsonObject(with: try templateBytes(Self.geminiTemplate))
        guard let ours = (template as? [String: Any])?["hooks"] as? [String: Any] else {
            throw Refused(reason: "The Gemini template has no hooks")
        }
        let (loaded, existed) = try loadGemini()
        var root = loaded
        let before = try Self.render(root)
        Self.removeOurs(&root)
        if root["hooks"] == nil {
            root["hooks"] = [String: Any]()
        }
        guard var hooks = root["hooks"] as? [String: Any] else { throw Refused(reason: "hooks is not an object") }
        for (name, groups) in ours {
            if hooks[name] == nil {
                hooks[name] = [Any]()
            }
            guard var list = hooks[name] as? [Any], let add = groups as? [Any] else {
                throw Refused(reason: "A hooks entry is not an array")
            }
            list.append(contentsOf: add)
            hooks[name] = list
        }
        root["hooks"] = hooks
        try save(geminiFile, root, existed: existed, before: before)
    }

    private func disconnectGemini() throws {
        guard FileManager.default.fileExists(atPath: geminiFile) else { return }
        var (root, _) = try loadGemini()
        let before = try Self.render(root)
        Self.removeOurs(&root)
        try save(geminiFile, root, existed: true, before: before)
    }

    private func loadGemini() throws -> (root: [String: Any], existed: Bool) {
        guard FileManager.default.fileExists(atPath: geminiFile) else { return ([:], false) }
        let data = try Data(contentsOf: URL(fileURLWithPath: geminiFile))
        guard data.count <= Self.maxBytes else { throw Refused(reason: "The Gemini settings file is too large") }
        guard let root = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw Refused(reason: "The Gemini settings are not an object")
        }
        return (root, true)
    }

    // Takes out every hook entry named edgepad that runs our command, then the groups and event lists that this
    // emptied; nothing else.
    private static func removeOurs(_ root: inout [String: Any]) {
        guard var hooks = root["hooks"] as? [String: Any] else { return }
        for (name, node) in hooks {
            guard var groups = node as? [Any] else { continue }
            var touched = false
            for index in stride(from: groups.count - 1, through: 0, by: -1) {
                guard var group = groups[index] as? [String: Any], var list = group["hooks"] as? [Any] else { continue }
                let count = list.count
                list.removeAll { isOurs($0) }
                guard list.count < count else { continue }
                touched = true
                if list.isEmpty {
                    groups.remove(at: index)
                } else {
                    group["hooks"] = list
                    groups[index] = group
                }
            }
            if touched {
                hooks[name] = groups.isEmpty ? nil : groups
            }
        }
        root["hooks"] = hooks.isEmpty ? nil : hooks
    }

    private static func isOurs(_ hook: Any) -> Bool {
        guard let h = hook as? [String: Any], h["name"] as? String == hookName,
            let command = h["command"] as? String
        else { return false }
        return command.contains(geminiCommand)
    }

    private static func render(_ root: [String: Any]) throws -> String {
        let data = try JSONSerialization.data(
            withJSONObject: root, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])
        return String(decoding: data, as: UTF8.self)
    }

    // Writes only a changed file, after copying the old one aside.
    private func save(_ file: String, _ root: [String: Any], existed: Bool, before: String) throws {
        let after = try Self.render(root)
        guard after != before else { return }
        let manager = FileManager.default
        var mode: NSNumber?
        if existed {
            mode = (try manager.attributesOfItem(atPath: file))[.posixPermissions] as? NSNumber
            let backup = file + Self.backupSuffix
            try? manager.removeItem(atPath: backup)
            try manager.copyItem(atPath: file, toPath: backup)
        }
        if let why = writePrivately(after + "\n", to: file) { throw Refused(reason: why) }
        if let mode {
            try manager.setAttributes([.posixPermissions: mode], ofItemAtPath: file)
        }
    }

    // MARK: OpenCode

    private func connectOpenCode() throws {
        let text = String(decoding: try templateBytes(Self.openCodeTemplate), as: UTF8.self)
        let file = openCodeFile
        if FileManager.default.fileExists(atPath: file) {
            guard try isOurPlugin(file) else { throw Refused(reason: "An OpenCode plugin of that name is not ours") }
            if try String(contentsOfFile: file, encoding: .utf8) == text { return }
        }
        if let why = writePrivately(text, to: file) { throw Refused(reason: why) }
    }

    private func disconnectOpenCode() throws {
        if FileManager.default.fileExists(atPath: openCodeFile), try isOurPlugin(openCodeFile) {
            try FileManager.default.removeItem(atPath: openCodeFile)
        }
    }

    private func isOurPlugin(_ file: String) throws -> Bool {
        let text = try String(contentsOfFile: file, encoding: .utf8)
        let first = text.split(separator: "\n", maxSplits: 1, omittingEmptySubsequences: false).first ?? ""
        return first.contains(Self.openCodeMarker)
    }

    // The template with __APP__ and __VERSION__ filled by the JSON encoder's escaping; the bytes as UTF-8.
    private func templateBytes(_ entry: String) throws -> Data {
        let found = try bundle.entries().first { $0.replacingOccurrences(of: "\\", with: "/") == entry }
        guard let found else { throw Refused(reason: "The plugin bundle has no " + entry) }
        var text = String(decoding: try bundle.read(found, limit: Self.maxBytes), as: UTF8.self)
        text = text.replacingOccurrences(of: PluginStamp.app, with: PluginStamp.jsonEscaped(appPath))
        text = text.replacingOccurrences(of: PluginStamp.version, with: PluginStamp.jsonEscaped(version))
        return Data(text.utf8)
    }
}
