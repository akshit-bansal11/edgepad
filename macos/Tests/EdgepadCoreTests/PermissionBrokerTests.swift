import XCTest

@testable import EdgepadCore

final class PermissionBrokerTests: XCTestCase {
    private var linked = true
    private var now = Date(timeIntervalSince1970: 1_795_089_600)
    private var timers: [() -> Void] = []
    private var asked: [PermPrompt] = []
    private var gone: [String] = []
    private let board = SessionBoard()

    private func newBroker() -> PermissionBroker {
        let broker = PermissionBroker(
            board: board, phoneLinked: { [unowned self] in self.linked }, clock: { [unowned self] in self.now },
            after: { [unowned self] _, action in self.timers.append(action) })
        broker.asked = { [unowned self] in self.asked.append($0) }
        broker.gone = { [unowned self] in self.gone.append($0) }
        return broker
    }

    private func bash(_ command: String, session: String = "s1") -> PermRequest {
        PermRequest(
            token: "T", agent: AgentToken.claude.rawValue, session: session, cwd: "/w", tool: "Bash",
            input: ToolInput(command: command))
    }

    /// Asks and hands back what `done` was told so far, and the hook-left closure.
    private func ask(_ broker: PermissionBroker, _ request: PermRequest) -> (() -> PermDecision?, () -> Void) {
        var result: PermDecision?
        let cancel = broker.request(request) { result = $0 }
        return ({ result }, cancel)
    }

    func testTheFirstAnswerWinsAndEveryLaterOneIsRefused() {
        let broker = newBroker()
        let (outcome, _) = ask(broker, bash("ls"))

        XCTAssertEqual(asked.count, 1)
        let id = asked[0].id
        XCTAssertEqual(id.count, 12)
        XCTAssertTrue(id.unicodeScalars.allSatisfy { ("0"..."9").contains($0) || ("a"..."f").contains($0) })
        XCTAssertNil(outcome())
        XCTAssertTrue(broker.answer(id: id, allow: true))
        XCTAssertEqual(outcome(), .allow)
        XCTAssertEqual(gone, [id])
        XCTAssertFalse(broker.answer(id: id, allow: false))
        XCTAssertFalse(broker.answer(id + "/a"))
        XCTAssertEqual(outcome(), .allow)
    }

    func testAWireAnswerDeniesAndAGuessedOrMalformedOneIsRefused() {
        let broker = newBroker()
        let (outcome, _) = ask(broker, bash("ls"))
        let id = asked[0].id

        XCTAssertFalse(broker.answer("000000000000/a"))
        XCTAssertFalse(broker.answer(id + "/x"))
        XCTAssertFalse(broker.answer(id.uppercased() + "/a"))
        XCTAssertTrue(broker.answer(id + "/d"))
        XCTAssertEqual(outcome(), .deny)
    }

    func testNoAnswerIn30SecondsDefers() {
        let broker = newBroker()
        let (outcome, _) = ask(broker, bash("ls"))
        let id = asked[0].id

        for timer in timers {
            timer()
        }
        XCTAssertEqual(outcome(), .deferred)
        XCTAssertEqual(gone, [id])
        XCTAssertFalse(broker.answer(id: id, allow: true))
    }

    func testTheHookLeavingDefersAndALaterAnswerIsRefused() {
        let broker = newBroker()
        let (outcome, cancel) = ask(broker, bash("ls"))
        let id = asked[0].id

        cancel()
        XCTAssertEqual(outcome(), .deferred)
        XCTAssertEqual(gone, [id])
        XCTAssertFalse(broker.answer(id: id, allow: true))
        cancel()
        XCTAssertEqual(gone, [id])
    }

    func testNoPhoneDefersAtOnceWithoutAsking() {
        linked = false
        let broker = newBroker()
        let (outcome, _) = ask(broker, bash("ls"))

        XCTAssertEqual(outcome(), .deferred)
        XCTAssertTrue(asked.isEmpty)
    }

    func testTheLastPhoneLeavingDefersWhatWaits() {
        let broker = newBroker()
        let (outcome, _) = ask(broker, bash("ls"))

        broker.phonesChanged()
        XCTAssertNil(outcome())
        linked = false
        broker.phonesChanged()
        XCTAssertEqual(outcome(), .deferred)
        XCTAssertEqual(broker.pendingCount, 0)
    }

