import Foundation

/// The tool fields a permission request may carry, as received (the hook client already whitelisted them).
/// `commandSafe` is false when `command` was a list (Codex's array form) with a control character inside one element:
/// joined with spaces it would not show what runs, so the broker offers Deny only.
public struct ToolInput: Equatable {
    public let command: String?
    public let filePath: String?
    public let url: String?
    public let commandSafe: Bool

    public init(command: String? = nil, filePath: String? = nil, url: String? = nil, commandSafe: Bool = true) {
        self.command = command
        self.filePath = filePath
        self.url = url
        self.commandSafe = commandSafe
    }
}

/// A PermissionRequest: `tool` is 1..24 of A-Za-z0-9_.-.
public struct PermRequest: Equatable {
    public let token: String
    public let agent: String
    public let session: String
    public let cwd: String
    public let tool: String
    public let input: ToolInput

    public init(token: String, agent: String, session: String, cwd: String, tool: String, input: ToolInput) {
        self.token = token
        self.agent = agent
        self.session = session
        self.cwd = cwd
        self.tool = tool
        self.input = input
    }
}

/// A lifecycle hook. `notificationType` only for a Notification.
public struct SessionRequest: Equatable {
    public let token: String
    public let agent: String
    public let session: String
    public let event: String
    public let cwd: String
    public let notificationType: String?
}

/// Limits as the agent reported them (Claude's `kind` or Codex minutes) and the context fill, for `LimitsBoard`.
public struct UsageRequest: Equatable {
    public let token: String
    public let agent: String
    public let windows: [RawLimit]
    public let contextPct: Double?
}

/// Token and cent totals from a spend scanner, for `SpendBoard`.
public struct SpendRequest: Equatable {
    public let token: String
    public let agent: String
    public let totals: SpendTotals
}

/// A request line of the local hook channel, once read. The agent always came from the plugin's argv, never the
/// payload.
public enum HookRequest: Equatable {
    case perm(PermRequest)
    case session(SessionRequest)
    case usage(UsageRequest)
    case spend(SpendRequest)

    public var token: String {
        switch self {
        case .perm(let r): return r.token
        case .session(let r): return r.token
        case .usage(let r): return r.token
        case .spend(let r): return r.token
        }
    }

    public var agent: String {
        switch self {
        case .perm(let r): return r.agent
        case .session(let r): return r.agent
        case .usage(let r): return r.agent
        case .spend(let r): return r.agent
        }
    }
}

/// The wire of the local hook channel: one request line of JSON `{"v":1,"token","agent","kind","d"}` and one reply
/// line. Strict: a wrong type, a missing field, an unknown agent or kind, a tool name outside its charset, depth over 8
/// or a line over 64 KiB is no request at all. Strings are capped here; text is cleaned later, where it is shown.
/// protocol/hook-vectors.txt holds the cases the Node and C# ends check too. The twin of the laptop core's
/// HookProtocol.
public enum HookProtocol {
    public static let maxLineBytes = 64 * 1024
    public static let maxDepth = 8
    public static let maxToolLength = 24
    public static let maxSessionLength = SessionBoard.maxIdLength
    public static let maxCwdLength = 1024
    public static let maxTextLength = 16 * 1024
    public static let maxNameLength = 64
    public static let maxWindows = 16
    public static let maxCommandParts = 1024

    public static let perm = "perm"
    public static let session = "session"
    public static let usage = "usage"
    public static let spend = "spend"

    public static let allow = "allow"
    public static let deny = "deny"
    public static let later = "defer"

    /// The reply line to a permission request, line feed included.
    public static func decisionReply(_ decision: String) -> [UInt8] {
        Array(("{\"v\":1,\"decision\":\"" + decision + "\"}\n").utf8)
    }

    /// The reply line to every other kind, line feed included.
    public static func okReply() -> [UInt8] {
        Array("{\"v\":1,\"ok\":true}\n".utf8)
    }

    /// The request in `line` (no line feed), or nil when it is not exactly one.
    public static func parse(_ line: [UInt8]) -> HookRequest? {
        guard (1...maxLineBytes).contains(line.count) else { return nil }
        guard let root = JsonRead.object(Data(line), maxDepth: maxDepth) else { return nil }
        guard JsonRead.int32(root["v"]) == 1,
            let token = str(root, "token", 256), !token.isEmpty,
            let agent = str(root, "agent", 16), DashWire.isAgent(agent),
            let kind = str(root, "kind", 16),
            let d = root["d"] as? [String: Any]
        else { return nil }
        switch kind {
        case perm: return readPerm(token, agent, d).map { .perm($0) }
        case session: return readSession(token, agent, d).map { .session($0) }
        case usage: return readUsage(token, agent, d).map { .usage($0) }
        case spend: return readSpend(token, agent, d).map { .spend($0) }
        default: return nil
        }
    }

    /// `text` cut to `max` UTF-16 units, never inside a surrogate pair.
    public static func cut(_ text: String, _ max: Int) -> String {
        let units = Array(text.utf16)
        guard units.count > max else { return text }
        let end = max > 0 && UTF16.isLeadSurrogate(units[max - 1]) ? max - 1 : max
        return String(decoding: units[..<end], as: UTF16.self)
    }

