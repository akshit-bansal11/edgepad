import XCTest

@testable import EdgepadCore

final class ClaudePricesTests: XCTestCase {
    func testPricesFollowTheTableAndItsPrefixes() {
        let million = 1_000_000
        XCTAssertEqual(ClaudePrices.cost("claude-opus-4-5-20251101", ["input_tokens": million]), 5, accuracy: 1e-9)
        XCTAssertEqual(ClaudePrices.cost("claude-opus-4-5", ["output_tokens": million]), 25, accuracy: 1e-9)
        XCTAssertEqual(ClaudePrices.cost("claude-opus-4-5", ["cache_read_input_tokens": million]), 0.5, accuracy: 1e-9)
        XCTAssertEqual(ClaudePrices.cost("claude-opus-4-1-x", ["input_tokens": million]), 15, accuracy: 1e-9)
        XCTAssertEqual(
            ClaudePrices.cost("us.anthropic.claude-opus-4-1-v1", ["input_tokens": million]), 15, accuracy: 1e-9)
        XCTAssertEqual(ClaudePrices.cost("claude-opus-5-5", ["input_tokens": million]), 4, accuracy: 1e-9)
    }

    func testCacheWritesSplitByLifetime() {
        let split: [String: Any] = ["ephemeral_1h_input_tokens": 400_000]
        let usage: [String: Any] = ["cache_creation_input_tokens": 1_000_000, "cache_creation": split]
        // 600k at 1.25x and 400k at 2x of $5.
        XCTAssertEqual(ClaudePrices.cost("claude-opus-4-5", usage), 0.6 * 5 * 1.25 + 0.4 * 5 * 2, accuracy: 1e-9)
    }

    func testALongHaikuPromptCostsFiveTimes() {
        XCTAssertEqual(ClaudePrices.cost("claude-haiku-5-5", ["input_tokens": 200_000]), 0.1, accuracy: 1e-9)
        XCTAssertEqual(ClaudePrices.cost("claude-haiku-5-5", ["input_tokens": 100_000]), 0.01, accuracy: 1e-9)
    }

    func testAnUnknownModelHasNoPriceAndCostsNothing() {
        XCTAssertFalse(ClaudePrices.isPriced("gpt-9"))
        XCTAssertFalse(ClaudePrices.isPriced(nil))
        XCTAssertTrue(ClaudePrices.isPriced("claude-sonnet-4-5"))
        XCTAssertEqual(ClaudePrices.cost("gpt-9", ["input_tokens": 1_000_000]), 0)
        XCTAssertEqual(ClaudePrices.cost("claude-opus-4-5", ["input_tokens": "1000000"]), 0)
    }
}

final class ClaudeSpendTests: XCTestCase {
    private var root = ""
    private var spend: SpendBoard!

