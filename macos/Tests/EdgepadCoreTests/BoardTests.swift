import XCTest

@testable import EdgepadCore

private let claude = AgentToken.claude.rawValue
private let codex = AgentToken.codex.rawValue
private let start = Date(timeIntervalSince1970: 1_795_089_600)  // 2026-11-19 12:00 UTC

final class SessionBoardTests: XCTestCase {
    private var now = start

    private func newBoard() -> SessionBoard {
        SessionBoard(clock: { [unowned self] in self.now })
    }

    private func hook(_ id: String, _ event: SessionEvent, agent: String = claude, cwd: String = "/work/app")
        -> HookEvent
    {
        HookEvent(agent: agent, sessionId: id, event: event, cwd: cwd)
    }

    func testEveryTransitionLandsOnTheStateItNames() {
        let board = newBoard()
        let steps: [(SessionEvent, SessionStateToken)] = [
            (.start, .idle), (.prompt, .busy), (.needsYou, .wait), (.stop, .idle),
        ]
        for (event, state) in steps {
            board.apply(hook("a", event))
            XCTAssertEqual(board.snapshot().map(\.state), [state.rawValue])
        }
        board.apply(hook("a", .end))
        XCTAssertTrue(board.snapshot().isEmpty)
    }

    func testClaudeHooksMapToEvents() {
        let cases: [(String, String?, SessionEvent)] = [
            ("SessionStart", nil, .start), ("UserPromptSubmit", nil, .prompt),
            ("Notification", "permission_prompt", .needsYou), ("Notification", "idle_prompt", .needsYou),
            ("Notification", "agent_needs_input", .needsYou), ("Stop", nil, .stop), ("SessionEnd", nil, .end),
        ]
        for (name, type, expected) in cases {
            let event = HookEvent.fromClaude(name, type, sessionId: "s1", cwd: "/p")
            XCTAssertEqual(event?.event, expected, name)
            XCTAssertEqual(event?.agent, claude)
        }
    }

    func testOtherClaudeHooksAreIgnored() {
        let cases: [(String?, String?)] = [
            ("Notification", "agent_completed"), ("Notification", "auth_success"), ("Notification", nil),
            ("PermissionRequest", nil), ("PreToolUse", nil), (nil, nil),
        ]
        for (name, type) in cases {
            XCTAssertNil(HookEvent.fromClaude(name, type, sessionId: "s1", cwd: "/p"))
        }
    }

    func testCodexMapsSessionStartOnly() {
        XCTAssertEqual(HookEvent.fromCodex("SessionStart", sessionId: "c1", cwd: "/p")?.event, .start)
        XCTAssertNil(HookEvent.fromCodex("Stop", sessionId: "c1", cwd: "/p"))
        XCTAssertEqual(HookEvent.fromCodex("SessionStart", sessionId: "c1", cwd: "/p")?.agent, codex)
    }

    func testASessionWithAnAskPendingWaitsUntilItIsAnswered() {
        let board = newBoard()
        board.apply(hook("a", .prompt))

        board.setWaiting("a", true)
        XCTAssertEqual(board.snapshot().map(\.state), [SessionStateToken.wait.rawValue])

        board.setWaiting("a", false)
        XCTAssertEqual(board.snapshot().map(\.state), [SessionStateToken.busy.rawValue])
    }

    func testAnAskForAnUnknownSessionDoesNothing() {
        let board = newBoard()
        var raised = 0
        board.changed = { raised += 1 }

        board.setWaiting("nobody", true)

        XCTAssertTrue(board.snapshot().isEmpty)
        XCTAssertEqual(raised, 0)
    }

    func testTheTitleIsTheLastFolderCleaned() {
        let cases = [
            ("/home/me/projects/edgepad", "edgepad"), ("/home/me/projects/edgepad/", "edgepad"),
            ("C:\\work\\my-proj\\", "my-proj"), ("C:\\work\\a|b:c", "a b c"), ("", ""),
        ]
        for (cwd, title) in cases {
            let board = newBoard()
            board.apply(hook("a", .start, cwd: cwd))
            XCTAssertEqual(board.snapshot().map(\.title), [title], cwd)
        }
    }

    func testANewestFirstListIsKeptAndNeverPastEight() {
        let board = newBoard()
        board.apply(hook("s0", .prompt, cwd: "/w/0"))
        for i in 1...7 {
            board.apply(hook("s\(i)", .start, cwd: "/w/\(i)"))
        }
        board.apply(hook("s8", .start, cwd: "/w/8"))

        let list = board.snapshot()
        XCTAssertEqual(list.count, SessionBoard.capacity)
        XCTAssertEqual(list.first?.title, "8")
        XCTAssertTrue(list.contains { $0.title == "0" })
        XCTAssertFalse(list.contains { $0.title == "1" })
    }

