import Foundation

/// Anthropic API list prices in US dollars per million tokens, as of 2026-10-20, from plugins/edgepad/scripts/prices.mjs
/// (adapted from Plimsoll by Akshit Bansal, MIT). Used to ESTIMATE cost from transcript tokens: a subscription is not
/// billed this way. Update both tables together when prices change.
public enum ClaudePrices {
    struct Row {
        let prefix: String
        let input: Double
        let output: Double
        /// The cache-read price as a share of the input price.
        let read: Double
    }

    // Order matters: a longer prefix before a shorter one it starts with; the first match wins.
    static let rows = [
        Row(prefix: "claude-fable-5-1", input: 10, output: 50, read: 0.025),
        Row(prefix: "claude-mythos-5-1", input: 10, output: 50, read: 0.025),
        Row(prefix: "claude-fable-5", input: 10, output: 50, read: 0.1),
        Row(prefix: "claude-mythos-5", input: 10, output: 50, read: 0.1),
        Row(prefix: "claude-opus-5-5", input: 4, output: 20, read: 0.05),
        Row(prefix: "claude-opus-5", input: 5, output: 25, read: 0.1),
        Row(prefix: "claude-opus-4-8", input: 5, output: 25, read: 0.1),
        Row(prefix: "claude-opus-4-7", input: 5, output: 25, read: 0.1),
        Row(prefix: "claude-opus-4-6", input: 5, output: 25, read: 0.1),
        Row(prefix: "claude-opus-4-5", input: 5, output: 25, read: 0.1),
        Row(prefix: "claude-opus-4-1", input: 15, output: 75, read: 0.1),
        Row(prefix: "claude-opus-4", input: 15, output: 75, read: 0.1),
        Row(prefix: "claude-sonnet-5-5", input: 2, output: 10, read: 0.05),
        Row(prefix: "claude-sonnet-5", input: 2, output: 10, read: 0.1),
        Row(prefix: "claude-sonnet-4-6", input: 3, output: 15, read: 0.1),
        Row(prefix: "claude-sonnet-4-5", input: 3, output: 15, read: 0.1),
        Row(prefix: "claude-sonnet-4", input: 3, output: 15, read: 0.1),
        Row(prefix: "claude-haiku-5-5", input: 0.1, output: 0.5, read: 0.1),
        Row(prefix: "claude-haiku-4-5", input: 1, output: 5, read: 0.1),
        Row(prefix: "claude-3-5-haiku", input: 0.8, output: 4, read: 0.1),
    ]
    static let write5m = 1.25
    static let write1h = 2.0
    // Claude Haiku 5.5 charges 5x every price for a request whose prompt (input plus cache reads and writes) is over
    // 100,000 tokens.
    static let longPrefix = "claude-haiku-5-5"
    static let longOver = 100_000.0
    static let longTimes = 5.0

    /// False for a model the table does not know: its tokens count, its dollars do not.
    public static func isPriced(_ model: String?) -> Bool {
        row(key(model)) != nil
    }

    /// The estimated dollars of one response's `usage`; 0 for an unknown model rather than a guessed price.
    public static func cost(_ model: String?, _ usage: [String: Any]) -> Double {
        let id = key(model)
        guard let row = row(id) else { return 0 }
        func value(_ name: String) -> Double { JsonRead.double(usage[name]) ?? 0 }
        // cache_creation splits the write by lifetime when present; without it every written token is 5-minute.
        let hour = JsonRead.double((usage["cache_creation"] as? [String: Any])?["ephemeral_1h_input_tokens"]) ?? 0
        let minutes = max(0, value("cache_creation_input_tokens") - hour)
        let usd =
            (value("input_tokens") * row.input + value("output_tokens") * row.output
                + value("cache_read_input_tokens") * row.input * row.read + minutes * row.input * write5m
                + hour * row.input * write1h) / 1_000_000
        let prompt = value("input_tokens") + value("cache_read_input_tokens") + value("cache_creation_input_tokens")
        return id.hasPrefix(longPrefix) && prompt > longOver ? usd * longTimes : usd
    }

    // Bedrock (us.anthropic.claude-opus-4-1-...) and Vertex (claude-sonnet-4-5@...) ids carry a prefix.
    private static func key(_ model: String?) -> String {
        guard let model else { return "" }
        guard let at = model.range(of: "claude-"), at.lowerBound > model.startIndex else { return model }
        return String(model[at.lowerBound...])
    }

