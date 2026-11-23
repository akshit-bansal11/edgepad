import Foundation

/// Bytes read from the start or from an offset: a rollout file, or a test's buffer.
public protocol ByteSource: AnyObject {
    /// The whole length, or nil when the source cannot seek.
    var length: UInt64? { get }
    func seek(to offset: UInt64) throws
    /// Up to `count` bytes; empty at the end.
    func read(upTo count: Int) throws -> Data
}

/// A file opened for reading. Closed when released.
public final class FileByteSource: ByteSource {
    private let handle: FileHandle

    public init(path: String) throws {
        handle = try FileHandle(forReadingFrom: URL(fileURLWithPath: path))
    }

    deinit {
        try? handle.close()
    }

    public var length: UInt64? {
        guard let end = try? handle.seekToEnd() else { return nil }
        try? handle.seek(toOffset: 0)
        return end
    }

    public func seek(to offset: UInt64) throws {
        try handle.seek(toOffset: offset)
    }

    public func read(upTo count: Int) throws -> Data {
        try handle.read(upToCount: count) ?? Data()
    }
}

/// One rate-limit window of a Codex rollout. `resetsAtSec` is epoch seconds, -1 unknown.
public struct RolloutWindow: Equatable {
    public let minutes: Int
    public let pct: Int
    public let resetsAtSec: Int64

    public init(minutes: Int, pct: Int, resetsAtSec: Int64) {
        self.minutes = minutes
        self.pct = pct
        self.resetsAtSec = resetsAtSec
    }
}

/// What the end of a rollout says: working or idle, the newest limits, what the first line says about the session, and
/// how full its context window was at the last `token_count` (the last request's tokens over `model_context_window`,
/// 0..100, -1 when either is missing).
public struct RolloutTail: Equatable {
    /// "busy" or "idle", nil when the part read holds no start or end marker.
    public let state: String?
    public let limits: [RolloutWindow]
    public let cwd: String
    public let cliVersion: String
    public let contextPct: Int

    public init(state: String?, limits: [RolloutWindow], cwd: String, cliVersion: String, contextPct: Int = -1) {
        self.state = state
        self.limits = limits
        self.cwd = cwd
        self.cliVersion = cliVersion
        self.contextPct = contextPct
    }
}

/// The cumulative usage a rollout reports last, and the model it last ran. Input includes the cached part.
public struct RolloutTotals: Equatable {
    public let model: String?
    public let input: Int64
    public let cachedInput: Int64
    public let output: Int64

    public init(model: String?, input: Int64, cachedInput: Int64, output: Int64) {
        self.model = model
        self.input = input
        self.cachedInput = cachedInput
        self.output = output
    }

    public static let zero = RolloutTotals(model: nil, input: 0, cachedInput: 0, output: 0)

    /// Input that was not served from the cache.
    public var uncached: Int64 { max(0, input - cachedInput) }

    /// Uncached input plus output; cached input is left out, as Plimsoll does for Claude.
    public var tokens: Int64 { uncached + output }
}

/// Reads the parts of a Codex rollout (`rollout-*.jsonl`, one JSON object per line) the dashboard needs. It never
/// throws on content: a line that does not parse, is too deep, is too long or is cut short is skipped. Which files may
/// be opened at all is `CodexWatcher`'s rule, not this one's. The twin of the laptop core's CodexRollout.
public enum CodexRollout {
    /// How much of the end of a file is read for the state and the limits.
    public static let tailBytes = 256 * 1024
    /// The first line holds session_meta; it is read only if it fits this.
    public static let headBytes = 64 * 1024

    private static let maxLine = 1024 * 1024
    private static let maxMinutes = 525_600
    private static let maxDepth = 16
    private static let chunk = 16 * 1024

