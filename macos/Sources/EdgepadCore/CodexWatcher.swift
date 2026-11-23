import Foundation

/// Reads Codex's own session files for the dashboard: which sessions are working, the usage limits and the spend. It
/// exists (has a timer) only while the Codex integration is on AND a phone is linked; `evaluate` decides that and the
/// app calls it when either changes. The only files ever opened are `sessions/YYYY/MM/DD/rollout-*.jsonl` under the
/// Codex folder, regular files only, never `auth.json`, the databases or the config, and nothing from a rollout leaves
/// this class except a state, percentages, token counts and the last folder name of a working directory.
///
/// Every 5 seconds it looks at the files of today and yesterday that changed in the last 6 hours (at most 8) and
/// tells the `SessionBoard` busy (written in the last 10 minutes, task open) or idle. While the phone draws the AI
/// widget or screen (DASH_WATCH bit 3) it also refreshes the limits and the spend once a minute; spend is cached per
/// file by size and modification time, so an unchanged month costs stats only. The twin of the laptop core's CodexWatcher.
public final class CodexWatcher {
    public static let aiBit = 8
    public static let every: TimeInterval = 5
    public static let fresh: TimeInterval = 10 * 60
    public static let cacheName = "dashboard-codex-cache.json"

    private static let usageEvery = 12
    private static let recentWrite: TimeInterval = 30
    private static let maxFiles = SessionBoard.capacity
    private static let limitFiles = 5
    private static let maxTails = 32
    private static let cacheVersion = 1

    private struct Rollout {
        let path: String
        let name: String
        let size: Int64
        let mtime: Date

        var stamp: Int64 { Int64((mtime.timeIntervalSince1970 * 1000).rounded(.down)) }
    }

    private struct Tracked {
        let size: Int64
        let stamp: Int64
        let state: String
        let id: String
    }

    private struct CacheEntry {
        let size: Int64
        let stamp: Int64
        let totals: RolloutTotals
    }

    private let settingsOn: () -> Bool
    private let phoneLinked: () -> Bool
    private let sessions: SessionBoard
    private let limits: LimitsBoard
    private let spend: SpendBoard
    private let clock: () -> Date
    private let root: String
    private let sessionsDir: String
    private let cachePath: String
    private let open: (String) throws -> ByteSource
    private let startTimer: (@escaping () -> Void, TimeInterval) -> Stoppable
    private let gate = NSLock()
    private let pollGate = NSLock()
    private var tracked: [String: Tracked] = [:]
    private var tails: [String: (size: Int64, stamp: Int64, tail: RolloutTail)] = [:]
    private var cache: [String: CacheEntry] = [:]
    private var timer: Stoppable?
    private var watch = 0
    private var usageIn = 0
    private var usageDue = false
    private var cacheDirty = false
    private var unpriced = false

    /// - Parameters:
    ///   - root: The Codex folder, `defaultRoot()` in the app.
    ///   - cachePath: Where the per-file spend cache lives; numbers and model ids only.
    ///   - open: Opens a file for reading. Only ever called with a rollout path. Tests spy on it.
    ///   - startTimer: Runs the action every interval on another queue until stopped.
    public init(
        settingsOn: @escaping () -> Bool,
        phoneLinked: @escaping () -> Bool,
        sessions: SessionBoard,
        limits: LimitsBoard,
        spend: SpendBoard,
        clock: @escaping () -> Date = Date.init,
        root: String,
        cachePath: String,
        open: ((String) throws -> ByteSource)? = nil,
        startTimer: ((@escaping () -> Void, TimeInterval) -> Stoppable)? = nil
    ) {
        self.settingsOn = settingsOn
        self.phoneLinked = phoneLinked
        self.sessions = sessions
        self.limits = limits
        self.spend = spend
        self.clock = clock
        self.root = root
        sessionsDir = (root as NSString).appendingPathComponent("sessions")
        self.cachePath = cachePath
        self.open = open ?? { try FileByteSource(path: $0) }
        let queue = DispatchQueue(label: "edgepad.codex", qos: .utility)
        self.startTimer = startTimer ?? { action, every in Stoppable.repeating(every: every, on: queue, action) }
        loadCache()
    }

    /// True while the timer exists.
    public var running: Bool {
        gate.lock()
        defer { gate.unlock() }
        return timer != nil
    }