    private static func row(_ key: String) -> Row? {
        key.isEmpty ? nil : rows.first { key.hasPrefix($0.prefix) }
    }
}

/// Claude Code's spend for the dashboard, read from its own transcripts (`<config>/projects/**/*.jsonl`, the Plimsoll
/// approach of plugins/edgepad/scripts/scan-claude.mjs, so no Node is needed): tokens and estimated cents for today,
/// this week (from Monday) and this month, in local time. Tokens are input + output; cache reads and writes are priced
/// but not counted as tokens. A response is written once per content block under one message id, its usage final on
/// the last, so the last row of each id is kept, and an id is counted once across files.
///
/// It reads only while the Claude Code integration is on, a phone is linked AND the phone draws the AI widget or screen
/// (DASH_WATCH bit 3): then once a minute, with each file's sums cached by size and modification time, so an unchanged
/// month costs stats only. A tick (every 5 s) parses at most `bytesPerTick` of changed files (at least one file); the
/// totals reach the board only after a scan that had every file in hand, so a first scan of a large folder spreads over
/// ticks instead of sending a partial sum. Nothing from a transcript leaves this class but numbers; the cache holds
/// message ids, days and numbers. The Mac twin of the core's ClaudeSpendWatcher.
public final class ClaudeSpend {
    public static let aiBit = CodexWatcher.aiBit
    public static let tick: TimeInterval = 5
    public static let every: TimeInterval = 60
    public static let saveEvery: TimeInterval = 5 * 60
    public static let bytesPerTick: Int64 = 32 * 1024 * 1024
    public static let cacheName = "dashboard-claude-cache.json"
    /// A file last written longer ago than this holds nothing for this week or month.
    static let windowDays = 40

    private static let maxLine = 4 * 1024 * 1024
    private static let cacheVersion = 1
    private static let usageMark = Data("\"usage\"".utf8)

    /// One response: its local day (yyyy-MM-dd), tokens, estimated dollars, and whether its model has a price.
    struct Row: Equatable {
        let day: String
        let tokens: Double
        let usd: Double
        let priced: Bool
    }

    private struct CacheEntry {
        let size: Int64
        let stamp: Int64
        let from: String
        let rows: [String: Row]
    }

    private let settingsOn: () -> Bool
    private let phoneLinked: () -> Bool
    private let spend: SpendBoard
    private let clock: () -> Date
    private let projects: String
    private let cachePath: String
    private let startTimer: (@escaping () -> Void, TimeInterval) -> Stoppable
    private let gate = NSLock()
    private let pollGate = NSLock()
    private var cache: [String: CacheEntry] = [:]
    private var cacheLoaded = false
    // Saved at most every 5 minutes while scanning, and once more when the scanning stops.
    private var cacheDirty = false
    private var lastSaved: Date?
    private var timer: Stoppable?
    private var watch = 0
    private var unpriced = false
    // When the last scan that had every file in hand finished.
    private var lastDone: Date?

    /// - Parameters:
    ///   - root: Claude Code's folder, `defaultRoot()` in the app.
    ///   - cachePath: Where the per-file sums are kept.
    ///   - startTimer: Runs the action at once and then every interval on another queue until stopped.
    public init(
        settingsOn: @escaping () -> Bool,
        phoneLinked: @escaping () -> Bool,
        spend: SpendBoard,
        clock: @escaping () -> Date = Date.init,
        root: String,
        cachePath: String,
        startTimer: ((@escaping () -> Void, TimeInterval) -> Stoppable)? = nil
    ) {
        self.settingsOn = settingsOn
        self.phoneLinked = phoneLinked
        self.spend = spend
        self.clock = clock
        projects = (root as NSString).appendingPathComponent("projects")
        self.cachePath = cachePath
        let queue = DispatchQueue(label: "edgepad.claude", qos: .utility)
        self.startTimer = startTimer ?? { action, every in Stoppable.repeating(every: every, on: queue, action) }
    }

