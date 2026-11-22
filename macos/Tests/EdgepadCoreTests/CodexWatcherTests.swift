import XCTest

@testable import EdgepadCore

final class CodexWatcherTests: XCTestCase {
    private var temp = ""
    private var opened: [String] = []
    private var timers = 0
    private var timersStopped = 0
    private var now = Date()
    private var on = true
    private var linked = true
    private var store: UsageStore!
    private var sessions: SessionBoard!
    private var limits: LimitsBoard!
    private var spend: SpendBoard!
    private var watcher: CodexWatcher!
    private let openLock = NSLock()

    private var root: String { (temp as NSString).appendingPathComponent("codex") }
    private var cachePath: String { (temp as NSString).appendingPathComponent(CodexWatcher.cacheName) }

    override func setUpWithError() throws {
        temp = (NSTemporaryDirectory() as NSString).appendingPathComponent("edgepad-codex-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: root, withIntermediateDirectories: true)
        now = try XCTUnwrap(Calendar.current.date(from: DateComponents(year: 2026, month: 11, day: 19, hour: 12)))
        store = UsageStore(path: (temp as NSString).appendingPathComponent(UsageStore.fileName), now: now)
        sessions = SessionBoard(clock: { [unowned self] in self.now })
        limits = LimitsBoard(store: store)
        spend = SpendBoard(store: store)
        watcher = newWatcher()
    }

    override func tearDownWithError() throws {
        watcher.dispose()
        try? FileManager.default.removeItem(atPath: temp)
    }

    private func newWatcher() -> CodexWatcher {
        CodexWatcher(
            settingsOn: { [unowned self] in self.on },
            phoneLinked: { [unowned self] in self.linked },
            sessions: sessions, limits: limits, spend: spend,
            clock: { [unowned self] in self.now },
            root: root, cachePath: cachePath,
            open: { [unowned self] path in
                self.openLock.lock()
                self.opened.append(path)
                self.openLock.unlock()
                return try FileByteSource(path: path)
            },
            startTimer: { [unowned self] _, _ in
                self.timers += 1
                return Stoppable { self.timersStopped += 1 }
            })
    }

    @discardableResult
    private func rollout(_ day: Date, _ name: String, _ content: String, age: TimeInterval = 60) throws -> String {
        let parts = Calendar.current.dateComponents([.year, .month, .day], from: day)
        let folder = String(format: "sessions/%04ld/%02ld/%02ld", parts.year ?? 0, parts.month ?? 0, parts.day ?? 0)
        let dir = (root as NSString).appendingPathComponent(folder)
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        let path = (dir as NSString).appendingPathComponent(name)
        try content.write(toFile: path, atomically: false, encoding: .utf8)
        try stamp(path, now.addingTimeInterval(-age))
        return path
    }

    @discardableResult
    private func today(_ name: String, _ content: String, age: TimeInterval = 60) throws -> String {
        try rollout(now, name, content, age: age)
    }

    private func stamp(_ path: String, _ date: Date) throws {
        try FileManager.default.setAttributes([.modificationDate: date], ofItemAtPath: path)
    }

    private func append(_ path: String, _ text: String) throws {
        let handle = try FileHandle(forWritingTo: URL(fileURLWithPath: path))
        try handle.seekToEnd()
        try handle.write(contentsOf: Data(text.utf8))
        try handle.close()
    }

    private var openedNow: [String] {
        openLock.lock()
        defer { openLock.unlock() }
        return opened
    }

    private let minutes: TimeInterval = 60
    private let hours: TimeInterval = 3600
    private let working = RolloutText.join(RolloutText.meta, RolloutText.started)

    func testNoTimerExistsUntilTheIntegrationIsOnAndAPhoneIsLinked() {
        on = false
        linked = false
        watcher.evaluate()
        on = true
        watcher.evaluate()
        XCTAssertFalse(watcher.running)
        XCTAssertEqual(timers, 0)

        linked = true
        watcher.evaluate()
        XCTAssertTrue(watcher.running)
        XCTAssertEqual(timers, 1)

        watcher.evaluate()
        XCTAssertEqual(timers, 1)

        linked = false
        watcher.evaluate()
        XCTAssertFalse(watcher.running)
        XCTAssertEqual(timersStopped, 1)
    }

    func testPollingDoesNothingWhenOffOrUnlinked() throws {
        try today("rollout-a.jsonl", working)
        watcher.setWatch(CodexWatcher.aiBit)

        on = false
        watcher.poll()
        on = true
        linked = false
        watcher.poll()

        XCTAssertTrue(openedNow.isEmpty)
        XCTAssertTrue(sessions.snapshot().isEmpty)
        XCTAssertTrue(limits.snapshot().isEmpty)
    }

    func testAWorkingRolloutShowsAsABusySessionThenIdle() throws {
        let path = try today("rollout-2026-11-19T11-00-00-aaa.jsonl", working)

        watcher.poll()

        let session = sessions.snapshot()
        XCTAssertEqual(session.map(\.agent), [AgentToken.codex.rawValue])
        XCTAssertEqual(session.map(\.state), [SessionStateToken.busy.rawValue])
        XCTAssertEqual(session.map(\.title), ["demo"])

        try append(path, RolloutText.complete + "\n")
        try stamp(path, now)
        watcher.poll()

        XCTAssertEqual(sessions.snapshot().map(\.state), [SessionStateToken.idle.rawValue])
    }

    func testAFileWithNoMarkerIsWorkingWhenJustWrittenAndUnknownWhenNot() throws {
        try today("rollout-new.jsonl", RolloutText.join(RolloutText.meta), age: 10)
        try today("rollout-old.jsonl", RolloutText.join(RolloutText.meta), age: 5 * minutes)

        watcher.poll()

        XCTAssertEqual(sessions.snapshot().map(\.state), [SessionStateToken.busy.rawValue])
    }

    func testAWorkingFileWhoseMarkerLeftTheTailStaysWorking() throws {
        let path = try today("rollout-a.jsonl", working)
        watcher.poll()
        XCTAssertEqual(sessions.snapshot().map(\.state), [SessionStateToken.busy.rawValue])

        let line = #"{"type":"event_msg","payload":{"type":"agent_message","message":""#
        let filler = String(repeating: "x", count: 900)
        let block = Array(repeating: line + filler + #""}}"# + "\n", count: 400).joined()
        try append(path, block)
        try stamp(path, now.addingTimeInterval(-5 * minutes))
        watcher.poll()

        XCTAssertEqual(sessions.snapshot().map(\.state), [SessionStateToken.busy.rawValue])
    }

    func testAnUnchangedFileIsNotReadAgain() throws {
        try today("rollout-a.jsonl", working)
        watcher.poll()
        let reads = openedNow.count

        watcher.poll()
        watcher.poll()

        XCTAssertEqual(openedNow.count, reads)
    }

    func testABusyFileUntouchedForTenMinutesIsIdle() throws {
        try today("rollout-a.jsonl", working)
        watcher.poll()
        XCTAssertEqual(sessions.snapshot().map(\.state), [SessionStateToken.busy.rawValue])

        now += CodexWatcher.fresh + minutes
        watcher.poll()

        XCTAssertEqual(sessions.snapshot().map(\.state), [SessionStateToken.idle.rawValue])
    }

    func testAFileFiveMinutesOldIsBusyAndFiveHoursOldIsIdle() throws {
        try today("rollout-a.jsonl", working, age: 5 * minutes)
        try today("rollout-b.jsonl", working, age: 5 * hours)

        watcher.poll()

        let states = sessions.snapshot().map(\.state).sorted()
        XCTAssertEqual(states, [SessionStateToken.busy.rawValue, SessionStateToken.idle.rawValue].sorted())
    }

    func testAFileOlderThanSixHoursNeverStartsASession() throws {
        try today("rollout-a.jsonl", working, age: 7 * hours)

        watcher.poll()

        XCTAssertTrue(sessions.snapshot().isEmpty)
        XCTAssertTrue(openedNow.isEmpty)
    }

    func testAtMostEightFilesAreLookedAt() throws {
        for i in 0..<11 {
            try today("rollout-\(i).jsonl", working, age: Double(1 + i) * minutes)
        }

        watcher.poll()

        XCTAssertEqual(sessions.snapshot().count, 8)
        XCTAssertEqual(openedNow.count, 8)
    }

    func testYesterdaysFolderIsLookedAtToo() throws {
        try rollout(now.addingTimeInterval(-24 * hours), "rollout-y.jsonl", working)

        watcher.poll()

        XCTAssertEqual(sessions.snapshot().count, 1)
    }

    func testImportFilesAreIgnoredForStateLimitsAndSpend() throws {
        let fixture = try RolloutText.fixture()
        try today("rollout-2026-08-10T00-00-00-external-import-turn-3.jsonl", fixture + RolloutText.started + "\n")
        watcher.setWatch(CodexWatcher.aiBit)

        watcher.poll()

        XCTAssertTrue(sessions.snapshot().isEmpty)
        XCTAssertTrue(limits.snapshot().isEmpty)
        XCTAssertTrue(spend.snapshot().isEmpty)
        XCTAssertTrue(openedNow.isEmpty)
    }

    func testLimitsAreReadOnlyWhileThePhoneDrawsThem() throws {
        try today("rollout-a.jsonl", try RolloutText.fixture())

        watcher.poll()
        XCTAssertTrue(limits.snapshot().isEmpty)
        XCTAssertTrue(spend.snapshot().isEmpty)

        watcher.setWatch(CodexWatcher.aiBit)
        watcher.poll()

        let limit = try XCTUnwrap(limits.snapshot().first)
        XCTAssertEqual(limits.snapshot().count, 1)
        XCTAssertEqual(limit.agent, AgentToken.codex.rawValue)
        XCTAssertEqual(limit.window, "43200")
        XCTAssertEqual(limit.pct, 12)
        XCTAssertEqual(limit.resetAtMin, 1_787_897_318 / 60)
    }

    func testTheFirstRecentFileWithALimitProvidesIt() throws {
        let new = RolloutText.join(RolloutText.meta, RolloutText.count("null"))
        try today("rollout-2026-11-19T11-30-00-new.jsonl", new)
        let old = RolloutText.join(
            RolloutText.meta, RolloutText.count(RolloutText.limits("codex", "33", 10080, 1_787_897_318)))
        try today("rollout-2026-11-19T10-00-00-old.jsonl", old, age: 2 * hours)
        watcher.setWatch(CodexWatcher.aiBit)

        watcher.poll()

        XCTAssertEqual(limits.snapshot().map(\.window), ["10080"])
        XCTAssertEqual(limits.snapshot().map(\.pct), [33])
    }

    func testUsageIsRefreshedAgainOnlyEveryTwelfthPollOrWhenTheBitIsGained() throws {
        try today("rollout-a.jsonl", try RolloutText.fixture())
        watcher.setWatch(CodexWatcher.aiBit)
        watcher.poll()
        let first = try XCTUnwrap(spend.snapshot().first).measuredAtMin

        now += 5 * minutes
        watcher.poll()
        XCTAssertEqual(spend.snapshot().first?.measuredAtMin, first)

        for _ in 0..<12 {
            watcher.poll()
        }
        XCTAssertNotEqual(spend.snapshot().first?.measuredAtMin, first)
    }

    func testSpendBucketsFilesByTheirFolderDate() throws {
        let fixture = try RolloutText.fixture()
        let calendar = Calendar.current
        let first = try XCTUnwrap(calendar.date(from: DateComponents(year: 2026, month: 11, day: 1, hour: 12)))
        let lastMonth = try XCTUnwrap(calendar.date(from: DateComponents(year: 2026, month: 10, day: 31, hour: 12)))
        try today("rollout-today.jsonl", fixture, age: 3 * hours)
        try rollout(now.addingTimeInterval(-24 * hours), "rollout-yesterday.jsonl", fixture, age: 3 * hours)
        try rollout(first, "rollout-first.jsonl", fixture, age: 3 * hours)
        try rollout(lastMonth, "rollout-lastmonth.jsonl", fixture, age: 3 * hours)
        watcher.setWatch(CodexWatcher.aiBit)

        watcher.poll()

        let entry = try XCTUnwrap(spend.snapshot().first)
        XCTAssertEqual(spend.snapshot().count, 1)
        XCTAssertEqual(entry.agent, AgentToken.codex.rawValue)
        XCTAssertEqual(entry.tokToday, 47898)
        XCTAssertEqual(entry.centsToday, 3)
        XCTAssertEqual(entry.tokWeek, 2 * 47898)
        XCTAssertEqual(entry.centsWeek, 6)
        XCTAssertEqual(entry.tokMonth, 3 * 47898)
        XCTAssertEqual(entry.centsMonth, 9)
        XCTAssertFalse(watcher.hasUnpriced)
    }

    func testAModelWithNoPriceCountsTokensNotCentsAndIsReported() throws {
        let text = RolloutText.join(
            RolloutText.context("gpt-5.2-codex"), RolloutText.count("null", input: 1000, cached: 200, output: 100))
        try today("rollout-a.jsonl", text, age: 3 * hours)
        watcher.setWatch(CodexWatcher.aiBit)

        watcher.poll()

        XCTAssertEqual(spend.snapshot().map(\.tokToday), [900])
        XCTAssertEqual(spend.snapshot().map(\.centsToday), [0])
        XCTAssertTrue(watcher.hasUnpriced)
    }

    func testSpendIsCachedPerFileBySizeAndModificationTime() throws {
        let path = try today("rollout-a.jsonl", try RolloutText.fixture(), age: 3 * hours)
        watcher.setWatch(CodexWatcher.aiBit)
        watcher.poll()
        XCTAssertFalse(openedNow.isEmpty)

        openLock.lock()
        opened.removeAll()
        openLock.unlock()
        watcher.setWatch(0)
        watcher.setWatch(CodexWatcher.aiBit)
        watcher.poll()
        XCTAssertTrue(openedNow.isEmpty)

        try append(path, RolloutText.count("null", input: 600_000, cached: 461_056, output: 8000) + "\n")
        try stamp(path, now.addingTimeInterval(-2 * hours))
        watcher.setWatch(0)
        watcher.setWatch(CodexWatcher.aiBit)
        watcher.poll()

        XCTAssertFalse(openedNow.isEmpty)
        XCTAssertTrue(openedNow.allSatisfy { $0 == path })
        XCTAssertEqual(spend.snapshot().map(\.tokToday), [600_000 - 461_056 + 8000])
    }

    func testTheCacheSurvivesARestart() throws {
        let path = try today("rollout-a.jsonl", try RolloutText.fixture(), age: 3 * hours)
        watcher.setWatch(CodexWatcher.aiBit)
        watcher.poll()
        XCTAssertTrue(FileManager.default.fileExists(atPath: cachePath))

        // Same size and time, different content: only the cache can still say 47898.
        let attributes = try FileManager.default.attributesOfItem(atPath: path)
        let size = try XCTUnwrap(attributes[.size] as? NSNumber).intValue
        try Data(count: size).write(to: URL(fileURLWithPath: path))
        try stamp(path, try XCTUnwrap(attributes[.modificationDate] as? Date))
        let again = newWatcher()
        again.setWatch(CodexWatcher.aiBit)
        again.poll()

        XCTAssertEqual(spend.snapshot().map(\.tokToday), [47898])
    }

    func testADamagedCacheIsRebuilt() throws {
        try "{{{ not json".write(toFile: cachePath, atomically: true, encoding: .utf8)
        try today("rollout-a.jsonl", try RolloutText.fixture(), age: 3 * hours)

        let again = newWatcher()
        again.setWatch(CodexWatcher.aiBit)
        again.poll()

        XCTAssertEqual(spend.snapshot().map(\.tokToday), [47898])
    }

    func testOnlyRolloutFilesUnderSessionsAreEverOpened() throws {
        let fixture = try RolloutText.fixture()
        let sessionsDir = (root as NSString).appendingPathComponent("sessions")
        try FileManager.default.createDirectory(atPath: sessionsDir, withIntermediateDirectories: true)
        let others = [(root, "auth.json"), (root, "config.toml"), (root, "state_5.sqlite"), (sessionsDir, "auth.json")]
        for (folder, name) in others {
            try "{\"token\":\"secret\"}".write(
                toFile: (folder as NSString).appendingPathComponent(name), atomically: true, encoding: .utf8)
        }
        try today("rollout-a.jsonl", fixture)
        try today("notes.jsonl", fixture)
        try today("rollout-b.txt", fixture)
        watcher.setWatch(CodexWatcher.aiBit)

        watcher.poll()
        watcher.setWatch(0)
        watcher.setWatch(CodexWatcher.aiBit)
        watcher.poll()

        XCTAssertFalse(openedNow.isEmpty)
        for path in openedNow {
            XCTAssertTrue(path.hasPrefix(sessionsDir + "/"), path)
            XCTAssertTrue((path as NSString).lastPathComponent.hasPrefix("rollout-"), path)
            XCTAssertTrue(path.hasSuffix(".jsonl"), path)
            XCTAssertFalse(path.contains("auth.json"), path)
        }
    }

    func testALinkedRolloutIsNeverOpened() throws {
        let outside = (temp as NSString).appendingPathComponent("secret.jsonl")
        try RolloutText.fixture().write(toFile: outside, atomically: true, encoding: .utf8)
        let real = try today("rollout-real.jsonl", working)
        let link = ((real as NSString).deletingLastPathComponent as NSString).appendingPathComponent("rollout-l.jsonl")
        try FileManager.default.createSymbolicLink(atPath: link, withDestinationPath: outside)
        watcher.setWatch(CodexWatcher.aiBit)

        watcher.poll()

        XCTAssertFalse(openedNow.contains(link))
        XCTAssertFalse(openedNow.contains(outside))
    }

    func testAMissingCodexFolderIsQuietlyNothing() throws {
        try FileManager.default.removeItem(atPath: root)
        watcher.setWatch(CodexWatcher.aiBit)

        watcher.poll()

        XCTAssertTrue(sessions.snapshot().isEmpty)
        XCTAssertTrue(limits.snapshot().isEmpty)
        XCTAssertTrue(spend.snapshot().isEmpty)
    }

    func testTheDefaultRootIsCodexHomeOrDotCodex() {
        let root = CodexWatcher.defaultRoot()
        XCTAssertFalse(root.isEmpty)
        if let home = ProcessInfo.processInfo.environment["CODEX_HOME"], !home.isEmpty {
            XCTAssertEqual(root, home)
        } else {
            XCTAssertTrue(root.hasSuffix(".codex"))
        }
    }
}