    private static func readPerm(_ token: String, _ agent: String, _ d: [String: Any]) -> PermRequest? {
        guard let tool = str(d, "tool_name", maxNameLength), (1...maxToolLength).contains(tool.utf16.count),
            tool.unicodeScalars.allSatisfy(PermAskBody.isToolChar),
            let session = optStr(d, "session_id", maxSessionLength),
            let cwd = optStr(d, "cwd", maxCwdLength)
        else { return nil }

        var input = ToolInput()
        if let raw = d["tool_input"] {
            guard let fields = raw as? [String: Any],
                let file = optStr(fields, "file_path", maxTextLength),
                let url = optStr(fields, "url", maxTextLength),
                let command = readCommand(fields)
            else { return nil }
            input = ToolInput(command: command.text, filePath: file, url: url, commandSafe: command.safe)
        }
        return PermRequest(token: token, agent: agent, session: session ?? "", cwd: cwd ?? "", tool: tool, input: input)
    }

    // A string, or (Codex's array form) a list of strings joined with spaces. Nil on anything else, JSON null included.
    private static func readCommand(_ input: [String: Any]) -> (text: String?, safe: Bool)? {
        guard let value = input["command"] else { return (nil, true) }
        if let text = value as? String {
            return (cut(text, maxTextLength), true)
        }
        guard let parts = value as? [Any], (1...maxCommandParts).contains(parts.count) else { return nil }
        var texts: [String] = []
        var safe = true
        for part in parts {
            guard let text = part as? String else { return nil }
            safe = safe && !text.unicodeScalars.contains { $0.properties.generalCategory == .control }
            texts.append(text)
        }
        return (cut(texts.joined(separator: " "), maxTextLength), safe)
    }

    private static func readSession(_ token: String, _ agent: String, _ d: [String: Any]) -> SessionRequest? {
        guard let name = str(d, "hook_event_name", maxNameLength), !name.isEmpty,
            let session = optStr(d, "session_id", maxSessionLength),
            let cwd = optStr(d, "cwd", maxCwdLength),
            let type = optStr(d, "notification_type", maxNameLength)
        else { return nil }
        return SessionRequest(
            token: token, agent: agent, session: session ?? "", event: name, cwd: cwd ?? "",
            notificationType: name == "Notification" ? type : nil)
    }

    private static func readUsage(_ token: String, _ agent: String, _ d: [String: Any]) -> UsageRequest? {
        var windows: [RawLimit] = []
        if let raw = d["limits"] {
            guard let limits = raw as? [Any], limits.count <= maxWindows else { return nil }
            for item in limits {
                guard let limit = item as? [String: Any],
                    let kind = str(limit, "kind", maxNameLength), !kind.isEmpty,
                    let percent = JsonRead.double(limit["percentUsed"]),
                    let resets = resetsAt(limit)
                else { return nil }
                windows.append(RawLimit(kind, percent, resets))
            }
        }

        var context: Double?
        if let raw = d["context"], !(raw is NSNull) {
            // {"percent": n} or a bare n; an object without "percent" is no number.
            var number: Any = raw
            if let object = raw as? [String: Any] {
                number = object["percent"] ?? object
            }
            if number is NSNull {
                return UsageRequest(token: token, agent: agent, windows: windows, contextPct: nil)
            }
            guard let value = JsonRead.double(number) else { return nil }
            context = value
        }
        return UsageRequest(token: token, agent: agent, windows: windows, contextPct: context)
    }

    // resetsAt is an ISO date, epoch seconds or milliseconds (as a number or text), null or absent. Nil when it is
    // something else; otherwise the text kept (nil inside for none).
    private static func resetsAt(_ limit: [String: Any]) -> String?? {
        guard let value = limit["resetsAt"], !(value is NSNull) else { return .some(nil) }
        if let text = value as? String {
            return text.utf16.count <= maxNameLength ? .some(text) : nil
        }
        guard let number = JsonRead.int64(value), number >= 0 else { return nil }
        return .some(String(number))
    }

    private static func readSpend(_ token: String, _ agent: String, _ d: [String: Any]) -> SpendRequest? {
        guard let today = period(d, "today"), let week = period(d, "week"), let month = period(d, "month") else {
            return nil
        }
        if let unpriced = d["unpriced"] {
            guard let flag = unpriced as? NSNumber, CFGetTypeID(flag) == CFBooleanGetTypeID() else { return nil }
        }
        let totals = SpendTotals(
            tokToday: today.tok, centsToday: today.cents, tokWeek: week.tok, centsWeek: week.cents,
            tokMonth: month.tok, centsMonth: month.cents)
        return SpendRequest(token: token, agent: agent, totals: totals)
    }

    private static func period(_ d: [String: Any], _ name: String) -> (tok: Int64, cents: Int64)? {
        guard let period = d[name] as? [String: Any], let tok = JsonRead.int64(period["tok"]),
            let cents = JsonRead.int64(period["cents"])
        else { return nil }
        return (tok, cents)
    }

    // A required string, cut to `max` UTF-16 units; nil when absent or not a string.
    private static func str(_ object: [String: Any], _ name: String, _ max: Int) -> String? {
        (object[name] as? String).map { cut($0, max) }
    }

    // An optional string: the value (nil inside when absent or JSON null), or nil when present with another type.
    private static func optStr(_ object: [String: Any], _ name: String, _ max: Int) -> String?? {
        guard let value = object[name], !(value is NSNull) else { return .some(nil) }
        guard let text = value as? String else { return nil }
        return .some(cut(text, max))
    }
}
