import XCTest

@testable import EdgepadCore

/// Builders for rollout lines, in the shape of the P1 fixture, and the fixture file the laptop core's tests read too.
enum RolloutText {
    static let meta =
        #"{"timestamp":"x","ordinal":0,"type":"session_meta","payload":{"cwd":"/work/demo","cli_version":"0.147.0"}}"#
    static let started = #"{"timestamp":"x","ordinal":1,"type":"event_msg","payload":{"type":"task_started"}}"#
    static let complete = #"{"timestamp":"x","ordinal":9,"type":"event_msg","payload":{"type":"task_complete"}}"#
    static let aborted = #"{"timestamp":"x","ordinal":9,"type":"event_msg","payload":{"type":"turn_aborted"}}"#

    static func context(_ model: String) -> String {
        #"{"timestamp":"x","ordinal":5,"type":"turn_context","payload":{"turn_id":"0","model":""# + model + #""}}"#
    }

    /// A token_count line; `rateLimits` is the JSON of rate_limits, "null" included.
    static func count(_ rateLimits: String, input: Int64 = 0, cached: Int64 = 0, output: Int64 = 0) -> String {
        let usage =
            #"{"input_tokens":\#(input),"cached_input_tokens":\#(cached),"cache_write_input_tokens":0,"#
            + #""output_tokens":\#(output)}"#
        return #"{"timestamp":"x","ordinal":6,"type":"event_msg","payload":{"type":"token_count","info":"#
            + #"{"total_token_usage":"# + usage + #"},"rate_limits":"# + rateLimits + "}}"
    }

    /// The JSON of a rate_limits object with a primary window and an optional secondary one.
    static func limits(_ id: String, _ pct: String, _ minutes: Int, _ resetsAt: Int64, secondary: String = "null")
        -> String
    {
        #"{"limit_id":""# + id + #"","primary":{"used_percent":"# + pct + #","window_minutes":\#(minutes),"#
            + #""resets_at":\#(resetsAt)},"secondary":"# + secondary + "}"
    }

    static func join(_ lines: String...) -> String {
        lines.joined(separator: "\n") + "\n"
    }

    static func fixture() throws -> String {
        var directory = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        while true {
            let candidate = directory.appendingPathComponent("shared/Edgepad.Core.Tests/Fixtures/codex-rollout.jsonl")
            if FileManager.default.fileExists(atPath: candidate.path) {
                return try String(contentsOf: candidate, encoding: .utf8)
            }
            let parent = directory.deletingLastPathComponent()
            if parent.path == directory.path { throw CocoaError(.fileNoSuchFile) }
            directory = parent
        }
    }

    static func source(_ text: String) -> DataSource {
        DataSource(Data(text.utf8))
    }
}

/// A seekable buffer that counts the bytes read from it.
final class DataSource: ByteSource {
    private let data: Data
    private var position = 0
    private(set) var bytesRead = 0

    init(_ data: Data) {
        self.data = data
    }

    var length: UInt64? { UInt64(data.count) }

    func seek(to offset: UInt64) throws {
        position = min(Int(offset), data.count)
    }

    func read(upTo count: Int) throws -> Data {
        let end = min(position + count, data.count)
        let chunk = data.subdata(in: position..<end)
        position = end
        bytesRead += chunk.count
        return chunk
    }
}

final class CodexRolloutTests: XCTestCase {
    private let busy = SessionStateToken.busy.rawValue
    private let idle = SessionStateToken.idle.rawValue

    private func tail(_ text: String) throws -> RolloutTail {
        try CodexRollout.parseTail(RolloutText.source(text))
    }

    private func totals(_ text: String) throws -> RolloutTotals {
        try CodexRollout.totals(RolloutText.source(text))
    }

    func testTheFixtureReadsAsTheP1FactsSay() throws {
        let fixture = try RolloutText.fixture()
        let read = try tail(fixture)
        let sum = try totals(fixture)

        XCTAssertEqual(read.state, idle)
        XCTAssertEqual(read.limits, [RolloutWindow(minutes: 43200, pct: 12, resetsAtSec: 1_787_897_318)])
        XCTAssertEqual(read.cwd, "/work/demo")
        XCTAssertEqual(read.cliVersion, "0.147.0")
        XCTAssertEqual(sum.model, "gpt-5.6-luna")
        XCTAssertEqual(sum.tokens, 501_674 - 461_056 + 7280)
        XCTAssertEqual(sum.tokens, 47898)
        let price = try XCTUnwrap(OpenAiPrices.price(sum.model))
        XCTAssertEqual(OpenAiPrices.cents(price, sum), 3)
    }

