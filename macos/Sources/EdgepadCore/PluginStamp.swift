import Foundation

/// A plugin bundle that cannot be stamped: a file that would leave its folder, one over 1 MiB, or no hooks template.
public struct PluginBundleError: Error, Equatable {
    public let reason: String
}

/// The plugin source folder (plugins/edgepad) as the app carries it: relative paths with "/", and their bytes.
public protocol PluginSource: AnyObject {
    /// Every file, as a path relative to the plugin folder.
    func entries() throws -> [String]
    /// The bytes of one entry from `entries`, at most `limit`; more throws `PluginBundleError`.
    func read(_ entry: String, limit: Int) throws -> Data
}

/// The plugin folder on disk: the app bundle's Resources/plugins/edgepad, or a test's copy. Regular files only; a link
/// inside the folder is never followed.
public final class DirectoryPluginSource: PluginSource {
    private let root: String

    public init(root: String) {
        self.root = root
    }

    public func entries() throws -> [String] {
        let manager = FileManager.default
        guard let walker = manager.enumerator(atPath: root) else {
            throw PluginBundleError(reason: "The plugin folder is missing")
        }
        var found: [String] = []
        while let relative = walker.nextObject() as? String {
            let path = (root as NSString).appendingPathComponent(relative)
            let type = (try? manager.attributesOfItem(atPath: path))?[.type] as? FileAttributeType
            if type == .typeRegular {
                found.append(relative)
            } else if type == .typeSymbolicLink {
                walker.skipDescendants()
            }
        }
        return found.sorted()
    }

    public func read(_ entry: String, limit: Int) throws -> Data {
        let path = (root as NSString).appendingPathComponent(entry)
        let handle = try FileHandle(forReadingFrom: URL(fileURLWithPath: path))
        defer { try? handle.close() }
        let data = try handle.read(upToCount: limit + 1) ?? Data()
        guard data.count <= limit else { throw PluginBundleError(reason: "A plugin file is over 1 MiB") }
        return data
    }
}

/// Writes the per-agent copy of the plugin that Connect points the vendor CLI at, `<plugins>/<agent>/`: `edgepad/` with
/// the agent's hooks template as `hooks/hooks.json` and only the files that agent uses, plus
/// `.claude-plugin/marketplace.json` beside it. Always deleted and written fresh, so a tampered copy reverts. The
/// placeholders `__APP__` (this app executable), `__ROOT__` (the copy's own
/// folder) and `__VERSION__` are replaced by values escaped by the JSON encoder, never pasted in raw, which is also a
/// valid escape inside relay.ts's double-quoted string. An entry whose path would leave the folder, or one over 1 MiB,
/// stops the stamp with `PluginBundleError`. The twin of the core's PluginStamp.
public enum PluginStamp {
    public static let maxEntryBytes = 1024 * 1024
    public static let pluginFolder = "edgepad"
    public static let marketplace = ".claude-plugin/marketplace.json"
    /// The app executable the hooks run (`"__APP__" --hook <agent> <kind>`).
    public static let app = "__APP__"
    public static let root = "__ROOT__"
    public static let version = "__VERSION__"

    private static let marketplaceTemplate = "marketplace.template.json"
    private static let claudeHooks = "hooks/hooks.claude.json"
    private static let codexHooks = "hooks/hooks.codex.json"
    private static let hooks = "hooks/hooks.json"

    // Files only Claude Code loads: its manifest and the usage mod. Codex has its own manifest folder.
    private static let claudeOnly = [".claude-plugin/", "hooks/relay.ts", "types/"]
    private static let codexOnly = [".codex-plugin/"]

    /// The `__ROOT__` value for a stamp in `folder`: its `edgepad` folder, absolute.
    public static func rootOf(_ folder: String) -> String {
        ((folder as NSString).appendingPathComponent(pluginFolder) as NSString).standardizingPath
    }

