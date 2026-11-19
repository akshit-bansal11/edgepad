import Foundation

/// The last-known value of one limit window. Pct is rounded 0..100; the times are UTC minutes since the Unix epoch, -1
/// unknown.
public struct LimitRow: Equatable {
    public let pct: Int64
    public let resetAtMin: Int64
    public let measuredAtMin: Int64
}

/// Token and cent totals of one agent. Tokens are uncached input plus output; cents are estimates at API list prices.
public struct SpendTotals: Equatable {
    public let tokToday: Int64
    public let centsToday: Int64
    public let tokWeek: Int64
    public let centsWeek: Int64
    public let tokMonth: Int64
    public let centsMonth: Int64

    public init(
        tokToday: Int64, centsToday: Int64, tokWeek: Int64, centsWeek: Int64, tokMonth: Int64, centsMonth: Int64
    ) {
        self.tokToday = tokToday
        self.centsToday = centsToday
        self.tokWeek = tokWeek
        self.centsWeek = centsWeek
        self.tokMonth = tokMonth
        self.centsMonth = centsMonth
    }

    var all: [Int64] {
        [tokToday, centsToday, tokWeek, centsWeek, tokMonth, centsMonth]
    }
}

/// The last-known spend of one agent and when it was measured.
public struct SpendRow: Equatable {
    public let measuredAtMin: Int64
    public let totals: SpendTotals
}

/// One limit window of one agent: the store's key.
struct LimitKey: Hashable {
    let agent: String
    let window: String
}

/// What the limits and spend boards remember between runs, in `dashboard-usage.json`: window names, percentages, token
/// and cent totals and minute stamps. No prompt, path, command, name or secret ever goes in it. A file that is missing,
/// damaged or from another version is an empty store; an entry older than 30 days is dropped on load. The boards share
/// one store, so one file and one lock (`gate`) cover limits, spend and last-heard. The twin of the core's UsageStore.
public final class UsageStore {
    public static let fileName = "dashboard-usage.json"
    static let maxAge: TimeInterval = 30 * 24 * 60 * 60

    private static let version = 1
    private let path: String
    private var warned = false

    /// Held by a board for as long as it reads or changes the maps below.
    let gate = NSRecursiveLock()
    var limits: [LimitKey: LimitRow] = [:]
    var spend: [String: SpendRow] = [:]
    var heard: [String: Int64] = [:]

    public init(path: String, now: Date) {
        self.path = path
        load(nowMin: Self.minutes(now))
    }

    /// UTC minutes since the Unix epoch.
    public static func minutes(_ time: Date) -> Int64 {
        Int64(time.timeIntervalSince1970.rounded(.down)) / 60
    }

    /// Stamps a hook message from `agent`. Written to disk only when the minute moved.
    public func touch(_ agent: String, now: Date) {
        guard DashWire.isAgent(agent) else { return }
        gate.lock()
        defer { gate.unlock() }
        let minute = Self.minutes(now)
        if heard[agent] == minute { return }
        heard[agent] = minute
        save()
    }

    /// UTC minutes of the last accepted hook message from the agent, or -1.
    public func lastHeard(_ agent: String) -> Int64 {
        gate.lock()
        defer { gate.unlock() }
        return heard[agent] ?? -1
    }

    /// Writes the maps privately. False (logged once) when the disk refuses; memory is unaffected.
    @discardableResult
    public func save() -> Bool {
        gate.lock()
        defer { gate.unlock() }
        let limitItems: [[String: Any]] = limits.map { key, row in
            ["a": key.agent, "w": key.window, "p": row.pct, "r": row.resetAtMin, "m": row.measuredAtMin]
        }
        let spendItems: [[String: Any]] = spend.map { agent, row in
            ["a": agent, "m": row.measuredAtMin, "t": row.totals.all]
        }
        let root: [String: Any] = ["v": Self.version, "limits": limitItems, "spend": spendItems, "heard": heard]
        guard
            let data = try? JSONSerialization.data(
                withJSONObject: root, options: [.sortedKeys, .withoutEscapingSlashes]),
            writePrivately(String(decoding: data, as: UTF8.self), to: path) == nil
        else {
            if !warned {
                warned = true
                Log.write("dashboard usage not saved")
            }
            return false
        }
        return true
    }

    private func load(nowMin: Int64) {
        let oldest = nowMin - Int64(Self.maxAge / 60)
        guard FileManager.default.fileExists(atPath: path) else { return }
        guard let data = try? Data(contentsOf: URL(fileURLWithPath: path)),
            let root = JsonRead.object(data, maxDepth: 8),
            JsonRead.int64(root["v"]) == Int64(Self.version)
        else { return }

        for item in Self.items(root, "limits") {
            guard let agent = JsonRead.string(item["a"]), DashWire.isAgent(agent),
                let window = JsonRead.string(item["w"]), DashWire.window(window) == window,
                let pct = JsonRead.int64(item["p"]), let reset = JsonRead.int64(item["r"]),
                let measured = JsonRead.int64(item["m"]), measured >= oldest
            else { continue }
            limits[LimitKey(agent: agent, window: window)] = LimitRow(
                pct: min(max(pct, 0), 100), resetAtMin: reset, measuredAtMin: measured)
        }
        for item in Self.items(root, "spend") {
            guard let agent = JsonRead.string(item["a"]), DashWire.isAgent(agent),
                let measured = JsonRead.int64(item["m"]), measured >= oldest,
                let t = item["t"] as? [Any], t.count == 6
            else { continue }
            let n = t.map { JsonRead.int64($0) ?? -1 }
            guard n.allSatisfy({ $0 >= 0 }) else { continue }
            spend[agent] = SpendRow(
                measuredAtMin: measured,
                totals: SpendTotals(
                    tokToday: n[0], centsToday: n[1], tokWeek: n[2], centsWeek: n[3], tokMonth: n[4], centsMonth: n[5]))
        }
        if let heardItems = root["heard"] as? [String: Any] {
            for (agent, value) in heardItems where DashWire.isAgent(agent) {
                if let minute = JsonRead.int64(value), minute >= oldest {
                    heard[agent] = minute
                }
            }
        }
    }

    private static func items(_ root: [String: Any], _ name: String) -> [[String: Any]] {
        (root[name] as? [Any] ?? []).compactMap { $0 as? [String: Any] }
    }
}