    /// The state, the newest limits, the working folder (empty if the first line is too large to read) and the CLI
    /// version.
    public static func parseTail(_ source: ByteSource) throws -> RolloutTail {
        let scan = TailScan()
        if let length = source.length {
            try source.seek(to: 0)
            var head = Data()
            while head.count < headBytes {
                let more = try source.read(upTo: headBytes - head.count)
                if more.isEmpty { break }
                head.append(more)
            }
            if let end = head.firstIndex(of: UInt8(ascii: "\n")), end > head.startIndex {
                scan.take(head.subdata(in: head.startIndex..<end))
            }
            let start = length > UInt64(tailBytes) ? length - UInt64(tailBytes) : 0
            try source.seek(to: start)
            try forEachLine(source, dropFirst: start > 0, scan.take)
        } else {
            try forEachLine(source, dropFirst: false, scan.take)
        }
        return scan.result()
    }

    /// How much of the end of a file is read first for the newest cumulative usage.
    public static let totalsTailBytes = 1024 * 1024

    /// The last cumulative usage and the last model. The last 1 MiB is read first; the whole file only when that holds
    /// no usage, or no model and `knownModel` (what an earlier read of this file found) is nil.
    public static func totals(_ source: ByteSource, knownModel: String? = nil) throws -> RolloutTotals {
        if let length = source.length, length > UInt64(totalsTailBytes) {
            try source.seek(to: length - UInt64(totalsTailBytes))
            let end = try scanTotals(source, dropFirst: true)
            if end.found, end.model != nil || knownModel != nil {
                return RolloutTotals(
                    model: end.model ?? knownModel, input: end.input, cachedInput: end.cached, output: end.output)
            }
            try source.seek(to: 0)
        }
        let all = try scanTotals(source, dropFirst: false)
        return RolloutTotals(model: all.model, input: all.input, cachedInput: all.cached, output: all.output)
    }

    private static func scanTotals(_ source: ByteSource, dropFirst: Bool) throws -> (
        model: String?, input: Int64, cached: Int64, output: Int64, found: Bool
    ) {
        var model: String?
        var input: Int64 = 0
        var cached: Int64 = 0
        var output: Int64 = 0
        var found = false
        try forEachLine(source, dropFirst: dropFirst) { line in
            guard has(line, "turn_context") || has(line, "total_token_usage") else { return }
            guard let root = JsonRead.object(line, maxDepth: maxDepth), let hit = payloadOf(root) else { return }
            let payload = hit.payload
            if hit.type == "turn_context", let m = payload["model"] as? String {
                model = short(m)
            } else if hit.type == "token_count", let info = payload["info"] as? [String: Any],
                let usage = info["total_token_usage"] as? [String: Any]
            {
                input = tokenCount(usage, "input_tokens")
                cached = tokenCount(usage, "cached_input_tokens")
                output = tokenCount(usage, "output_tokens")
                found = true
            }
        }
        return (model, input, cached, output, found)
    }

    /// The window of a rate-limit object (`used_percent`, `window_minutes`, `resets_at`), or nil when it does not read.
    static func windowOf(_ value: Any?) -> RolloutWindow? {
        guard let e = value as? [String: Any], let pct = JsonRead.double(e["used_percent"]),
            let minutes = JsonRead.int32(e["window_minutes"]), (1...maxMinutes).contains(minutes)
        else { return nil }
        let reset = JsonRead.int64(e["resets_at"]).flatMap { $0 >= 0 ? $0 : nil } ?? -1
        return RolloutWindow(
            minutes: minutes, pct: Int((min(max(pct, 0), 100) + 0.5).rounded(.down)), resetsAtSec: reset)
    }