    func testWithNoIdleSessionTheOldestMakesRoom() {
        let board = newBoard()
        for i in 0..<SessionBoard.capacity {
            board.apply(hook("s\(i)", .prompt, cwd: "/w/\(i)"))
        }
        board.apply(hook("new", .prompt, cwd: "/w/new"))

        let list = board.snapshot()
        XCTAssertEqual(list.count, SessionBoard.capacity)
        XCTAssertFalse(list.contains { $0.title == "0" })
        XCTAssertEqual(list.first?.title, "new")
    }

    func testASessionNobodyTouchedForSixHoursIsForgotten() {
        let board = newBoard()
        board.apply(hook("a", .prompt))

        now += SessionBoard.expiry - 60
        XCTAssertEqual(board.snapshot().count, 1)

        now += 60
        XCTAssertTrue(board.snapshot().isEmpty)
    }

    func testChangedFiresOnlyWhenWhatAPhoneSeesChanges() {
        let board = newBoard()
        var raised = 0
        board.changed = { raised += 1 }

        board.apply(hook("a", .start))
        XCTAssertEqual(raised, 1)
        board.apply(hook("a", .start))
        board.apply(hook("a", .stop))
        XCTAssertEqual(raised, 1)
        board.apply(hook("a", .prompt))
        XCTAssertEqual(raised, 2)
        board.apply(hook("a", .end))
        XCTAssertEqual(raised, 3)
        board.apply(hook("a", .end))
        XCTAssertEqual(raised, 3)
    }

    func testExpiryRaisesChanged() {
        let board = newBoard()
        board.apply(hook("a", .start))
        var raised = 0
        board.changed = { raised += 1 }

        now += SessionBoard.expiry
        board.sweep()

        XCTAssertEqual(raised, 1)
    }

    func testABadIdOrAgentIsIgnored() {
        let board = newBoard()
        board.apply(HookEvent(agent: "claude", sessionId: "", event: .start, cwd: "/p"))
        board.apply(HookEvent(agent: "gemini", sessionId: "id", event: .start, cwd: "/p"))
        board.apply(hook(String(repeating: "x", count: SessionBoard.maxIdLength + 1), .start))
        XCTAssertTrue(board.snapshot().isEmpty)
    }

    func testTheSameIdUnderTwoAgentsIsTwoSessions() {
        let board = newBoard()
        board.apply(hook("same", .start))
        board.apply(hook("same", .prompt, agent: codex))
        XCTAssertEqual(board.snapshot().count, 2)
    }

    func testFourThreadsNeverCorruptTheBoard() {
        let board = newBoard()
        board.changed = { _ = board.snapshot() }
        DispatchQueue.concurrentPerform(iterations: 4) { t in
            for i in 0..<500 {
                board.apply(hook("t\(t)-\(i % 12)", SessionEvent.allCases[i % 5], cwd: "/w/\(t)"))
                board.setWaiting("t\(t)-\(i % 12)", i % 2 == 0)
                _ = board.snapshot()
            }
        }
        XCTAssertLessThanOrEqual(board.snapshot().count, SessionBoard.capacity)
    }
}

final class LimitsBoardTests: XCTestCase {
    private var dir = ""

