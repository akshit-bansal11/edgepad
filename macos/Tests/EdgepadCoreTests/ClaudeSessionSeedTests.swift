import XCTest

@testable import EdgepadCore

final class ClaudeSessionSeedTests: XCTestCase {
    private let done = #"{"type":"assistant","cwd":"/work/app","message":{"stop_reason":"end_turn"}}"#
    private let toolUse = #"{"type":"assistant","cwd":"/work/app","message":{"stop_reason":"tool_use"}}"#
    private let prompt = #"{"type":"user","cwd":"/work/app","message":{"content":"hi"}}"#

    private var temp = ""
    private let now = Date(timeIntervalSince1970: 1_790_000_000)

    override func setUpWithError() throws {
        try super.setUpWithError()
        temp = (NSTemporaryDirectory() as NSString).appendingPathComponent("edgepad-seed-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: temp + "/claude/projects", withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: temp)
        try super.tearDownWithError()
    }

    private func watcher() -> ClaudeSpend {
        let store = UsageStore(path: temp + "/usage.json", now: now)
        return ClaudeSpend(
            settingsOn: { true }, phoneLinked: { true }, spend: SpendBoard(store: store), clock: { [now] in now },
            root: temp + "/claude", cachePath: temp + "/cache.json", startTimer: { _, _ in Stoppable {} })
    }

    private func board() -> SessionBoard {
        SessionBoard(clock: { [now] in now })
    }

    @discardableResult
    private func transcript(_ id: String, _ content: String, age: TimeInterval, folder: String = "proj") throws
        -> String
    {
        let dir = temp + "/claude/projects/" + folder
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        let path = dir + "/" + id + ".jsonl"
        try content.write(toFile: path, atomically: false, encoding: .utf8)
        try FileManager.default.setAttributes([.modificationDate: now.addingTimeInterval(-age)], ofItemAtPath: path)
        return path
    }

    private func states(_ seeds: [SeedSession]) -> [String: String] {
        Dictionary(uniqueKeysWithValues: seeds.map { ($0.sessionId, $0.state) })
    }

    func testAnOlderSessionIsFoundWithItsFolderAndIdle() throws {
        try transcript("s1", prompt + "\n" + done + "\n", age: 20 * 60)

        let seeds = watcher().listSessions()
        let seed = try XCTUnwrap(seeds.first)
        let sessions = board()
        sessions.seed(seeds)

        XCTAssertEqual(seeds.count, 1)
        XCTAssertEqual(seed.sessionId, "s1")
        XCTAssertEqual(sessions.snapshot().map(\.title), ["app"])
        XCTAssertEqual(sessions.snapshot().map(\.state), [SessionStateToken.idle.rawValue])
    }

    func testAnEmptyOrTinyFileIsIdleWithNoFolder() throws {
        try transcript("a", "", age: 5)
        try transcript("b", String(repeating: "x", count: 10), age: 5)

        let seeds = watcher().listSessions()

        XCTAssertEqual(seeds.count, 2)
        for seed in seeds {
            XCTAssertEqual(seed.state, SessionStateToken.idle.rawValue)
            XCTAssertEqual(seed.cwd, "")
        }
    }

    func testAnUnfinishedTurnIsWorkingOnlyWhileTheFileIsFresh() throws {
        try transcript("a", prompt + "\n", age: 5)
        try transcript("b", prompt + "\n" + toolUse + "\n", age: 100)
        try transcript("c", prompt + "\n" + toolUse + "\n", age: 3 * 60)

        let byId = states(watcher().listSessions())

        XCTAssertEqual(byId["a"], SessionStateToken.busy.rawValue)
        XCTAssertEqual(byId["b"], SessionStateToken.busy.rawValue)
        XCTAssertEqual(byId["c"], SessionStateToken.idle.rawValue)
    }

    func testAFinishedTurnIsIdleEvenWhenJustWritten() throws {
        try transcript("a", toolUse + "\n" + done + "\n" + #"{"type":"ai-title"}"# + "\n", age: 2)
        let ended = #"{"type":"system","subtype":"turn_duration"}"#
        try transcript("b", toolUse + "\n" + ended + "\n", age: 2)

        let seeds = watcher().listSessions()

        XCTAssertEqual(seeds.count, 2)
        XCTAssertTrue(seeds.allSatisfy { $0.state == SessionStateToken.idle.rawValue })
    }

    func testAFileOlderThanSixHoursIsIgnored() throws {
        try transcript("old", done + "\n", age: SessionBoard.expiry + 60)
        try transcript("new", done + "\n", age: SessionBoard.expiry - 60)

        XCTAssertEqual(watcher().listSessions().map(\.sessionId), ["new"])
    }

    func testSubagentTranscriptsAreNotSessions() throws {
        try transcript("agent-1", done + "\n", age: 60, folder: "proj/s1/subagents")

        XCTAssertTrue(watcher().listSessions().isEmpty)
    }

    func testAtMostEightAreListedAndSeeded() throws {
        for i in 0..<12 {
            try transcript("s\(i)", done + "\n", age: Double(1 + i) * 60)
        }

        let seeds = watcher().listSessions()
        let sessions = board()
        sessions.seed(seeds)

        XCTAssertEqual(seeds.count, SessionBoard.capacity)
        XCTAssertEqual(sessions.snapshot().count, SessionBoard.capacity)
        XCTAssertFalse(seeds.contains { $0.sessionId == "s11" })
    }

    func testALaterHookEventReplacesTheSeedWithoutADuplicate() throws {
        try transcript("s1", prompt + "\n", age: 5)
        let sessions = board()
        sessions.seed(watcher().listSessions())
        XCTAssertEqual(sessions.snapshot().map(\.state), [SessionStateToken.busy.rawValue])

        sessions.apply(HookEvent(agent: AgentToken.claude.rawValue, sessionId: "s1", event: .stop, cwd: "/work/app"))

        XCTAssertEqual(sessions.snapshot().map(\.state), [SessionStateToken.idle.rawValue])
    }

    func testASeedNeverOverridesWhatAHookAlreadySaid() {
        let sessions = board()
        let claude = AgentToken.claude.rawValue
        sessions.apply(HookEvent(agent: claude, sessionId: "s1", event: .needsYou, cwd: "/work/app"))

        let other = SeedSession(
            agent: claude, sessionId: "s1", state: SessionStateToken.idle.rawValue, cwd: "/other", touched: now)
        sessions.seed([other])

        XCTAssertEqual(sessions.snapshot().map(\.state), [SessionStateToken.wait.rawValue])
        XCTAssertEqual(sessions.snapshot().map(\.title), ["app"])
    }

    func testASeedDoesNotPushOutAFullBoardOfLiveSessions() {
        let sessions = board()
        let claude = AgentToken.claude.rawValue
        for i in 0..<SessionBoard.capacity {
            sessions.apply(HookEvent(agent: claude, sessionId: "live\(i)", event: .prompt, cwd: "/w"))
        }

        let seed = SeedSession(
            agent: claude, sessionId: "seed", state: SessionStateToken.idle.rawValue, cwd: "/w", touched: now)
        sessions.seed([seed])

        XCTAssertFalse(sessions.snapshot().contains { $0.state == SessionStateToken.idle.rawValue })
        XCTAssertEqual(sessions.snapshot().count, SessionBoard.capacity)
    }

    func testASeedPastTheExpiryIsIgnored() {
        let sessions = board()
        let old = SeedSession(
            agent: AgentToken.claude.rawValue, sessionId: "s1", state: SessionStateToken.idle.rawValue, cwd: "/w",
            touched: now.addingTimeInterval(-SessionBoard.expiry))

        sessions.seed([old])

        XCTAssertTrue(sessions.snapshot().isEmpty)
    }

    func testAHugeFileIsReadOnlyByItsTail() throws {
        let dir = temp + "/claude/projects/proj"
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        let path = dir + "/big.jsonl"
        // Junk with no newline, so only a tail read that cuts the first partial line can reach the real records.
        var data = Data(repeating: UInt8(ascii: "x"), count: 4 * 1024 * 1024)
        data.append(Data(("\n" + prompt + "\n" + done + "\n").utf8))
        XCTAssertTrue(FileManager.default.createFile(atPath: path, contents: data))
        try FileManager.default.setAttributes([.modificationDate: now.addingTimeInterval(-1)], ofItemAtPath: path)

        let seeds = watcher().listSessions()

        XCTAssertEqual(seeds.map(\.state), [SessionStateToken.idle.rawValue])
        XCTAssertEqual(seeds.map(\.cwd), ["/work/app"])
        XCTAssertGreaterThan(data.count, ClaudeSpend.tailBytes)
    }
}
