import Foundation

/// The plugins' hook command, run as the app's own executable: `"<app>" --hook <claude|codex> <perm|session|usage|spend>`,
/// the hook JSON on stdin for perm and session, the JSON as the next argument for usage and spend. It hands the event to
/// the running app over the socket named in hook.json (one request line, one reply line) and, for a permission request
/// only, prints the app's allow or deny in the PermissionRequest output shape. Rules kept whatever happens: the exit code
/// is 0 (2 would block a tool); nothing is printed except that decision (no output means "defer": the agent asks as
/// usual; for SessionStart and UserPromptSubmit stdout would reach the model); the token and the request are never
/// printed or logged; the agent comes from argv, never the payload; only whitelisted fields leave. The server must be
/// this user too: hook.json must be this user's and private, and the socket's peer uid this process's.
/// protocol/hook-vectors.txt CLIENT lines hold the exact requests. The twin of the laptop core's HookClient; main.swift
/// calls `runConsole` before anything of the app starts.
public enum HookClient {
    public static let flag = "--hook"
    public static let stdinCap = 1 << 20
    public static let argCap = 8192
    public static let hookFileCap = 4096
    public static let replyCap = 4096
    /// Characters, above the laptop's 4096-byte cap so its rule decides exactly.
    public static let inputCap = 5000
    public static let maxParts = 1024
    public static let denyMessage = "Denied from Edgepad on the phone."
    public static let connectWait: TimeInterval = 2
    /// The perm hook's own timeout is 120 s; the laptop answers within 30 s.
    public static let replyWait: TimeInterval = 35

    private static let agents = [AgentToken.claude.rawValue, AgentToken.codex.rawValue]
    private static let kinds = [HookProtocol.perm, HookProtocol.session, HookProtocol.usage, HookProtocol.spend]
    // The lifecycle events the laptop's SessionBoard maps, and the Notification types that mean "the agent waits".
    private static let events = ["SessionStart", "UserPromptSubmit", "Notification", "Stop", "SessionEnd"]
    private static let notifications = ["permission_prompt", "idle_prompt", "agent_needs_input"]
    // Bash `command`; Edit/Write/MultiEdit/Read/NotebookEdit `file_path`; WebFetch `url`. The laptop picks per tool.
    private static let inputFields = ["command", "file_path", "url"]

    /// True when the app was started as a hook command (`--hook` first); `arguments` without the program itself.
    public static func isHook(_ arguments: [String]) -> Bool {
        arguments.first == flag
    }

    /// `run` on the process's own stdin and stdout. Always 0.
    public static func runConsole(_ arguments: [String]) -> Int32 {
        let output = run(
            arguments, stdin: { readCapped(FileHandle.standardInput) }, file: HookEndpoint.defaultFile(),
            connect: connectSocket, replyWait: replyWait)
        if let output {
            FileHandle.standardOutput.write(Data((output + "\n").utf8))
        }
        return 0
    }