    /// `CLAUDE_CONFIG_DIR`, else `.claude` in the user's home.
    public static func defaultRoot() -> String {
        if let folder = ProcessInfo.processInfo.environment["CLAUDE_CONFIG_DIR"], !folder.isEmpty {
            return folder
        }
        return (NSHomeDirectory() as NSString).appendingPathComponent(".claude")
    }

    /// True while the timer exists.
    public var running: Bool {
        gate.lock()
        defer { gate.unlock() }
        return timer != nil
    }

    /// True when the last scan met a model with no price, so its cents are a lower bound.
    public var hasUnpriced: Bool {
        gate.lock()
        defer { gate.unlock() }
        return unpriced
    }

    /// Starts or stops the timer to match "on, a phone linked and bit 3 watched". Call it whenever one changes.
    public func evaluate() {
        gate.lock()
        defer { gate.unlock() }
        let wanted = watch & Self.aiBit != 0 && settingsOn() && phoneLinked()
        if wanted && timer == nil {
            // A watch gained afresh scans on the first tick.
            lastDone = nil
            timer = startTimer({ [weak self] in self?.poll() }, Self.tick)
        } else if !wanted, let running = timer {
            running.stop()
            timer = nil
            flushCacheLater()
        }
    }

    /// The phone's DASH_WATCH.
    public func setWatch(_ mask: Int) {
        gate.lock()
        watch = mask
        gate.unlock()
        evaluate()
    }

    public func dispose() {
        gate.lock()
        defer { gate.unlock() }
        let had = timer != nil
        timer?.stop()
        timer = nil
        if had {
            flushCacheLater()
        }
    }

    /// One tick: a scan when the last whole one is a minute old (or none was), its totals sent to the board once it had
    /// every file in hand. Internal to the timer and to tests; never overlaps itself.
    func poll(budget: Int64 = ClaudeSpend.bytesPerTick) {
        guard pollGate.try() else { return }
        defer { pollGate.unlock() }
        guard settingsOn(), phoneLinked() else { return }
        let now = clock()
        gate.lock()
        let due = lastDone.map { now.timeIntervalSince($0) >= Self.every || now < $0 } ?? true
        gate.unlock()
        guard due else { return }
        let found = scan(budget: budget)
        guard found.complete else { return }
        gate.lock()
        unpriced = found.unpriced
        lastDone = now
        gate.unlock()
        spend.update(AgentToken.claude.rawValue, found.totals, now: now)
    }

    /// The first day of each period in local time, and `from`, the earliest of them (a week can start last month).
    static func periods(_ now: Date, _ calendar: Calendar = .current) -> (
        today: String, weekStart: String, monthStart: String, from: String
    ) {
        let today = calendar.startOfDay(for: now)
        let parts = calendar.dateComponents([.year, .month, .weekday], from: today)
        let monday = calendar.date(byAdding: .day, value: -(((parts.weekday ?? 2) + 5) % 7), to: today) ?? today
        let first = calendar.date(from: DateComponents(year: parts.year, month: parts.month, day: 1)) ?? today
        let week = dayOf(monday, calendar)
        let month = dayOf(first, calendar)
        return (dayOf(today, calendar), week, month, min(week, month))
    }

    static func dayOf(_ date: Date, _ calendar: Calendar = .current) -> String {
        let parts = calendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04ld-%02ld-%02ld", parts.year ?? 0, parts.month ?? 0, parts.day ?? 0)
    }