    override func setUpWithError() throws {
        dir = (NSTemporaryDirectory() as NSString).appendingPathComponent("edgepad-limits-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: dir)
    }

    private var usagePath: String { (dir as NSString).appendingPathComponent(UsageStore.fileName) }

    private func newStore(_ at: Date = start) -> UsageStore {
        UsageStore(path: usagePath, now: at)
    }

    func testWindowsMapToTheWireForm() {
        let cases: [(String, String, String?)] = [
            ("claude", "five_hour", "300"), ("claude", "seven_day", "10080"), ("claude", "spend_limit", "~spend_limit"),
            ("claude", "weird_kind", "~weird_kind"), ("claude", "300", "~300"), ("claude", "", nil),
            ("claude", "   ", nil), ("codex", "10080", "10080"), ("codex", "43200", "43200"), ("codex", "0", nil),
            ("codex", "600000", nil), ("codex", "5h", nil), ("gemini", "300", nil),
        ]
        for (agent, window, expected) in cases {
            XCTAssertEqual(LimitsBoard.windowFor(agent, window), expected, "\(agent) \(window)")
        }
    }

    func testClaudeKindsAreKeptNeverDropped() {
        let board = LimitsBoard(store: newStore())
        let windows = [
            RawLimit("five_hour", 40, nil), RawLimit("seven_day", 10, nil), RawLimit("spend_limit", 5, nil),
            RawLimit("weird_kind", 7, nil), RawLimit("", 99, nil),
        ]
        board.update(claude, windows, contextPct: nil, now: start)

        XCTAssertEqual(board.snapshot().map(\.window), ["300", "10080", "~spend_limit", "~weird_kind"])
    }

    func testPercentagesRoundAndClamp() {
        let board = LimitsBoard(store: newStore())
        let windows = [
            RawLimit("300", 150, nil), RawLimit("10080", -5, nil), RawLimit("43200", 12.5, nil),
            RawLimit("60", .nan, nil), RawLimit("120", .infinity, nil),
        ]
        board.update(codex, windows, contextPct: nil, now: start)

        let byWindow = Dictionary(uniqueKeysWithValues: board.snapshot().map { ($0.window, $0.pct) })
        XCTAssertEqual(byWindow["300"], 100)
        XCTAssertEqual(byWindow["10080"], 0)
        XCTAssertEqual(byWindow["43200"], 13)
        XCTAssertEqual(byWindow["120"], 100)
        XCTAssertNil(byWindow["60"])
    }

    func testResetTimesReadAsIsoDateOrEpochSeconds() {
        let board = LimitsBoard(store: newStore())
        let windows = [
            RawLimit("five_hour", 1, "2026-11-19T15:00:00Z"), RawLimit("seven_day", 1, "1787897318"),
            RawLimit("spend_limit", 1, "garbage"), RawLimit("x", 1, nil),
        ]
        board.update(claude, windows, contextPct: nil, now: start)

        let byWindow = Dictionary(uniqueKeysWithValues: board.snapshot().map { ($0.window, $0.resetAtMin) })
        XCTAssertEqual(byWindow["300"], UsageStore.minutes(start) + 180)
        XCTAssertEqual(byWindow["10080"], 29_798_288)
        XCTAssertEqual(byWindow["~spend_limit"], -1)
        XCTAssertEqual(byWindow["~x"], -1)
    }

    func testTheContextIsItsOwnWindowAndSortsLast() {
        let board = LimitsBoard(store: newStore())
        let windows = [RawLimit("seven_day", 10, nil), RawLimit("five_hour", 20, nil), RawLimit("zzz", 1, nil)]
        board.update(claude, windows, contextPct: 63.4, now: start)

        XCTAssertEqual(board.snapshot().map(\.window), ["300", "10080", "~zzz", "ctx"])
        XCTAssertEqual(board.snapshot().last?.pct, 63)
        XCTAssertEqual(board.snapshot().last?.resetAtMin, -1)
    }

    func testClaudeSortsBeforeCodex() {
        let board = LimitsBoard(store: newStore())
        board.update(codex, [RawLimit("60", 1, nil)], contextPct: nil, now: start)
        board.update(claude, [RawLimit("five_hour", 1, nil)], contextPct: nil, now: start)

        XCTAssertEqual(board.snapshot().map(\.agent), [claude, codex])
    }

    func testAWindowAbsentFromTheReportStaysForTwentyFourHoursThenGoes() {
        let board = LimitsBoard(store: newStore())
        let both = [RawLimit("five_hour", 10, nil), RawLimit("seven_day", 20, nil)]
        board.update(claude, both, contextPct: nil, now: start)

        board.update(claude, [RawLimit("seven_day", 21, nil)], contextPct: nil, now: start + 23 * 3600)
        XCTAssertEqual(board.snapshot().count, 2)

        board.update(claude, [RawLimit("seven_day", 22, nil)], contextPct: nil, now: start + 25 * 3600)
        XCTAssertEqual(board.snapshot().map(\.window), ["10080"])
    }

    func testAnotherAgentsReportDoesNotExpireThisOnesWindows() {
        let board = LimitsBoard(store: newStore())
        board.update(claude, [RawLimit("five_hour", 10, nil)], contextPct: nil, now: start)
        board.update(codex, [RawLimit("60", 1, nil)], contextPct: nil, now: start + 3 * 86400)
        XCTAssertEqual(board.snapshot().count, 2)
    }

    func testChangedFiresOnARealChangeNotOnAFresherMeasurement() {
        let board = LimitsBoard(store: newStore())
        var raised = 0
        board.changed = { raised += 1 }

        board.update(claude, [RawLimit("five_hour", 10, nil)], contextPct: nil, now: start)
        board.update(claude, [RawLimit("five_hour", 10, nil)], contextPct: nil, now: start + 300)
        XCTAssertEqual(raised, 1)
        XCTAssertEqual(board.snapshot().first?.measuredAtMin, UsageStore.minutes(start + 300))

        board.update(claude, [RawLimit("five_hour", 11, nil)], contextPct: nil, now: start + 360)
        XCTAssertEqual(raised, 2)
    }

    func testAnUnknownAgentIsIgnored() {
        let board = LimitsBoard(store: newStore())
        board.update("gemini", [RawLimit("300", 1, nil)], contextPct: nil, now: start)
        XCTAssertTrue(board.snapshot().isEmpty)
    }

    func testLimitsSpendAndLastHeardSurviveARestart() {
        let store = newStore()
        let limits = LimitsBoard(store: store)
        let spend = SpendBoard(store: store)
        limits.update(claude, [RawLimit("five_hour", 42, "1787897318")], contextPct: 7, now: start)
        spend.update(codex, totals(1, 2, 3, 4, 5, 6), now: start)
        store.touch(codex, now: start)

        let again = newStore()

        XCTAssertEqual(LimitsBoard(store: again).snapshot(), limits.snapshot())
        XCTAssertEqual(SpendBoard(store: again).snapshot(), spend.snapshot())
        XCTAssertEqual(again.lastHeard(codex), UsageStore.minutes(start))
        XCTAssertEqual(again.lastHeard(claude), -1)
    }

    func testADamagedFileIsAnEmptyStore() throws {
        let contents = [
            "{not json", "", "[]",
            #"{"v":2,"limits":[{"a":"claude","w":"300","p":1,"r":1,"m":99999999}]}"#,
            #"{"v":1,"limits":[{"a":"claude","w":"5h","p":1,"r":1,"m":99999999}],"spend":[{"a":"x"}],"#
                + #""heard":{"claude":"soon"}}"#,
        ]
        for content in contents {
            try content.write(toFile: usagePath, atomically: true, encoding: .utf8)
            let store = newStore()
            XCTAssertTrue(LimitsBoard(store: store).snapshot().isEmpty, content)
            XCTAssertTrue(SpendBoard(store: store).snapshot().isEmpty, content)
            XCTAssertEqual(store.lastHeard(claude), -1, content)
        }
    }

    func testEntriesOlderThanThirtyDaysAreDroppedOnLoad() {
        let store = newStore()
        LimitsBoard(store: store).update(claude, [RawLimit("five_hour", 10, nil)], contextPct: nil, now: start)
        SpendBoard(store: store).update(claude, totals(1, 1, 1, 1, 1, 1), now: start)
        store.touch(claude, now: start)

        let young = newStore(start + 29 * 86400)
        XCTAssertEqual(LimitsBoard(store: young).snapshot().count, 1)
        XCTAssertEqual(SpendBoard(store: young).snapshot().count, 1)
        XCTAssertNotEqual(young.lastHeard(claude), -1)

        let old = newStore(start + 31 * 86400)
        XCTAssertTrue(LimitsBoard(store: old).snapshot().isEmpty)
        XCTAssertTrue(SpendBoard(store: old).snapshot().isEmpty)
        XCTAssertEqual(old.lastHeard(claude), -1)
    }

    func testTheFileHoldsNumbersAndWindowNamesOnly() throws {
        let store = newStore()
        LimitsBoard(store: store).update(claude, [RawLimit("five_hour", 10, nil)], contextPct: nil, now: start)
        store.touch(claude, now: start)

        let text = try String(contentsOfFile: usagePath, encoding: .utf8)
        XCTAssertTrue(text.contains(#""w":"300""#), text)
        XCTAssertFalse(text.contains("cwd"))
        XCTAssertFalse(text.contains("token"))
    }

    func testAFailedWriteKeepsMemoryAndReportsFalse() throws {
        try FileManager.default.createDirectory(atPath: usagePath + ".tmp", withIntermediateDirectories: true)
        let store = newStore()
        let board = LimitsBoard(store: store)

        board.update(claude, [RawLimit("five_hour", 10, nil)], contextPct: nil, now: start)

        XCTAssertFalse(store.save())
        XCTAssertEqual(board.snapshot().count, 1)
    }

    func testFourThreadsNeverCorruptTheStore() {
        let store = newStore()
        let board = LimitsBoard(store: store)
        let spend = SpendBoard(store: store)
        DispatchQueue.concurrentPerform(iterations: 4) { _ in
            for i in 0..<100 {
                board.update(claude, [RawLimit("five_hour", Double(i), nil)], contextPct: nil, now: start)
                spend.update(codex, totals(Int64(i), 1, 1, 1, 1, 1), now: start)
                store.touch(codex, now: start + Double(i) * 60)
                _ = board.snapshot()
            }
        }
        let again = newStore(start + 6000)
        XCTAssertEqual(LimitsBoard(store: again).snapshot().count, 1)
        XCTAssertEqual(SpendBoard(store: again).snapshot().count, 1)
    }
}

final class SpendBoardTests: XCTestCase {
    private func newBoard() -> SpendBoard {
        let dir = (NSTemporaryDirectory() as NSString).appendingPathComponent("edgepad-spend-\(UUID().uuidString)")
        addTeardownBlock { try? FileManager.default.removeItem(atPath: dir) }
        let path = (dir as NSString).appendingPathComponent(UsageStore.fileName)
        return SpendBoard(store: UsageStore(path: path, now: start))
    }

    func testTotalsAreStoredAndReadBack() {
        let board = newBoard()
        board.update(codex, totals(10, 20, 30, 40, 50, 60), now: start)
        board.update(claude, totals(1, 2, 3, 4, 5, 6), now: start)

        let minute = UsageStore.minutes(start)
        XCTAssertEqual(
            board.snapshot(),
            [
                SpendEntry(
                    agent: claude, measuredAtMin: minute, tokToday: 1, centsToday: 2, tokWeek: 3, centsWeek: 4,
                    tokMonth: 5, centsMonth: 6),
                SpendEntry(
                    agent: codex, measuredAtMin: minute, tokToday: 10, centsToday: 20, tokWeek: 30, centsWeek: 40,
                    tokMonth: 50, centsMonth: 60),
            ])
    }

    func testNegativesAndHugeNumbersAreClamped() {
        let board = newBoard()
        board.update(claude, totals(-1, -2, .max, .max, 5, 6), now: start)

        let entry = board.snapshot().first
        XCTAssertEqual(entry?.tokToday, 0)
        XCTAssertEqual(entry?.centsToday, 0)
        XCTAssertEqual(entry?.tokWeek, SpendBoard.maxTokens)
        XCTAssertEqual(entry?.centsWeek, SpendBoard.maxCents)
    }

    func testChangedFiresOnlyWhenATotalChanges() {
        let board = newBoard()
        var raised = 0
        board.changed = { raised += 1 }

        board.update(claude, totals(1, 1, 1, 1, 1, 1), now: start)
        board.update(claude, totals(1, 1, 1, 1, 1, 1), now: start + 180)
        XCTAssertEqual(raised, 1)

        board.update(claude, totals(2, 1, 1, 1, 1, 1), now: start + 240)
        XCTAssertEqual(raised, 2)
    }

    func testUnpricedSurvivesClampingSaveAndLoad() {
        let dir = (NSTemporaryDirectory() as NSString).appendingPathComponent("edgepad-spend-\(UUID().uuidString)")
        addTeardownBlock { try? FileManager.default.removeItem(atPath: dir) }
        let path = (dir as NSString).appendingPathComponent(UsageStore.fileName)
        let board = SpendBoard(store: UsageStore(path: path, now: start))
        board.update(
            claude,
            SpendTotals(
                tokToday: 1, centsToday: 2, tokWeek: 3, centsWeek: 4, tokMonth: 5, centsMonth: 6, unpriced: true),
            now: start)
        board.update(codex, totals(1, 1, 1, 1, 1, 1), now: start)

        let again = SpendBoard(store: UsageStore(path: path, now: start))
        XCTAssertEqual(again.snapshot().map(\.unpriced), [true, false])
    }

    func testAnUnknownAgentIsIgnored() {
        let board = newBoard()
        board.update("gemini", totals(1, 1, 1, 1, 1, 1), now: start)
        XCTAssertTrue(board.snapshot().isEmpty)
    }
}

private func totals(_ a: Int64, _ b: Int64, _ c: Int64, _ d: Int64, _ e: Int64, _ f: Int64) -> SpendTotals {
    SpendTotals(tokToday: a, centsToday: b, tokWeek: c, centsWeek: d, tokMonth: e, centsMonth: f)
}