    /// Handles `--hook <agent> <kind> [json]`: the line to print, or nil to print nothing. Never throws.
    static func run(
        _ arguments: [String], stdin: () -> Data?, file: String?, connect: (String) -> HookStream?,
        replyWait: TimeInterval
    ) -> String? {
        guard arguments.count >= 3, arguments[0] == flag, agents.contains(arguments[1]), kinds.contains(arguments[2])
        else { return nil }
        let agent = arguments[1]
        let kind = arguments[2]
        let input: Data?
        if kind == HookProtocol.perm || kind == HookProtocol.session {
            input = stdin()
        } else {
            input = arguments.count >= 4 && arguments[3].utf8.count <= argCap ? Data(arguments[3].utf8) : nil
        }
        guard let input, var message = request(agent: agent, kind: kind, input: input), let file,
            let info = readHookFile(file)
        else { return nil }
        message["token"] = info.token
        guard let line = try? JSONSerialization.data(withJSONObject: message, options: [.withoutEscapingSlashes]),
            let stream = connect(info.endpoint)
        else { return nil }
        defer { stream.close() }
        let wantsReply = kind == HookProtocol.perm
        guard stream.write(Array(line) + [UInt8(ascii: "\n")]), wantsReply,
            let decision = readDecision(stream, Date().addingTimeInterval(replyWait)),
            decision == HookProtocol.allow || decision == HookProtocol.deny
        else { return nil }
        var behavior: [String: Any] = ["behavior": decision]
        if decision == HookProtocol.deny {
            behavior["message"] = denyMessage
        }
        let output: [String: Any] = ["hookEventName": "PermissionRequest", "decision": behavior]
        let reply: [String: Any] = ["hookSpecificOutput": output]
        guard let data = try? JSONSerialization.data(withJSONObject: reply, options: [.withoutEscapingSlashes]) else {
            return nil
        }
        return String(decoding: data, as: UTF8.self)
    }

    /// The request without its token, `{"v":1,"agent","kind","d"}`, for a hook payload (perm, session) or an argument
    /// (usage, spend); nil when nothing is sent.
    public static func request(agent: String, kind: String, input: Data) -> [String: Any]? {
        guard agents.contains(agent), kinds.contains(kind), let parsed = JsonRead.object(input, maxDepth: 64) else {
            return nil
        }
        let d: [String: Any]?
        switch kind {
        case HookProtocol.perm: d = permData(agent, parsed)
        case HookProtocol.session: d = sessionData(parsed)
        default: d = parsed
        }
        guard let d else { return nil }
        return ["v": 1, "agent": agent, "kind": kind, "d": d]
    }

    private static func permData(_ agent: String, _ hook: [String: Any]) -> [String: Any]? {
        let tool = text(hook["tool_name"], 64)
        guard !tool.isEmpty else { return nil }
        var input: [String: Any] = [:]
        var truncated = false
        if let raw = hook["tool_input"] as? [String: Any] {
            for key in inputFields {
                if let got = field(agent, key, raw[key]) {
                    input[key] = got.value
                    truncated = truncated || got.cut
                }
            }
        }
        var d: [String: Any] = [
            "session_id": text(hook["session_id"], 128), "cwd": text(hook["cwd"], 1024), "tool_name": tool,
            "tool_input": input,
        ]
        // Only when a field was cut, so the laptop offers Deny only: what it would show is not all that runs.
        if truncated {
            d["truncated"] = true
        }
        return d
    }

    private static func sessionData(_ hook: [String: Any]) -> [String: Any]? {
        guard let event = hook["hook_event_name"] as? String, events.contains(event) else { return nil }
        var d: [String: Any] = [
            "hook_event_name": event, "session_id": text(hook["session_id"], 128), "cwd": text(hook["cwd"], 1024),
        ]
        if event == "Notification" {
            guard let type = hook["notification_type"] as? String, notifications.contains(type) else { return nil }
            d["notification_type"] = type
        }
        return d
    }

    // A string cut to inputCap; for Codex's `command` also its array form, a list of strings sent as is unless joined
    // for display (`HookProtocol.joinCommand`) it passes inputCap, then as the cut joined string. Anything else is left
    // out. `cut` is true when the value was cut.
    private static func field(_ agent: String, _ key: String, _ value: Any?) -> (value: Any, cut: Bool)? {
        if let text = value as? String {
            return (HookProtocol.cut(text, inputCap), text.utf16.count > inputCap)
        }
        guard agent == AgentToken.codex.rawValue, key == "command", let parts = value as? [Any],
            (1...maxParts).contains(parts.count)
        else { return nil }
        let texts = parts.compactMap { $0 as? String }
        guard texts.count == parts.count else { return nil }
        let joined = HookProtocol.joinCommand(texts)
        guard joined.utf16.count > inputCap else { return (texts, false) }
        return (HookProtocol.cut(joined, inputCap), true)
    }