    func testTheNinthWaitingAskDefers() {
        let broker = newBroker()
        for index in 0..<PermissionBroker.maxPending {
            _ = ask(broker, bash("ls", session: "s\(index)"))
        }
        let (ninth, _) = ask(broker, bash("ls", session: "s9"))

        XCTAssertEqual(ninth(), .deferred)
        XCTAssertEqual(asked.count, PermissionBroker.maxPending)
    }

    func testThe21stAskInAMinuteDefersAndTheNextMinuteAsksAgain() {
        let broker = newBroker()
        for _ in 0..<PermissionBroker.maxPerMinute {
            _ = ask(broker, bash("ls"))
            XCTAssertTrue(broker.answer(id: asked[asked.count - 1].id, allow: false))
        }
        let (refused, _) = ask(broker, bash("ls"))
        XCTAssertEqual(refused(), .deferred)
        XCTAssertEqual(asked.count, PermissionBroker.maxPerMinute)

        now = now.addingTimeInterval(61)
        let (later, _) = ask(broker, bash("ls"))
        XCTAssertNil(later())
        XCTAssertEqual(asked.count, PermissionBroker.maxPerMinute + 1)
    }

    func testAMultiLineCommandIsCompleteAndShownInFull() throws {
        let broker = newBroker()
        _ = ask(broker, bash("echo a\necho b"))

        XCTAssertTrue(asked[0].complete)
        let body = try XCTUnwrap(PermAskBody.parse(asked[0].body))
        XCTAssertEqual(body.text, "echo a\necho b")
        XCTAssertEqual(body.tool, "Bash")
        XCTAssertEqual(body.agent, AgentToken.claude.rawValue)
    }

    func testTextOverTheCapIsLeftOutAndMarkedIncomplete() throws {
        let broker = newBroker()
        _ = ask(broker, bash(String(repeating: "a", count: PermAskBody.maxBytes)))

        XCTAssertFalse(asked[0].complete)
        XCTAssertEqual(try XCTUnwrap(PermAskBody.parse(asked[0].body)).text, "")
        XCTAssertEqual(DashWire.permLabel(asked[0].id, complete: false), asked[0].id + "/0")
    }

    func testABidiOverrideIsShownAsAMarker() throws {
        let broker = newBroker()
        _ = ask(broker, bash("ls \u{202E}txt.exe"))

        XCTAssertEqual(try XCTUnwrap(PermAskBody.parse(asked[0].body)).text, "ls <U+202E>txt.exe")
    }

    func testACodexListWithAControlCharacterIsDenyOnly() {
        let broker = newBroker()
        let input = ToolInput(command: "bash -lc rm x\necho ok", commandSafe: false)
        _ = ask(broker, PermRequest(token: "T", agent: "codex", session: "c", cwd: "", tool: "shell", input: input))

        XCTAssertFalse(asked[0].complete)
    }

    func testAWaitingAskMarksItsSessionWaiting() {
        board.apply(HookEvent(agent: AgentToken.claude.rawValue, sessionId: "s1", event: .prompt, cwd: "/w/app"))
        let broker = newBroker()
        _ = ask(broker, bash("ls"))
        XCTAssertEqual(board.snapshot().map(\.state), [SessionStateToken.wait.rawValue])

        broker.answer(id: asked[0].id, allow: true)
        XCTAssertEqual(board.snapshot().map(\.state), [SessionStateToken.busy.rawValue])
    }

    func testOnlyTheWhitelistedFieldIsShown() {
        func text(_ agent: String, _ tool: String, _ input: ToolInput) -> String {
            let request = PermRequest(token: "T", agent: agent, session: "", cwd: "", tool: tool, input: input)
            return PermissionBroker.textFor(request)
        }
        let all = ToolInput(command: "cmd", filePath: "/f", url: "https://u")
        XCTAssertEqual(text("claude", "Bash", all), "cmd")
        XCTAssertEqual(text("claude", "Edit", all), "/f")
        XCTAssertEqual(text("claude", "NotebookEdit", all), "/f")
        XCTAssertEqual(text("claude", "WebFetch", all), "https://u")
        XCTAssertEqual(text("claude", "mcp.tool", all), "")
        XCTAssertEqual(text("codex", "shell", all), "cmd")
        XCTAssertEqual(text("codex", "apply_patch", ToolInput(filePath: "/p")), "/p")
    }
}