    /// The totals for the transcripts under `projects`, with the cache read and saved around it. At most `budget` bytes
    /// of changed files are parsed (at least one file); `complete` is false when a changed file was left for the next
    /// tick, and the totals are then partial.
    func scan(budget: Int64 = ClaudeSpend.bytesPerTick) -> (totals: SpendTotals, unpriced: Bool, complete: Bool) {
        let now = clock()
        let (today, weekStart, monthStart, from) = Self.periods(now)
        let calendar = Calendar.current
        let oldest = calendar.date(byAdding: .day, value: -Self.windowDays, to: calendar.startOfDay(for: now)) ?? now
        if !cacheLoaded {
            loadCache()
            cacheLoaded = true
        }

        var kept: [String: CacheEntry] = [:]
        var byId: [String: Row] = [:]
        var left = budget
        var complete = true
        for file in transcripts() where file.mtime >= oldest {
            let stamp = Int64((file.mtime.timeIntervalSince1970 * 1000).rounded(.down))
            let rows: [String: Row]
            if let hit = cache[file.key], hit.size == file.size, hit.stamp == stamp, hit.from <= from {
                rows = hit.rows
            } else if left <= 0 {
                complete = false
                continue
            } else if let summed = Self.sumFile(file.path, from: from) {
                left -= file.size
                rows = summed
            } else {
                continue  // removed or unreadable mid-scan
            }
            kept[file.key] = CacheEntry(size: file.size, stamp: stamp, from: from, rows: rows)
            byId.merge(rows) { _, new in new }
        }
        if !complete {
            // A file left for later keeps what it had; only a whole scan drops the files that went.
            kept.merge(cache) { new, _ in new }
        }
        let changed = kept.count != cache.count || kept.contains { key, entry in
            guard let old = cache[key] else { return true }
            return old.size != entry.size || old.stamp != entry.stamp || old.from != entry.from
        }
        cache = kept
        if changed {
            cacheDirty = true
        }
        if cacheDirty, lastSaved.map({ now.timeIntervalSince($0) >= Self.saveEvery || now < $0 }) ?? true {
            flushCache(now)
        }

        var sums = (today: (0.0, 0.0), week: (0.0, 0.0), month: (0.0, 0.0))
        var anyUnpriced = false
        for row in byId.values where row.day >= from {
            if row.day == today {
                sums.today = (sums.today.0 + row.tokens, sums.today.1 + row.usd)
            }
            if row.day >= weekStart {
                sums.week = (sums.week.0 + row.tokens, sums.week.1 + row.usd)
            }
            if row.day >= monthStart {
                sums.month = (sums.month.0 + row.tokens, sums.month.1 + row.usd)
            }
            if !row.priced && row.tokens > 0 {
                anyUnpriced = true
            }
        }
        func tok(_ sum: (Double, Double)) -> Int64 { Int64(sum.0.rounded()) }
        func cents(_ sum: (Double, Double)) -> Int64 { Int64((sum.1 * 100).rounded()) }
        let totals = SpendTotals(
            tokToday: tok(sums.today), centsToday: cents(sums.today), tokWeek: tok(sums.week),
            centsWeek: cents(sums.week), tokMonth: tok(sums.month), centsMonth: cents(sums.month))
        return (totals, anyUnpriced, complete)
    }

    /// One transcript's responses on or after `from`, by message id; nil when it cannot be read.
    static func sumFile(_ path: String, from: String) -> [String: Row]? {
        let precise = ISO8601DateFormatter()
        precise.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let plain = ISO8601DateFormatter()
        var rows: [String: Row] = [:]
        let read = forEachLine(path) { line in
            guard line.range(of: usageMark) != nil, let row = JsonRead.object(line, maxDepth: 64),
                row["type"] as? String == "assistant",
                let message = row["message"] as? [String: Any],
                let usage = message["usage"] as? [String: Any]
            else { return }
            let model = message["model"] as? String
            guard model != "<synthetic>", let stamp = row["timestamp"] as? String,
                let at = precise.date(from: stamp) ?? plain.date(from: stamp)
            else { return }
            let day = dayOf(at)
            guard day >= from, let id = (message["id"] as? String) ?? (row["uuid"] as? String) else { return }
            let input = positive(JsonRead.double(usage["input_tokens"]))
            let tokens = input + positive(JsonRead.double(usage["output_tokens"]))
            rows[id] = Row(
                day: day, tokens: tokens, usd: positive(ClaudePrices.cost(model, usage)),
                priced: ClaudePrices.isPriced(model))
        }
        return read ? rows : nil
    }

    // A hostile or odd number (a string, a negative, NaN) counts as nothing.
    private static func positive(_ value: Double?) -> Double {
        guard let value, value.isFinite, value > 0 else { return 0 }
        return value
    }