    // A string cut to `max` UTF-16 units, or "" for anything else.
    private static func text(_ value: Any?, _ max: Int) -> String {
        (value as? String).map { HookProtocol.cut($0, max) } ?? ""
    }

    /// `(endpoint, token)` from hook.json, or nil: it must be a regular file (not a link) of this user, with no group or
    /// other permission bits, at most 4 KiB, version 1, a base64url token and an absolute socket path.
    static func readHookFile(_ path: String) -> (endpoint: String, token: String)? {
        let fd = Darwin.open(path, O_RDONLY | O_NOFOLLOW | O_CLOEXEC)
        guard fd >= 0 else { return nil }
        defer { Darwin.close(fd) }
        var info = stat()
        guard fstat(fd, &info) == 0, info.st_mode & 0o170000 == 0o100000, info.st_uid == geteuid(),
            info.st_mode & 0o077 == 0, info.st_size <= off_t(hookFileCap)
        else { return nil }
        var buffer = [UInt8](repeating: 0, count: hookFileCap)
        let count = buffer.withUnsafeMutableBytes { Darwin.read(fd, $0.baseAddress, hookFileCap) }
        guard count > 0, let root = JsonRead.object(Data(buffer.prefix(count)), maxDepth: 4),
            JsonRead.int32(root["v"]) == 1,
            let token = root["token"] as? String, isToken(token),
            let endpoint = root["endpoint"] as? String, endpoint.hasPrefix("/")
        else { return nil }
        return (endpoint, token)
    }

    static func isToken(_ token: String) -> Bool {
        (16...128).contains(token.utf8.count)
            && token.unicodeScalars.allSatisfy {
                ("A"..."Z").contains($0) || ("a"..."z").contains($0) || ("0"..."9").contains($0) || $0 == "_"
                    || $0 == "-"
            }
    }

    // The decision in the first reply line, or nil: none in time, longer than 4 KiB, or not a reply.
    private static func readDecision(_ stream: HookStream, _ deadline: Date) -> String? {
        var reply: [UInt8] = []
        while reply.count <= replyCap {
            guard let chunk = stream.read(limit: replyCap + 1 - reply.count, until: deadline), !chunk.isEmpty else {
                return nil
            }
            let start = reply.count
            reply.append(contentsOf: chunk)
            guard let end = reply[start...].firstIndex(of: UInt8(ascii: "\n")) else { continue }
            guard let root = JsonRead.object(Data(reply[..<end]), maxDepth: 4), JsonRead.int32(root["v"]) == 1,
                let decision = root["decision"] as? String,
                [HookProtocol.allow, HookProtocol.deny, HookProtocol.later].contains(decision)
            else { return nil }
            return decision
        }
        return nil
    }

    // Connects to the app's socket within 2 s; nil when it is not there or another user listens on it.
    private static func connectSocket(_ path: String) -> HookStream? {
        let fd = socket(AF_UNIX, SOCK_STREAM, 0)
        guard fd >= 0 else { return nil }
        var on: Int32 = 1
        _ = setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &on, socklen_t(MemoryLayout<Int32>.size))
        var wait = timeval(tv_sec: Int(connectWait), tv_usec: 0)
        _ = setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &wait, socklen_t(MemoryLayout<timeval>.size))
        let connected = withUnixAddress(path) { Darwin.connect(fd, $0, $1) }
        guard connected == 0, peerUid(fd) == geteuid() else {
            Darwin.close(fd)
            return nil
        }
        return SocketStream(fd: fd)
    }

    // Stdin up to the cap; nil when it is longer.
    private static func readCapped(_ handle: FileHandle) -> Data? {
        var data = Data()
        while true {
            let chunk = handle.availableData
            if chunk.isEmpty { return data }
            data.append(chunk)
            if data.count > stdinCap { return nil }
        }
    }
}