    /// Calls `onLine` for every complete line, with its newline removed. A line over 1 MiB is skipped without being
    /// held.
    private static func forEachLine(_ source: ByteSource, dropFirst: Bool, _ onLine: (Data) -> Void) throws {
        var line = Data()
        var skipping = dropFirst
        while true {
            let buffer = try source.read(upTo: chunk)
            if buffer.isEmpty { break }
            var at = buffer.startIndex
            while at < buffer.endIndex {
                let newline = buffer[at...].firstIndex(of: UInt8(ascii: "\n"))
                let end = newline ?? buffer.endIndex
                if !skipping {
                    if line.count + (end - at) > maxLine {
                        skipping = true
                        line.removeAll(keepingCapacity: true)
                    } else {
                        line.append(buffer[at..<end])
                    }
                }
                guard let newline else { break }
                if !skipping && !line.isEmpty {
                    onLine(line)
                }
                line.removeAll(keepingCapacity: true)
                skipping = false
                at = newline + 1
            }
        }
        // The last line has no newline yet when Codex is mid-write; it is usually cut short and then does not parse.
        if !skipping && !line.isEmpty {
            onLine(line)
        }
    }

    private static func has(_ line: Data, _ word: String) -> Bool {
        line.range(of: Data(word.utf8)) != nil
    }

    /// The `type` and `payload` of a line. For an event_msg the type is the payload's own type.
    private static func payloadOf(_ root: [String: Any]) -> (type: String, payload: [String: Any])? {
        guard let outer = root["type"] as? String, let payload = root["payload"] as? [String: Any] else { return nil }
        if outer != "event_msg" { return (outer, payload) }
        guard let inner = payload["type"] as? String else { return nil }
        return (inner, payload)
    }

    private static func tokenCount(_ parent: [String: Any], _ name: String) -> Int64 {
        let n = JsonRead.int64(parent[name]) ?? 0
        return n > 0 ? n : 0
    }

    private static func short(_ text: String) -> String? {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        return text.utf16.count > 100 ? String(String.UnicodeScalarView(text.unicodeScalars.prefix(100))) : text
    }

    private final class TailScan {
        private var state: String?
        private var cwd = ""
        private var version = ""
        private var context = -1
        private var preferred: [RolloutWindow]?
        private var other: [RolloutWindow]?

        func take(_ line: Data) {
            guard CodexRollout.has(line, "event_msg") || CodexRollout.has(line, "session_meta") else { return }
            guard let root = JsonRead.object(line, maxDepth: CodexRollout.maxDepth),
                let found = CodexRollout.payloadOf(root)
            else { return }
            let payload = found.payload
            switch found.type {
            case "task_started":
                state = SessionStateToken.busy.rawValue
            case "task_complete", "turn_aborted":
                state = SessionStateToken.idle.rawValue
            case "session_meta":
                cwd = payload["cwd"] as? String ?? cwd
                version = payload["cli_version"] as? String ?? version
            case "token_count":
                takeLimits(payload)
                takeContext(payload)
            default:
                break
            }
        }

        func result() -> RolloutTail {
            RolloutTail(
                state: state, limits: preferred ?? other ?? [], cwd: cwd.utf16.count > 4096 ? "" : cwd,
                cliVersion: version.utf16.count > 64 ? "" : version, contextPct: context)
        }

        // A token_count with no info (Codex sends one right after the limits change) leaves the last fill as it was.
        private func takeContext(_ payload: [String: Any]) {
            guard let info = payload["info"] as? [String: Any], let last = info["last_token_usage"] as? [String: Any],
                let size = JsonRead.int64(info["model_context_window"]), size > 0
            else { return }
            let used = CodexRollout.tokenCount(last, "total_tokens")
            let (product, overflow) = used.multipliedReportingOverflow(by: 100)
            context = overflow ? 100 : Int(min(100, product / size))
        }

        private func takeLimits(_ payload: [String: Any]) {
            guard let limits = payload["rate_limits"] as? [String: Any],
                let first = CodexRollout.windowOf(limits["primary"])
            else { return }
            var windows = [first]
            if let second = CodexRollout.windowOf(limits["secondary"]), second.minutes != first.minutes {
                windows.append(second)
            }
            if limits["limit_id"] as? String == "codex" {
                preferred = windows
            } else {
                other = windows
            }
        }
    }
}
