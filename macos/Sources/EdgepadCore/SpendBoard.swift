import Foundation

/// The last-known token and estimated-cent totals of each agent, for TEXT SPEND. A total is clamped to 0 and the caps
/// below, so a wrong number cannot break the wire format. `changed` is called outside the lock when a total changed.
/// The twin of the laptop core's SpendBoard.
public final class SpendBoard {
    public static let maxTokens: Int64 = 10_000_000_000_000
    public static let maxCents: Int64 = 10_000_000_000

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

    public func update(_ agent: String, _ totals: SpendTotals, now: Date) {
        guard DashWire.isAgent(agent) else { return }
        func tokens(_ v: Int64) -> Int64 { min(max(v, 0), Self.maxTokens) }
        func cents(_ v: Int64) -> Int64 { min(max(v, 0), Self.maxCents) }
        let clamped = SpendTotals(
            tokToday: tokens(totals.tokToday), centsToday: cents(totals.centsToday), tokWeek: tokens(totals.tokWeek),
            centsWeek: cents(totals.centsWeek), tokMonth: tokens(totals.tokMonth), centsMonth: cents(totals.centsMonth),
            unpriced: totals.unpriced)
        store.gate.lock()
        let changed = store.spend[agent]?.totals != clamped
        store.spend[agent] = SpendRow(measuredAtMin: UsageStore.minutes(now), totals: clamped)
        if changed {
            store.save()
        }
        store.gate.unlock()
        if changed {
            self.changed?()
        }
    }

    /// Claude first, then Codex.
    public func snapshot() -> [SpendEntry] {
        store.gate.lock()
        defer { store.gate.unlock() }
        let claude = AgentToken.claude.rawValue
        return store.spend.sorted { ($0.key == claude ? 0 : 1) < ($1.key == claude ? 0 : 1) }.map { agent, row in
            let t = row.totals
            return SpendEntry(
                agent: agent, measuredAtMin: row.measuredAtMin, tokToday: t.tokToday, centsToday: t.centsToday,
                tokWeek: t.tokWeek, centsWeek: t.centsWeek, tokMonth: t.tokMonth, centsMonth: t.centsMonth,
                unpriced: t.unpriced)
        }
    }
}
