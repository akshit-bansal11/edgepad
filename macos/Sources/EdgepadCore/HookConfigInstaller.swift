import Foundation

/// Connect and Disconnect for Gemini CLI and OpenCode, the two tools with no plugin CLI: Edgepad writes its own entry
/// into the tool's config and takes only that entry out again. Gemini CLI: five hook entries named `edgepad` merged
/// into `~/.gemini/settings.json`; every other key, hook and group is kept. OpenCode: the file `edgepad.ts` in
/// `~/.config/opencode/plugins/`, which a file without our first-line marker is never overwritten or deleted. A config
/// that is not strict JSON (comments, a trailing comma, a root that is not an object, a `hooks` of the wrong shape) is
/// refused and reported, never rewritten. An existing Gemini file is edited as text: only our groups are inserted or
/// cut out (with the comma beside them), so key order, number spelling, duplicate keys and whitespace of everything
/// else survive byte for byte. The first copy of the old file is kept as `settings.json.edgepad-backup` and never
/// overwritten; the new file replaces the old whole, keeping its mode. Two `hooks` keys, or two keys of one of our
/// event names, are refused as ambiguous. The twin of the core's HookConfigInstaller.
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

    // One member of an object or element of an array, as byte offsets into the file: start..<end is the whole item
    // (`"key": value` for a member), valueStart..<end the value.
    private struct Item {
        let start: Int
        let end: Int
        let key: String?
        let valueStart: Int
    }

    private func connectGemini() throws {
        let template = try JSONSerialization.jsonObject(with: try templateBytes(Self.geminiTemplate))
        guard let ours = (template as? [String: Any])?["hooks"] as? [String: Any] else {
            throw Refused(reason: "The Gemini template has no hooks")
        }
        guard FileManager.default.fileExists(atPath: geminiFile) else {
            let fresh = try Self.render(["hooks": ours])
            if let why = writePrivately(fresh + "\n", to: geminiFile) { throw Refused(reason: why) }
            return
        }
        let (parsed, original) = try loadGemini()
        try Self.checkShape(parsed, ours)
        if Self.isCurrent(parsed, ours) { return }
        // Only our entries are cut out and put in, as text: every other byte of the file stays where it is.
        var bytes = [UInt8](original)
        try Self.removeOurs(&bytes)
        try Self.insertOurs(&bytes, ours)
        try save(geminiFile, bytes, original: original)
    }

    private func disconnectGemini() throws {
        guard FileManager.default.fileExists(atPath: geminiFile) else { return }
        let (_, original) = try loadGemini()
        var bytes = [UInt8](original)
        try Self.removeOurs(&bytes)
        try save(geminiFile, bytes, original: original)
    }

    private func loadGemini() throws -> (root: [String: Any], bytes: Data) {
        let data = try Data(contentsOf: URL(fileURLWithPath: geminiFile))
        guard data.count <= Self.maxBytes else { throw Refused(reason: "The Gemini settings file is too large") }
        // Foundation's parser forgives some things (a trailing comma); rewriting such a file is refused, not repaired.
        guard Self.isStrictJSON([UInt8](data)) else { throw Refused(reason: "The Gemini settings are not strict JSON") }
        guard let root = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw Refused(reason: "The Gemini settings are not an object")
        }
        return (root, data)
    }

    // A `hooks` that is not an object, or an event of ours that is not an array, cannot take our entries.
    private static func checkShape(_ root: [String: Any], _ ours: [String: Any]) throws {
        guard let node = root["hooks"] else { return }
        guard let hooks = node as? [String: Any] else { throw Refused(reason: "hooks is not an object") }
        for name in ours.keys where hooks[name] != nil && !(hooks[name] is [Any]) {
            throw Refused(reason: "A hooks entry is not an array")
        }
    }

    // True when the file already holds exactly our current entries and no other of ours: nothing to write.
    private static func isCurrent(_ root: [String: Any], _ ours: [String: Any]) -> Bool {
        let hooks = root["hooks"] as? [String: Any] ?? [:]
        var present = 0
        for (_, groups) in hooks {
            for group in groups as? [Any] ?? [] where isOurGroup(group) {
                present += 1
            }
        }
        var wanted = 0
        for (name, groups) in ours {
            let have = hooks[name] as? [Any] ?? []
            for group in groups as? [Any] ?? [] {
                wanted += 1
                guard let want = group as? [String: Any] else { return false }
                var same = false
                for candidate in have {
                    guard let c = candidate as? [String: Any] else { continue }
                    if NSDictionary(dictionary: c).isEqual(to: want) { same = true }
                }
                if !same { return false }
            }
        }
        return present == wanted
    }

    // A group of hook entries all of which are ours (Edgepad writes whole groups of its own, never into a user's).
    private static func isOurGroup(_ group: Any) -> Bool {
        guard let list = (group as? [String: Any])?["hooks"] as? [Any], !list.isEmpty else { return false }
        return list.allSatisfy { isOurs($0) }
    }

    private static func isOurs(_ hook: Any) -> Bool {
        guard let h = hook as? [String: Any], h["name"] as? String == hookName,
            let command = h["command"] as? String
        else { return false }
        return command.contains(geminiCommand)
    }

    // Takes out every group that is ours, then the event list and the `hooks` object that this alone emptied; the
    // text around each cut, the user's formatting included, is left as it was.
    private static func removeOurs(_ bytes: inout [UInt8]) throws {
        while try removeOneOurs(&bytes) {}
    }

    private static func removeOneOurs(_ bytes: inout [UInt8]) throws -> Bool {
        let root = try rootOpen(bytes)
        let top = items(bytes, open: root)
        let found = top.indices.filter { top[$0].key == "hooks" }
        guard found.count <= 1 else { throw Refused(reason: "The Gemini settings have two hooks keys") }
        guard let h = found.first, bytes[top[h].valueStart] == 0x7B else { return false }
        let hooks = items(bytes, open: top[h].valueStart)
        for (e, event) in hooks.enumerated() where bytes[event.valueStart] == 0x5B {
            let groups = items(bytes, open: event.valueStart)
            for (g, group) in groups.enumerated() where bytes[group.start] == 0x7B && isOurGroup(parse(bytes, group)) {
                if groups.count > 1 {
                    delete(&bytes, open: event.valueStart, index: g)
                } else if hooks.count > 1 {
                    delete(&bytes, open: top[h].valueStart, index: e)
                } else {
                    delete(&bytes, open: root, index: h)
                }
                return true
            }
        }
        return false
    }

    // Puts our groups at the end of their event's list, adding the event, or `hooks` itself, when it is missing.
    private static func insertOurs(_ bytes: inout [UInt8], _ ours: [String: Any]) throws {
        let root = try rootOpen(bytes)
        let found = items(bytes, open: root).filter { $0.key == "hooks" }
        guard found.count <= 1 else { throw Refused(reason: "The Gemini settings have two hooks keys") }
        guard let hooks = found.first else {
            append(&bytes, open: root, Array("\"hooks\":".utf8) + (try compact(ours)))
            return
        }
        guard bytes[hooks.valueStart] == 0x7B else { throw Refused(reason: "hooks is not an object") }
        for name in ours.keys.sorted() {
            let groups = ours[name] as? [Any] ?? []
            let events = items(bytes, open: hooks.valueStart).filter { $0.key == name }
            guard events.count <= 1 else { throw Refused(reason: "The Gemini settings have two \(name) keys") }
            if let event = events.first {
                guard bytes[event.valueStart] == 0x5B else { throw Refused(reason: "A hooks entry is not an array") }
                for group in groups {
                    append(&bytes, open: event.valueStart, try compact(group))
                }
            } else {
                append(&bytes, open: hooks.valueStart, Array("\"\(name)\":".utf8) + (try compact(groups)))
            }
        }
    }

    private static func rootOpen(_ bytes: [UInt8]) throws -> Int {
        let root = skipSpace(bytes, 0)
        guard root < bytes.count, bytes[root] == 0x7B else {
            throw Refused(reason: "The Gemini settings are not an object")
        }
        return root
    }

    private static func compact(_ value: Any) throws -> [UInt8] {
        [UInt8](try JSONSerialization.data(withJSONObject: value, options: [.sortedKeys, .withoutEscapingSlashes]))
    }

    private static func parse(_ bytes: [UInt8], _ item: Item) -> Any {
        (try? JSONSerialization.jsonObject(with: Data(bytes[item.start..<item.end]))) ?? NSNull()
    }

    private static func render(_ root: [String: Any]) throws -> String {
        let data = try JSONSerialization.data(
            withJSONObject: root, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])
        return String(decoding: data, as: UTF8.self)
    }

    // MARK: Strict JSON (RFC 8259: no comments, no trailing comma, no single quotes, no bare words)

    private static func isStrictJSON(_ bytes: [UInt8]) -> Bool {
        var i = skipSpace(bytes, 0)
        guard value(bytes, &i, depth: 0) else { return false }
        return skipSpace(bytes, i) == bytes.count
    }

    private static func value(_ bytes: [UInt8], _ i: inout Int, depth: Int) -> Bool {
        guard depth < 256, i < bytes.count else { return false }
        switch bytes[i] {
        case 0x7B: return container(bytes, &i, depth: depth, close: 0x7D)
        case 0x5B: return container(bytes, &i, depth: depth, close: 0x5D)
        case 0x22: return string(bytes, &i)
        case 0x74: return word(bytes, &i, "true")
        case 0x66: return word(bytes, &i, "false")
        case 0x6E: return word(bytes, &i, "null")
        default: return number(bytes, &i)
        }
    }

    private static func container(_ bytes: [UInt8], _ i: inout Int, depth: Int, close: UInt8) -> Bool {
        let isObject = close == 0x7D
        i = skipSpace(bytes, i + 1)
        if i < bytes.count, bytes[i] == close {
            i += 1
            return true
        }
        while true {
            if isObject {
                guard i < bytes.count, bytes[i] == 0x22, string(bytes, &i) else { return false }
                i = skipSpace(bytes, i)
                guard i < bytes.count, bytes[i] == 0x3A else { return false }
                i = skipSpace(bytes, i + 1)
            }
            guard value(bytes, &i, depth: depth + 1) else { return false }
            i = skipSpace(bytes, i)
            guard i < bytes.count else { return false }
            if bytes[i] == close {
                i += 1
                return true
            }
            guard bytes[i] == 0x2C else { return false }
            i = skipSpace(bytes, i + 1)
        }
    }

    private static func word(_ bytes: [UInt8], _ i: inout Int, _ literal: String) -> Bool {
        let expected = Array(literal.utf8)
        guard i + expected.count <= bytes.count, Array(bytes[i..<i + expected.count]) == expected else { return false }
        i += expected.count
        return true
    }

    private static func string(_ bytes: [UInt8], _ i: inout Int) -> Bool {
        i += 1
        while i < bytes.count {
            let c = bytes[i]
            if c == 0x22 {
                i += 1
                return true
            }
            if c < 0x20 { return false }
            if c == 0x5C {
                guard i + 1 < bytes.count else { return false }
                if bytes[i + 1] == 0x75 {
                    guard i + 5 < bytes.count, bytes[i + 2...i + 5].allSatisfy({ isHex($0) }) else { return false }
                    i += 6
                } else {
                    guard "\"\\/bfnrt".utf8.contains(bytes[i + 1]) else { return false }
                    i += 2
                }
            } else {
                i += 1
            }
        }
        return false
    }

    private static func isHex(_ c: UInt8) -> Bool {
        (c >= 0x30 && c <= 0x39) || (c >= 0x41 && c <= 0x46) || (c >= 0x61 && c <= 0x66)
    }

    private static func digits(_ bytes: [UInt8], _ i: inout Int) -> Bool {
        let start = i
        while i < bytes.count, bytes[i] >= 0x30, bytes[i] <= 0x39 { i += 1 }
        return i > start
    }

    private static func number(_ bytes: [UInt8], _ i: inout Int) -> Bool {
        if i < bytes.count, bytes[i] == 0x2D { i += 1 }
        guard i < bytes.count else { return false }
        if bytes[i] == 0x30 {
            i += 1
        } else {
            guard bytes[i] >= 0x31, bytes[i] <= 0x39, digits(bytes, &i) else { return false }
        }
        if i < bytes.count, bytes[i] == 0x2E {
            i += 1
            guard digits(bytes, &i) else { return false }
        }
        if i < bytes.count, bytes[i] == 0x65 || bytes[i] == 0x45 {
            i += 1
            if i < bytes.count, bytes[i] == 0x2B || bytes[i] == 0x2D { i += 1 }
            guard digits(bytes, &i) else { return false }
        }
        return true
    }

    // MARK: Text scanning (the file is already known to be strict JSON, so this only has to find the edges)

    private static let delimiters = Set<UInt8>(Array(",]} \t\r\n".utf8))

    private static func skipSpace(_ bytes: [UInt8], _ from: Int) -> Int {
        var i = from
        while i < bytes.count, bytes[i] == 0x20 || bytes[i] == 0x09 || bytes[i] == 0x0A || bytes[i] == 0x0D {
            i += 1
        }
        return i
    }

    // `open` is the opening quote; the result is the index after the closing one.
    private static func stringEnd(_ bytes: [UInt8], _ open: Int) -> Int {
        var i = open + 1
        while i < bytes.count {
            if bytes[i] == 0x5C {
                i += 2
            } else if bytes[i] == 0x22 {
                return i + 1
            } else {
                i += 1
            }
        }
        return bytes.count
    }

    private static func valueEnd(_ bytes: [UInt8], _ start: Int) -> Int {
        guard start < bytes.count else { return start }
        switch bytes[start] {
        case 0x22:
            return stringEnd(bytes, start)
        case 0x7B, 0x5B:
            var depth = 0
            var i = start
            while i < bytes.count {
                switch bytes[i] {
                case 0x22:
                    i = stringEnd(bytes, i)
                    continue
                case 0x7B, 0x5B:
                    depth += 1
                case 0x7D, 0x5D:
                    depth -= 1
                    if depth == 0 { return i + 1 }
                default:
                    break
                }
                i += 1
            }
            return bytes.count
        default:
            var i = start
            while i < bytes.count, !delimiters.contains(bytes[i]) { i += 1 }
            return i
        }
    }

    // The members of the object, or the elements of the array, opening at `open`.
    private static func items(_ bytes: [UInt8], open: Int) -> [Item] {
        let isObject = bytes[open] == 0x7B
        var found: [Item] = []
        var i = skipSpace(bytes, open + 1)
        while i < bytes.count, bytes[i] != 0x7D, bytes[i] != 0x5D {
            let start = i
            var key: String?
            var valueStart = i
            if isObject {
                let keyEnd = stringEnd(bytes, i)
                let literal = [UInt8(ascii: "[")] + bytes[start..<keyEnd] + [UInt8(ascii: "]")]
                key = (try? JSONSerialization.jsonObject(with: Data(literal)) as? [String])?.first
                valueStart = skipSpace(bytes, skipSpace(bytes, keyEnd) + 1)
            }
            let end = valueEnd(bytes, valueStart)
            found.append(Item(start: start, end: end, key: key, valueStart: valueStart))
            i = skipSpace(bytes, end)
            if i < bytes.count, bytes[i] == 0x2C { i = skipSpace(bytes, i + 1) }
        }
        return found
    }

    // Cuts one item with the comma that joins it to its neighbour. The exact inverse of `append` for its last item.
    private static func delete(_ bytes: inout [UInt8], open: Int, index: Int) {
        let list = items(bytes, open: open)
        let item = list[index]
        if list.count == 1 {
            bytes.removeSubrange(item.start..<item.end)
        } else if index < list.count - 1 {
            bytes.removeSubrange(item.start..<list[index + 1].start)
        } else {
            bytes.removeSubrange(list[index - 1].end..<item.end)
        }
    }

    // Adds `text` after the last item as `,` + the last item's own line indent + `text`; into an empty container,
    // straight after its opening bracket.
    private static func append(_ bytes: inout [UInt8], open: Int, _ text: [UInt8]) {
        guard let last = items(bytes, open: open).last else {
            bytes.insert(contentsOf: text, at: open + 1)
            return
        }
        var lead: [UInt8] = []
        var j = last.start
        while j > 0, bytes[j - 1] == 0x20 || bytes[j - 1] == 0x09 { j -= 1 }
        if j == 0 || bytes[j - 1] == 0x0A {
            lead = [0x0A] + bytes[j..<last.start]
        }
        bytes.insert(contentsOf: [UInt8(ascii: ",")] + lead + text, at: last.end)
    }

    // Writes only a changed file, after copying the old one aside; an existing backup is the first and is kept.
    private func save(_ file: String, _ bytes: [UInt8], original: Data) throws {
        guard Data(bytes) != original else { return }
        guard (try? JSONSerialization.jsonObject(with: Data(bytes)) as? [String: Any]) != nil else {
            throw Refused(reason: "The edited Gemini settings would not be valid JSON")
        }
        let manager = FileManager.default
        let mode = (try manager.attributesOfItem(atPath: file))[.posixPermissions] as? NSNumber
        let backup = file + Self.backupSuffix
        if !manager.fileExists(atPath: backup) {
            try manager.copyItem(atPath: file, toPath: backup)
        }
        if let why = writePrivately(String(decoding: bytes, as: UTF8.self), to: file) { throw Refused(reason: why) }
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