    /// `value` as it reads between the quotes of a JSON string, escaped by JSONSerialization.
    public static func jsonEscaped(_ value: String) -> String {
        guard let data = try? JSONSerialization.data(withJSONObject: [value], options: [.withoutEscapingSlashes]),
            let text = String(data: data, encoding: .utf8), text.hasPrefix("[\""), text.hasSuffix("\"]")
        else { return "" }
        return String(text.dropFirst(2).dropLast(2))
    }

    /// Deletes `folder` and writes the agent's copy there. Throws what the disk threw, or `PluginBundleError` for a bad
    /// bundle; a bad bundle leaves the old copy in place.
    public static func write(_ bundle: PluginSource, agent: String, folder: String, appPath: String, version: String)
        throws
    {
        guard DashWire.isAgent(agent) else { throw PluginBundleError(reason: "Not an agent") }
        let full = (folder as NSString).standardizingPath
        let edgepad = (full as NSString).appendingPathComponent(pluginFolder)
        let values = [
            (app, jsonEscaped(appPath)), (root, jsonEscaped(rootOf(full))), (Self.version, jsonEscaped(version)),
        ]

        // Read and check everything before the old copy goes.
        var files: [(target: String, bytes: Data)] = []
        for raw in try bundle.entries() {
            let entry = raw.replacingOccurrences(of: "\\", with: "/")
            guard let relative = target(agent, entry) else { continue }
            let base = relative == marketplace ? full : edgepad
            let destination = ((base as NSString).appendingPathComponent(relative) as NSString).standardizingPath
            let parts = entry.split(separator: "/", omittingEmptySubsequences: false)
            guard !entry.hasPrefix("/"), !parts.contains(".."), destination.hasPrefix(full + "/") else {
                throw PluginBundleError(reason: "A plugin file would land outside its folder")
            }
            var bytes = try bundle.read(raw, limit: maxEntryBytes)
            if relative.hasSuffix(".json") || relative.hasSuffix(".ts") {
                var text = String(decoding: bytes, as: UTF8.self)
                for (placeholder, value) in values {
                    text = text.replacingOccurrences(of: placeholder, with: value)
                }
                bytes = Data(text.utf8)
            }
            files.append((destination, bytes))
        }
        guard files.contains(where: { $0.target.hasSuffix("/" + hooks) }) else {
            throw PluginBundleError(reason: "The plugin bundle has no hooks template for this agent")
        }

        try remove(full)
        let manager = FileManager.default
        let privateFolder: [FileAttributeKey: Any] = [.posixPermissions: 0o700]
        try manager.createDirectory(atPath: full, withIntermediateDirectories: true, attributes: privateFolder)
        for file in files {
            try manager.createDirectory(
                atPath: (file.target as NSString).deletingLastPathComponent, withIntermediateDirectories: true,
                attributes: [.posixPermissions: 0o700])
            guard manager.createFile(atPath: file.target, contents: file.bytes, attributes: [.posixPermissions: 0o600])
            else { throw CocoaError(.fileWriteUnknown) }
        }
    }

    /// Deletes a stamped copy. A link is removed as a link, never followed.
    public static func remove(_ folder: String) throws {
        let manager = FileManager.default
        guard (try? manager.attributesOfItem(atPath: folder)) != nil else { return }
        try manager.removeItem(atPath: folder)
    }

    // Where an entry goes for this agent (relative to edgepad/, or the marketplace file beside it); nil when not
    // copied.
    private static func target(_ agent: String, _ entry: String) -> String? {
        if entry == marketplaceTemplate { return marketplace }
        // The Gemini CLI and OpenCode templates are written by HookConfigInstaller, not copied into a plugin.
        if entry == HookConfigInstaller.geminiTemplate || entry.hasPrefix(HookConfigInstaller.openCodeFolder) {
            return nil
        }
        if entry == claudeHooks || entry == codexHooks {
            return entry == (agent == AgentToken.claude.rawValue ? claudeHooks : codexHooks) ? hooks : nil
        }
        let other = agent == AgentToken.claude.rawValue ? codexOnly : claudeOnly
        let skipped = other.contains { $0.hasSuffix("/") ? entry.hasPrefix($0) : entry == $0 }
        return skipped ? nil : entry
    }
}