    func testAnUnmatchedTaskStartedIsWorkingAndAbortedIsIdle() throws {
        XCTAssertEqual(try tail(RolloutText.join(RolloutText.meta, RolloutText.started)).state, busy)
        XCTAssertEqual(try tail(RolloutText.join(RolloutText.started, RolloutText.aborted)).state, idle)
        let again = RolloutText.join(RolloutText.started, RolloutText.complete, RolloutText.started)
        XCTAssertEqual(try tail(again).state, busy)
        XCTAssertNil(try tail("").state)
    }

    func testNullRateLimitsAndANullPrimaryGiveNoLimits() throws {
        XCTAssertTrue(try tail(RolloutText.join(RolloutText.count("null"))).limits.isEmpty)
        let nullPrimary = RolloutText.count(#"{"limit_id":"premium","primary":null,"secondary":null}"#)
        XCTAssertTrue(try tail(RolloutText.join(nullPrimary)).limits.isEmpty)
    }

    func testANullLaterLineDoesNotEraseTheLimitsAlreadySeen() throws {
        let text = RolloutText.join(
            RolloutText.count(RolloutText.limits("codex", "40", 10080, 1_787_897_318)), RolloutText.count("null"))
        XCTAssertEqual(try tail(text).limits.map(\.pct), [40])
    }

    func testTheCodexLimitIsPreferredAndASecondaryWindowIsKept() throws {
        let secondary = #"{"used_percent":3.5,"window_minutes":300,"resets_at":1787000000}"#
        let text = RolloutText.join(
            RolloutText.count(RolloutText.limits("codex", "10", 10080, 1_787_897_318, secondary: secondary)),
            RolloutText.count(RolloutText.limits("premium", "99", 43200, 1_787_897_999)))

        XCTAssertEqual(
            try tail(text).limits,
            [
                RolloutWindow(minutes: 10080, pct: 10, resetsAtSec: 1_787_897_318),
                RolloutWindow(minutes: 300, pct: 4, resetsAtSec: 1_787_000_000),
            ])
    }

    func testWindowsOutsideTheRangeOrMissingFieldsAreRefused() throws {
        let bad = [
            RolloutText.limits("codex", "10", 0, 1), RolloutText.limits("codex", "10", 600_000, 1),
            #"{"limit_id":"codex","primary":{"window_minutes":300},"secondary":null}"#,
            #"{"limit_id":"codex","primary":{"used_percent":"high","window_minutes":300},"secondary":null}"#,
            #"{"limit_id":"codex","primary":{"used_percent":true,"window_minutes":300},"secondary":null}"#,
        ]
        for limits in bad {
            XCTAssertTrue(try tail(RolloutText.join(RolloutText.count(limits))).limits.isEmpty, limits)
        }
    }

    func testAPercentageIsRoundedAndClamped() throws {
        let high = RolloutText.join(RolloutText.count(RolloutText.limits("codex", "250.4", 300, 1)))
        let mid = RolloutText.join(RolloutText.count(RolloutText.limits("codex", "12.6", 300, 1)))
        XCTAssertEqual(try tail(high).limits.map(\.pct), [100])
        XCTAssertEqual(try tail(mid).limits.map(\.pct), [13])
    }

    func testATruncatedLastLineIsSkippedAndTheRestStands() throws {
        let cut = String(RolloutText.count(RolloutText.limits("codex", "77", 10080, 1)).prefix(60))
        let text =
            RolloutText.join(RolloutText.count(RolloutText.limits("codex", "40", 10080, 1)), RolloutText.started) + cut

        let read = try tail(text)

        XCTAssertEqual(read.limits.map(\.pct), [40])
        XCTAssertEqual(read.state, busy)
    }

    func testGarbageLinesAreSkippedOneByOne() throws {
        let deep = String(repeating: "[", count: 40) + #""event_msg""# + String(repeating: "]", count: 40)
        let text = RolloutText.join(
            "not json at all event_msg", #"{"type":"event_msg","payload":5}"#, #"["event_msg"]"#, deep,
            RolloutText.started)
        XCTAssertEqual(try tail(text).state, busy)
    }

    func testABigFileIsReadOnlyAtItsHeadAndItsTail() throws {
        let filler =
            #"{"timestamp":"x","type":"response_item","payload":{"type":"function_call_output","output":""#
            + String(repeating: "a", count: 900) + #""}}"# + "\n"
        var text = RolloutText.meta + "\n" + RolloutText.count(RolloutText.limits("codex", "5", 300, 1)) + "\n"
        text += String(repeating: filler, count: 5 * 1024 * 1024 / filler.utf8.count + 1)
        text += RolloutText.join(RolloutText.started, RolloutText.count(RolloutText.limits("codex", "61", 10080, 7)))
        let source = DataSource(Data(text.utf8))

        let read = try CodexRollout.parseTail(source)

        XCTAssertEqual(read.limits.map(\.pct), [61])
        XCTAssertEqual(read.state, busy)
        XCTAssertEqual(read.cwd, "/work/demo")
        XCTAssertLessThanOrEqual(source.bytesRead, CodexRollout.tailBytes + CodexRollout.headBytes + 32 * 1024)
    }

    func testAnOverlongLineIsSkippedWithoutBeingHeld() throws {
        let filler = String(repeating: "b", count: 2 * 1024 * 1024)
        let huge = #"{"type":"response_item","payload":{"output":""# + filler + #""}}"#
        let text = RolloutText.join(
            RolloutText.context("gpt-5.5"), huge, RolloutText.count("null", input: 1000, cached: 400, output: 50))

        let read = try totals(text)

        XCTAssertEqual(read, RolloutTotals(model: "gpt-5.5", input: 1000, cachedInput: 400, output: 50))
        XCTAssertEqual(read.tokens, 650)
    }

    func testTheLastCumulativeUsageAndTheLastModelWin() throws {
        let text = RolloutText.join(
            RolloutText.context("gpt-5.4"), RolloutText.count("null", input: 100, cached: 10, output: 5),
            RolloutText.context("gpt-5.5"), RolloutText.count("null", input: 300, cached: 100, output: 20))

        XCTAssertEqual(try totals(text), RolloutTotals(model: "gpt-5.5", input: 300, cachedInput: 100, output: 20))
    }

    private func padded(_ lines: Int) -> String {
        let line =
            #"{"type":"event_msg","payload":{"type":"agent_message","message":""# + String(repeating: "x", count: 900)
        return (0..<lines).map { _ in line + #""}}"# }.joined(separator: "\n") + "\n"
    }

    func testBigFilesReadTheLastMebibyteFirstAndKeepTheKnownModel() throws {
        let text =
            RolloutText.join(RolloutText.context("gpt-5.5"), RolloutText.count("null", input: 10, output: 1))
            + padded(1300) + RolloutText.join(RolloutText.count("null", input: 300, cached: 100, output: 20))
        let source = RolloutText.source(text)

        let read = try CodexRollout.totals(source, knownModel: "gpt-5.6-luna")

        XCTAssertEqual(read, RolloutTotals(model: "gpt-5.6-luna", input: 300, cachedInput: 100, output: 20))
        XCTAssertLessThanOrEqual(source.bytesRead, CodexRollout.totalsTailBytes + 16 * 1024)
    }

    func testBigFilesNeedingTheModelOrTheUsageAreReadWhole() throws {
        let usage = RolloutText.count("null", input: 300, cached: 100, output: 20)
        let noModel = RolloutText.join(RolloutText.context("gpt-5.5")) + padded(1300) + RolloutText.join(usage)
        XCTAssertEqual(try totals(noModel).model, "gpt-5.5")

        let noUsage = RolloutText.join(usage) + padded(1300)
        XCTAssertEqual(try totals(noUsage).input, 300)
    }

    func testANegativeCountIsZeroAndCachedAboveInputNeverMakesNegativeTokens() throws {
        XCTAssertEqual(try totals(RolloutText.join(RolloutText.count("null", input: -5, output: 7))).tokens, 7)
        XCTAssertEqual(RolloutTotals(model: "m", input: 10, cachedInput: 50, output: 7).tokens, 7)
    }

    func testAFileWithoutUsageHasZeroTotalsAndNoModel() throws {
        XCTAssertEqual(try totals(RolloutText.join(RolloutText.meta, RolloutText.started)), .zero)
    }

    func testOnlyListedModelsHaveAPrice() {
        let cases: [(String?, Bool)] = [
            ("gpt-5.6-luna", true), ("GPT-5.6-LUNA", true), ("gpt-5.5", true), ("gpt-5.2-codex", false),
            ("gpt-5.1-codex-mini", false), ("codex-auto-review", false), ("gpt-5.5-2099-01-01", false), ("", false),
            (nil, false),
        ]
        for (model, priced) in cases {
            XCTAssertEqual(OpenAiPrices.price(model) != nil, priced, model ?? "nil")
        }
    }

    func testCentsPriceUncachedCachedAndOutputSeparately() throws {
        let price = try XCTUnwrap(OpenAiPrices.price("gpt-5.5"))
        let m = "gpt-5.5"
        XCTAssertEqual(
            OpenAiPrices.cents(price, RolloutTotals(model: m, input: 1_000_000, cachedInput: 0, output: 0)), 500)
        XCTAssertEqual(
            OpenAiPrices.cents(price, RolloutTotals(model: m, input: 1_000_000, cachedInput: 1_000_000, output: 0)), 50)
        XCTAssertEqual(
            OpenAiPrices.cents(price, RolloutTotals(model: m, input: 0, cachedInput: 0, output: 1_000_000)), 3000)
        XCTAssertEqual(OpenAiPrices.cents(price, .zero), 0)
    }
}