    // Calls `body` with every line of the file, a line longer than 4 MiB skipped. False when it could not be read.
    private static func forEachLine(_ path: String, _ body: (Data) -> Void) -> Bool {
        guard let handle = try? FileHandle(forReadingFrom: URL(fileURLWithPath: path)) else { return false }
        defer { try? handle.close() }
        var pending = Data()
        var skipping = false
        while true {
            guard let chunk = try? handle.read(upToCount: 1 << 20) else { return false }
            if chunk.isEmpty { break }
            var start = chunk.startIndex
            while let end = chunk[start...].firstIndex(of: UInt8(ascii: "\n")) {
                if !skipping {
                    pending.append(chunk[start..<end])
                    if pending.count <= maxLine {
                        body(pending)
                    }
                }
                pending.removeAll(keepingCapacity: true)
                skipping = false
                start = chunk.index(after: end)
            }
            if !skipping {
                pending.append(chunk[start...])
                if pending.count > maxLine {
                    skipping = true
                    pending.removeAll()
                }
            }
        }
        if !skipping && !pending.isEmpty {
            body(pending)
        }
        return true
    }

    // The regular `.jsonl` files under `projects`, links never followed.
    private func transcripts() -> [(path: String, key: String, size: Int64, mtime: Date)] {
        let manager = FileManager.default
        guard let walker = manager.enumerator(atPath: projects) else { return [] }
        var found: [(path: String, key: String, size: Int64, mtime: Date)] = []
        while let relative = walker.nextObject() as? String {
            let path = (projects as NSString).appendingPathComponent(relative)
            guard let attributes = try? manager.attributesOfItem(atPath: path) else { continue }
            let type = attributes[.type] as? FileAttributeType
            if type == .typeSymbolicLink {
                walker.skipDescendants()
                continue
            }
            guard type == .typeRegular, relative.hasSuffix(".jsonl"),
                let size = (attributes[.size] as? NSNumber)?.int64Value,
                let mtime = attributes[.modificationDate] as? Date
            else { continue }
            found.append((path, relative, size, mtime))
        }
        return found
    }

    // ---- the cache

    private func loadCache() {
        guard FileManager.default.fileExists(atPath: cachePath),
            let data = try? Data(contentsOf: URL(fileURLWithPath: cachePath)),
            let root = JsonRead.object(data, maxDepth: 8),
            JsonRead.int64(root["v"]) == Int64(Self.cacheVersion),
            let files = root["files"] as? [Any]
        else { return }
        for case let f as [String: Any] in files {
            guard let key = f["p"] as? String, (1...1024).contains(key.utf16.count),
                let size = JsonRead.int64(f["s"]), let stamp = JsonRead.int64(f["t"]),
                let from = f["f"] as? String, let list = f["r"] as? [Any]
            else { continue }
            var rows: [String: Row] = [:]
            for case let r as [Any] in list where r.count == 5 {
                guard let id = r[0] as? String, let day = r[1] as? String, let tokens = JsonRead.double(r[2]),
                    let usd = JsonRead.double(r[3]), let priced = JsonRead.int64(r[4])
                else { continue }
                rows[id] = Row(day: day, tokens: tokens, usd: usd, priced: priced == 1)
            }
            cache[key] = CacheEntry(size: size, stamp: stamp, from: from, rows: rows)
        }
    }

    /// Saves the cache if it changed since the last save. Waits for a scan in progress, so it never runs beside one.
    func flushCacheNow() {
        pollGate.lock()
        defer { pollGate.unlock() }
        if cacheDirty {
            flushCache(clock())
        }
    }

    // The scanning stopped: save what is unsaved, off the caller's thread, which may be the main one.
    private func flushCacheLater() {
        DispatchQueue.global(qos: .utility).async { [weak self] in self?.flushCacheNow() }
    }

    private func flushCache(_ now: Date) {
        saveCache()
        cacheDirty = false
        lastSaved = now
    }

    // The cache only saves work: the next scan rebuilds what is missing.
    private func saveCache() {
        let files: [[String: Any]] = cache.map { key, entry in
            let rows: [[Any]] = entry.rows.map { id, row in [id, row.day, row.tokens, row.usd, row.priced ? 1 : 0] }
            let item: [String: Any] = ["p": key, "s": entry.size, "t": entry.stamp, "f": entry.from, "r": rows]
            return item
        }
        let root: [String: Any] = ["v": Self.cacheVersion, "files": files]
        guard let data = try? JSONSerialization.data(withJSONObject: root, options: [.withoutEscapingSlashes]) else {
            return
        }
        _ = writePrivately(String(decoding: data, as: UTF8.self), to: cachePath)
    }
}