    /// True when the last spend refresh met a model with no price, so its cents are a lower bound.
    public var hasUnpriced: Bool {
        gate.lock()
        defer { gate.unlock() }
        return unpriced
    }

    /// `CODEX_HOME`, else `.codex` in the user's home.
    public static func defaultRoot() -> String {
        if let home = ProcessInfo.processInfo.environment["CODEX_HOME"], !home.isEmpty {
            return home
        }
        return (NSHomeDirectory() as NSString).appendingPathComponent(".codex")
    }

    /// Starts or stops the timer to match "integration on and a phone linked". Call it whenever either changes.
    public func evaluate() {
        gate.lock()
        defer { gate.unlock() }
        let wanted = settingsOn() && phoneLinked()
        if wanted && timer == nil {
            timer = startTimer({ [weak self] in self?.poll() }, Self.every)
        } else if !wanted, let running = timer {
            running.stop()
            timer = nil
        }
    }

    /// The phone's DASH_WATCH. Gaining bit 3 refreshes limits and spend on the next poll.
    public func setWatch(_ mask: Int) {
        gate.lock()
        defer { gate.unlock() }
        if watch & Self.aiBit == 0 && mask & Self.aiBit != 0 {
            usageDue = true
        }
        watch = mask
    }

    public func dispose() {
        gate.lock()
        defer { gate.unlock() }
        timer?.stop()
        timer = nil
    }

    /// One look at the files. Internal to the timer and to tests; never overlaps itself.
    func poll() {
        guard pollGate.try() else { return }
        defer { pollGate.unlock() }
        guard settingsOn(), phoneLinked() else { return }
        scanSessions()
        gate.lock()
        var due = false
        if watch & Self.aiBit != 0 {
            usageIn -= 1
            if usageDue || usageIn <= 0 {
                usageIn = Self.usageEvery
                usageDue = false
                due = true
            }
        }
        gate.unlock()
        if due {
            refreshLimits()
            refreshSpend()
        }
    }

    // ---- sessions

    private func scanSessions() {
        let now = clock()
        let today = Self.calendar.startOfDay(for: now)
        let recent = (rollouts(dayDir(today)) + rollouts(dayDir(Self.addDays(today, -1))))
            .filter { now.timeIntervalSince($0.mtime) <= SessionBoard.expiry }
            .sorted { $0.mtime > $1.mtime }
            .prefix(Self.maxFiles)
        var seen = Set<String>()
        for file in recent {
            seen.insert(file.path)
            let stale = now.timeIntervalSince(file.mtime) > Self.fresh
            if let before = tracked[file.path], before.size == file.size, before.stamp == file.stamp {
                // Untouched for ten minutes: whatever it said last, it is not working now.
                if stale && before.state == SessionStateToken.busy.rawValue {
                    sessions.apply(
                        HookEvent(agent: AgentToken.codex.rawValue, sessionId: before.id, event: .stop, cwd: ""))
                    tracked[file.path] = Tracked(
                        size: before.size, stamp: before.stamp, state: SessionStateToken.idle.rawValue, id: before.id)
                }
                continue
            }
            guard let tail = tailOf(file) else { continue }
            let id = (file.name as NSString).deletingPathExtension
            // No marker in the tail: keep what was known; a file written in the last 30 s is working; otherwise
            // nothing is known, and idle is never assumed.
            var known: String
            if let state = tail.state {
                known = state
            } else if let before = tracked[file.path] {
                known = before.state
            } else if now.timeIntervalSince(file.mtime) <= Self.recentWrite {
                known = SessionStateToken.busy.rawValue
            } else {
                continue
            }
            if stale && known == SessionStateToken.busy.rawValue {
                known = SessionStateToken.idle.rawValue
            }
            let event: SessionEvent = known == SessionStateToken.busy.rawValue ? .prompt : .stop
            // Measured when the file was written, not when it was looked at.
            sessions.apply(
                HookEvent(agent: AgentToken.codex.rawValue, sessionId: id, event: event, cwd: tail.cwd),
                at: file.mtime)
            if tail.contextPct >= 0 {
                sessions.setContextById(agent: AgentToken.codex.rawValue, sessionId: id, pct: Double(tail.contextPct))
            }
            tracked[file.path] = Tracked(size: file.size, stamp: file.stamp, state: known, id: id)
        }
        for (path, old) in tracked where !seen.contains(path) {
            // Untouched for ten minutes: whatever it said last, it is not working now.
            if old.state == SessionStateToken.busy.rawValue {
                sessions.apply(HookEvent(agent: AgentToken.codex.rawValue, sessionId: old.id, event: .stop, cwd: ""))
            }
            tracked[path] = nil
        }
    }

