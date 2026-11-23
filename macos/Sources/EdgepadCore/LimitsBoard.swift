import Foundation

/// One window as an agent reported it. `window` is Claude's `kind` or, for Codex, decimal minutes. `resetsAt` is an ISO
/// date or epoch seconds, or nil.
public struct RawLimit: Equatable {
    public let window: String
    public let pct: Double
    public let resetsAt: String?

    public init(_ window: String, _ pct: Double, _ resetsAt: String?) {
        self.window = window
        self.pct = pct
        self.resetsAt = resetsAt
    }
}

/// The last-known usage limits of each agent, for TEXT LIMITS. Claude's `five_hour` is 300 minutes and `seven_day`
/// 10080; every other kind is kept under `~kind` (never dropped, only an empty one). The context window is `ctx`. A
/// window an agent stops reporting stays for 24 hours as last measured, then goes. `changed` is called outside the lock
/// when a percentage, a reset time or the set of windows changed; a fresher measurement of the same numbers is stored
/// but not announced. The twin of the laptop core's LimitsBoard.
public final class LimitsBoard {
    public static let keepAbsent: TimeInterval = 24 * 60 * 60

    private let store: UsageStore
    private let changedGate = NSLock()
    private var onChanged: (() -> Void)?

    public init(store: UsageStore) {
        self.store = store
    }

    public var changed: (() -> Void)? {
        get {
            changedGate.lock()
            defer { changedGate.unlock() }
            return onChanged
        }
        set {
            changedGate.lock()
            defer { changedGate.unlock() }
            onChanged = newValue
        }
    }

    /// The wire window for what an agent called it, or nil when it cannot be one.
    public static func windowFor(_ agent: String, _ window: String) -> String? {
        var name = window
        if agent == AgentToken.claude.rawValue {
            switch window {
            case "five_hour": name = "300"
            case "seven_day": name = "10080"
            default: name = LimitWindow.namedPrefix + window
            }
        }
        guard DashWire.isAgent(agent) else { return nil }
        return DashWire.window(name)
    }

    public func update(_ agent: String, _ windows: [RawLimit], contextPct: Double?, now: Date) {
        guard DashWire.isAgent(agent) else { return }
        let nowMin = UsageStore.minutes(now)
        var incoming: [String: LimitRow] = [:]
        for raw in windows {
            if let window = Self.windowFor(agent, raw.window), let pct = Self.round(raw.pct) {
                let reset = Self.resetMinutes(raw.resetsAt)
                incoming[window] = LimitRow(pct: pct, resetAtMin: reset, measuredAtMin: nowMin)
            }
        }
        if let context = contextPct, let rounded = Self.round(context) {
            incoming[LimitWindow.context] = LimitRow(pct: rounded, resetAtMin: -1, measuredAtMin: nowMin)
        }

        var changed = false
        store.gate.lock()
        for (window, row) in incoming {
            let key = LimitKey(agent: agent, window: window)
            if let old = store.limits[key] {
                changed = changed || old.pct != row.pct || old.resetAtMin != row.resetAtMin
            } else {
                changed = true
            }
            store.limits[key] = row
        }
        let cutoff = nowMin - Int64(Self.keepAbsent / 60)
        for (key, row) in store.limits
        where key.agent == agent && incoming[key.window] == nil && row.measuredAtMin < cutoff {
            store.limits[key] = nil
            changed = true
        }
        if changed {
            store.save()
        }
        store.gate.unlock()
        if changed {
            self.changed?()
        }
    }

    /// Claude first, then Codex; within an agent the shortest window first, then named limits, then the context.
    public func snapshot() -> [LimitEntry] {
        store.gate.lock()
        defer { store.gate.unlock() }
        let claude = AgentToken.claude.rawValue
        return store.limits.sorted { a, b in
            let agentA = a.key.agent == claude ? 0 : 1
            let agentB = b.key.agent == claude ? 0 : 1
            if agentA != agentB { return agentA < agentB }
            let rankA = Self.rank(a.key.window)
            let rankB = Self.rank(b.key.window)
            if rankA != rankB { return rankA < rankB }
            return a.key.window < b.key.window
        }
        .map { key, row in
            LimitEntry(
                agent: key.agent, window: key.window, pct: Double(row.pct), resetAtMin: row.resetAtMin,
                measuredAtMin: row.measuredAtMin)
        }
    }

    // Minutes in numeric order (the string is padded in the sort), then named limits, then the context.
    private static func rank(_ window: String) -> String {
        if window == LimitWindow.context { return "3" }
        if window.hasPrefix(LimitWindow.namedPrefix) { return "2" }
        return "1" + String(repeating: "0", count: max(0, 6 - window.count)) + window
    }

    private static func round(_ pct: Double) -> Int64? {
        pct.isNaN ? nil : Int64((min(max(pct, 0), 100) + 0.5).rounded(.down))
    }

    static func resetMinutes(_ text: String?) -> Int64 {
        let value = (text ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { return -1 }
        if (1...15).contains(value.utf8.count), value.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }),
            let number = Int64(value)
        {
            return value.utf8.count >= 13 ? number / 60_000 : number / 60
        }
        guard let date = isoDate(value) else { return -1 }
        return max(-1, UsageStore.minutes(date))
    }

    private static func isoDate(_ text: String) -> Date? {
        let plain = ISO8601DateFormatter()
        if let date = plain.date(from: text) { return date }
        let fractional = ISO8601DateFormatter()
        fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return fractional.date(from: text)
    }
}