    override func setUpWithError() throws {
        try super.setUpWithError()
        root = (NSTemporaryDirectory() as NSString).appendingPathComponent("claude-" + UUID().uuidString)
        try FileManager.default.createDirectory(atPath: root + "/projects/app", withIntermediateDirectories: true)
        spend = SpendBoard(store: UsageStore(path: root + "/usage.json", now: Date()))
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: root)
        try super.tearDownWithError()
    }

    private func utc() -> Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC") ?? .current
        return calendar
    }

    func testPeriodsAreTheLast7And30DaysCountingToday() {
        // Thursday 2026-11-19, Monday 2026-11-16 and Wednesday 2026-12-02, noon UTC.
        let thursday = ClaudeSpend.periods(Date(timeIntervalSince1970: 1_795_089_600), utc())
        XCTAssertEqual(thursday.today, "2026-11-19")
        XCTAssertEqual(thursday.start7, "2026-11-13")
        XCTAssertEqual(thursday.start30, "2026-10-21")
        XCTAssertEqual(thursday.from, "2026-10-21")

        let monday = ClaudeSpend.periods(Date(timeIntervalSince1970: 1_794_830_400), utc())
        XCTAssertEqual(monday.today, "2026-11-16")
        XCTAssertEqual(monday.start7, "2026-11-10")
        XCTAssertEqual(monday.start30, "2026-10-18")

        let wednesday = ClaudeSpend.periods(Date(timeIntervalSince1970: 1_796_212_800), utc())
        XCTAssertEqual(wednesday.today, "2026-12-02")
        XCTAssertEqual(wednesday.start7, "2026-11-26")
        XCTAssertEqual(wednesday.start30, "2026-11-03")
        XCTAssertEqual(wednesday.from, "2026-11-03")
    }

    private func row(_ id: String, _ model: String, _ input: Int, _ output: Int, at: Date, type: String = "assistant")
        -> String
    {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let usage: [String: Any] = ["input_tokens": input, "output_tokens": output]
        let message: [String: Any] = ["id": id, "model": model, "usage": usage]
        let object: [String: Any] = [
            "type": type, "timestamp": formatter.string(from: at), "uuid": "u-" + id, "message": message,
        ]
        let data = (try? JSONSerialization.data(withJSONObject: object)) ?? Data()
        return String(decoding: data, as: UTF8.self)
    }

    private func write(_ name: String, _ lines: [String]) {
        let path = root + "/projects/app/" + name
        XCTAssertTrue(FileManager.default.createFile(atPath: path, contents: Data(lines.joined(separator: "\n").utf8)))
    }

    private func newSpend(
        linked: Bool = true, clock: @escaping () -> Date = Date.init,
        timers: ((@escaping () -> Void, TimeInterval) -> Stoppable)? = nil
    ) -> ClaudeSpend {
        ClaudeSpend(
            settingsOn: { true }, phoneLinked: { linked }, spend: spend, clock: clock, root: root,
            cachePath: root + "/cache.json", startTimer: timers ?? { _, _ in Stoppable {} })
    }

    private func stamp(_ name: String, _ date: Date) throws {
        try FileManager.default.setAttributes(
            [.modificationDate: date], ofItemAtPath: root + "/projects/app/" + name)
    }

    private var claudeEntry: SpendEntry? {
        spend.snapshot().first { $0.agent == AgentToken.claude.rawValue }
    }

    func testAllTimeIsReadOnceAndAVanishedTranscriptKeepsItsTotals() throws {
        let now = Date()
        let old = now.addingTimeInterval(-150 * 86400)
        let model = "claude-sonnet-4-5"
        write(
            "old.jsonl",
            [row("m-old", model, 100_000, 10_000, at: old), row("m-old2", model, 100_000, 10_000, at: old)])
        try stamp("old.jsonl", now.addingTimeInterval(-100 * 86400))
        write("new.jsonl", [row("m-new", model, 100_000, 10_000, at: now)])

        newSpend().poll()
        let first = try XCTUnwrap(claudeEntry)
        XCTAssertEqual(first.tokToday, 110_000)
        XCTAssertEqual(first.tokAll, 330_000)
        XCTAssertEqual(first.centsAll, 135)

        // The old transcript was folded to day totals: its ids are not in the cache any more.
        let cached = try String(contentsOfFile: root + "/cache.json", encoding: .utf8)
        XCTAssertFalse(cached.contains("m-old"))

        // A restart gives the same, and so does a transcript Claude Code deleted: its totals stay.
        try FileManager.default.removeItem(atPath: root + "/projects/app/old.jsonl")
        var current = now
        let second = newSpend(clock: { current })
        second.poll()
        XCTAssertEqual(claudeEntry?.tokAll, 330_000)

        // Only a new or changed file is read, and all time grows by it.
        write("later.jsonl", [row("m-later", model, 100_000, 10_000, at: now)])
        current = now.addingTimeInterval(120)
        second.poll()
        XCTAssertEqual(claudeEntry?.tokAll, 440_000)
    }

    func testAllTimeIsNotAnnouncedAsAPartialSumWhileOldHistoryIsStillBeingRead() throws {
        let now = Date()
        let old = now.addingTimeInterval(-150 * 86400)
        let model = "claude-sonnet-4-5"
        write("a.jsonl", [row("m-big", model, 1, 1, at: old), String(repeating: "x", count: 4000)])
        try stamp("a.jsonl", now.addingTimeInterval(-100 * 86400))
        write("b.jsonl", [row("m-b", model, 5, 5, at: old)])
        try stamp("b.jsonl", now.addingTimeInterval(-101 * 86400))
        write("new.jsonl", [row("m-new", model, 100_000, 10_000, at: now)])
        let reader = newSpend()

        // One tick's whole budget goes to the new file and the first old one; the second old one waits.
        reader.poll(budget: 3000)
        let partial = try XCTUnwrap(claudeEntry)
        XCTAssertEqual(partial.tokToday, 110_000)
        XCTAssertEqual(partial.tokAll, -1)

        reader.poll(budget: 3000)
        XCTAssertEqual(claudeEntry?.tokAll, 110_012)
    }

    func testAResponseCountsOnceByItsLastRowAndOnlyRealOnesCount() {
        let now = Date()
        let old = now.addingTimeInterval(-45 * 24 * 60 * 60)
        write(
            "a.jsonl",
            [
                row("m1", "claude-opus-4-5", 1_000_000, 10, at: now),
                row("m1", "claude-opus-4-5", 1_000_000, 20, at: now),
                row("s1", "<synthetic>", 500, 500, at: now),
                row("u1", "claude-opus-4-5", 700, 700, at: now, type: "user"),
                row("o1", "claude-opus-4-5", 900, 900, at: old),
                "not json at all \"usage\"",
            ])
        write("b.jsonl", [row("m1", "claude-opus-4-5", 1_000_000, 20, at: now), row("m2", "gpt-x", 10, 0, at: now)])

        let found = newSpend().scan()
        XCTAssertEqual(found.totals.tokToday, 1_000_030)
        XCTAssertEqual(found.totals.centsToday, 500)
        XCTAssertEqual(found.totals.tokWeek, 1_000_030)
        XCTAssertEqual(found.totals.tokMonth, 1_000_030)
        XCTAssertTrue(found.unpriced)
    }

    func testTheCacheGivesTheSameTotalsAndAFileThatGoesStopsCounting() throws {
        write("a.jsonl", [row("m1", "claude-sonnet-4-5", 1_000, 1_000, at: Date())])
        let first = newSpend().scan()
        XCTAssertEqual(first.totals.tokToday, 2_000)
        XCTAssertTrue(FileManager.default.fileExists(atPath: root + "/cache.json"))

        let again = newSpend().scan()
        XCTAssertEqual(again.totals, first.totals)

        try FileManager.default.removeItem(atPath: root + "/projects/app/a.jsonl")
        XCTAssertEqual(newSpend().scan().totals.tokToday, 0)
    }

    func testTheCacheIsSavedAtMostEveryFiveMinutesAndOnceMoreOnStop() throws {
        write("a.jsonl", [row("m1", "claude-sonnet-4-5", 1_000, 1_000, at: Date())])
        let reader = newSpend()
        _ = reader.scan()
        XCTAssertTrue(FileManager.default.fileExists(atPath: root + "/cache.json"))
        try FileManager.default.removeItem(atPath: root + "/cache.json")

        write("b.jsonl", [row("m2", "claude-sonnet-4-5", 10, 10, at: Date())])
        _ = reader.scan()
        XCTAssertFalse(FileManager.default.fileExists(atPath: root + "/cache.json"))

        reader.flushCacheNow()
        XCTAssertTrue(FileManager.default.fileExists(atPath: root + "/cache.json"))
    }

    func testItReadsOnlyWhileTheAiScreenIsWatchedWithAPhoneLinked() {
        var started = 0
        let timers: (@escaping () -> Void, TimeInterval) -> Stoppable = { _, _ in
            started += 1
            return Stoppable {}
        }
        let reader = newSpend(timers: timers)
        XCTAssertFalse(reader.running)
        reader.setWatch(1)
        XCTAssertFalse(reader.running)
        reader.setWatch(ClaudeSpend.aiBit)
        XCTAssertTrue(reader.running)
        reader.setWatch(ClaudeSpend.aiBit | 1)
        XCTAssertEqual(started, 1)
        reader.setWatch(0)
        XCTAssertFalse(reader.running)

        let alone = newSpend(linked: false, timers: timers)
        alone.setWatch(ClaudeSpend.aiBit)
        XCTAssertFalse(alone.running)
    }

    func testATickReadsOnlyItsBudgetAndTheBoardWaitsForTheWholeScan() {
        write("a.jsonl", [row("m1", "claude-sonnet-4-5", 100, 50, at: Date())])
        write("b.jsonl", [row("m2", "claude-sonnet-4-5", 10, 5, at: Date())])
        let reader = newSpend()

        let first = reader.scan(budget: 1)
        XCTAssertFalse(first.complete)
        XCTAssertTrue([150, 15].contains(first.totals.tokToday))
        let second = reader.scan(budget: 1)
        XCTAssertTrue(second.complete)
        XCTAssertEqual(second.totals.tokToday, 165)

        let polled = newSpend()
        try? FileManager.default.removeItem(atPath: root + "/cache.json")
        polled.poll(budget: 1)
        XCTAssertNil(spend.snapshot().first { $0.agent == AgentToken.claude.rawValue })
        polled.poll(budget: 1)
        XCTAssertEqual(spend.snapshot().first { $0.agent == AgentToken.claude.rawValue }?.tokToday, 165)
    }

    func testAPollPutsTheTotalsOnTheBoard() {
        write("a.jsonl", [row("m1", "claude-sonnet-4-5", 100, 50, at: Date())])
        newSpend().poll()

        let entry = spend.snapshot().first { $0.agent == AgentToken.claude.rawValue }
        XCTAssertEqual(entry?.tokToday, 150)
    }
}