    // ---- limits

    private func refreshLimits() {
        for file in newestFirst(limit: Self.limitFiles) {
            guard let tail = tailOf(file), !tail.limits.isEmpty else { continue }
            let raw = tail.limits.map { w in
                RawLimit(String(w.minutes), Double(w.pct), w.resetsAtSec >= 0 ? String(w.resetsAtSec) : nil)
            }
            limits.update(AgentToken.codex.rawValue, raw, contextPct: nil, now: clock())
            return
        }
    }

    // ---- spend

    private func refreshSpend() {
        let today = Self.calendar.startOfDay(for: clock())
        let start7 = Self.addDays(today, -6)
        let start30 = Self.addDays(today, -29)
        var tokDay: Int64 = 0
        var centsDay: Int64 = 0
        var tokWeek: Int64 = 0
        var centsWeek: Int64 = 0
        var tokMonth: Int64 = 0
        var centsMonth: Int64 = 0
        var anyUnpriced = false
        var live = Set<String>()
        var day = start30
        while day <= today {
            for file in rollouts(dayDir(day)) {
                let totals = totalsOf(file, &live)
                if totals.input == 0 && totals.output == 0 { continue }
                var cents: Int64 = 0
                if let price = OpenAiPrices.price(totals.model) {
                    cents = OpenAiPrices.cents(price, totals)
                } else {
                    anyUnpriced = true
                }
                tokMonth += totals.tokens
                centsMonth += cents
                if day >= start7 {
                    tokWeek += totals.tokens
                    centsWeek += cents
                }
                if day == today {
                    tokDay += totals.tokens
                    centsDay += cents
                }
            }
            day = Self.addDays(day, 1)
        }
        for key in cache.keys where !live.contains(key) {
            cache[key] = nil
            cacheDirty = true
        }
        if cacheDirty {
            saveCache()
        }
        gate.lock()
        unpriced = anyUnpriced
        gate.unlock()
        let codex = AgentToken.codex.rawValue
        if live.isEmpty && !spend.snapshot().contains(where: { $0.agent == codex }) {
            return  // no rollout was ever seen: nothing to show, rather than a row of zeros
        }
        let totals = SpendTotals(
            tokToday: tokDay, centsToday: centsDay, tokWeek: tokWeek, centsWeek: centsWeek, tokMonth: tokMonth,
            centsMonth: centsMonth, unpriced: anyUnpriced)
        spend.update(codex, totals, now: clock())
    }

    private func totalsOf(_ file: Rollout, _ live: inout Set<String>) -> RolloutTotals {
        let key = String(file.path.dropFirst(root.count)).trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        live.insert(key)
        if let hit = cache[key], hit.size == file.size, hit.stamp == file.stamp {
            return hit.totals
        }
        // The model of an earlier read is kept, so a grown file needs only its last 1 MiB.
        guard let source = try? open(file.path),
            let totals = try? CodexRollout.totals(source, knownModel: cache[key]?.totals.model)
        else {
            return .zero
        }
        cache[key] = CacheEntry(size: file.size, stamp: file.stamp, totals: totals)
        cacheDirty = true
        return totals
    }

    // ---- files

    private func tailOf(_ file: Rollout) -> RolloutTail? {
        if let hit = tails[file.path], hit.size == file.size, hit.stamp == file.stamp {
            return hit.tail
        }
        guard let source = try? open(file.path), let tail = try? CodexRollout.parseTail(source) else { return nil }
        if tails.count >= Self.maxTails {
            tails.removeAll()
        }
        tails[file.path] = (file.size, file.stamp, tail)
        return tail
    }

    private static let calendar = Calendar.autoupdatingCurrent

    private static func addDays(_ day: Date, _ days: Int) -> Date {
        calendar.date(byAdding: .day, value: days, to: day) ?? day
    }

    private func dayDir(_ day: Date) -> String {
        let parts = Self.calendar.dateComponents([.year, .month, .day], from: day)
        let name = String(format: "%04ld/%02ld/%02ld", parts.year ?? 0, parts.month ?? 0, parts.day ?? 0)
        return (sessionsDir as NSString).appendingPathComponent(name)
    }

    /// The newest rollouts first, walking the date folders from the newest down, at most `limit`.
    private func newestFirst(limit: Int) -> [Rollout] {
        var found: [Rollout] = []
        for year in Self.numbered(sessionsDir, 4) {
            for month in Self.numbered(year, 2) {
                for day in Self.numbered(month, 2) {
                    for file in rollouts(day).sorted(by: { $0.name > $1.name }) {
                        found.append(file)
                        if found.count >= limit { return found }
                    }
                }
            }
        }
        return found
    }

    /// The all-digit sub-folders of `parent` with a name of `length` characters, newest first, links skipped.
    private static func numbered(_ parent: String, _ length: Int) -> [String] {
        let names = (try? FileManager.default.contentsOfDirectory(atPath: parent)) ?? []
        return
            names
            .filter { $0.utf8.count == length && $0.utf8.allSatisfy { $0 >= 48 && $0 <= 57 } }
            .map { (parent as NSString).appendingPathComponent($0) }
            .filter { kind($0) == .typeDirectory }
            .sorted(by: >)
    }

    /// The regular `rollout-*.jsonl` files of a day folder; imports and links are left out.
    private func rollouts(_ dayDirectory: String) -> [Rollout] {
        guard Self.kind(dayDirectory) == .typeDirectory,
            let names = try? FileManager.default.contentsOfDirectory(atPath: dayDirectory)
        else { return [] }
        var files: [Rollout] = []
        for name in names where name.hasPrefix("rollout-") && name.hasSuffix(".jsonl") {
            guard name.range(of: "external-import", options: .caseInsensitive) == nil else { continue }
            let path = (dayDirectory as NSString).appendingPathComponent(name)
            guard let attributes = try? FileManager.default.attributesOfItem(atPath: path),
                attributes[.type] as? FileAttributeType == .typeRegular,
                let size = (attributes[.size] as? NSNumber)?.int64Value,
                let mtime = attributes[.modificationDate] as? Date
            else { continue }
            files.append(Rollout(path: path, name: name, size: size, mtime: mtime))
        }
        return files
    }

    /// The type of what is at `path` itself, a link not followed.
    private static func kind(_ path: String) -> FileAttributeType? {
        (try? FileManager.default.attributesOfItem(atPath: path))?[.type] as? FileAttributeType
    }

    // ---- the spend cache

    private func loadCache() {
        guard FileManager.default.fileExists(atPath: cachePath),
            let data = try? Data(contentsOf: URL(fileURLWithPath: cachePath)),
            let root = JsonRead.object(data, maxDepth: 8),
            JsonRead.int64(root["v"]) == Int64(Self.cacheVersion),
            let files = root["files"] as? [Any]
        else { return }
        for case let f as [String: Any] in files {
            guard let key = f["p"] as? String, (1...300).contains(key.utf16.count),
                let size = Self.long(f["s"]), let stamp = Self.long(f["t"]), let input = Self.long(f["i"]),
                let cached = Self.long(f["c"]), let output = Self.long(f["o"])
            else { continue }
            let totals = RolloutTotals(model: f["m"] as? String, input: input, cachedInput: cached, output: output)
            cache[key] = CacheEntry(size: size, stamp: stamp, totals: totals)
        }
    }

    private func saveCache() {
        let files: [[String: Any]] = cache.map { key, e in
            var item: [String: Any] = [
                "p": key, "s": e.size, "t": e.stamp, "i": e.totals.input, "c": e.totals.cachedInput,
                "o": e.totals.output,
            ]
            if let model = e.totals.model {
                item["m"] = model
            }
            return item
        }
        let root: [String: Any] = ["v": Self.cacheVersion, "files": files]
        // The cache only saves work; the next refresh rebuilds what is missing.
        guard let data = try? JSONSerialization.data(withJSONObject: root, options: [.withoutEscapingSlashes]),
            writePrivately(String(decoding: data, as: UTF8.self), to: cachePath) == nil
        else { return }
        cacheDirty = false
    }

    private static func long(_ value: Any?) -> Int64? {
        JsonRead.int64(value).flatMap { $0 >= 0 ? $0 : nil }
    }
}
