import XCTest

@testable import EdgepadCore

/// protocol/hook-vectors.txt, which the C# and Node suites read too, and the limits the file leaves to code.
final class HookProtocolTests: XCTestCase {
    private func vectors(_ prefix: String) throws -> [(input: String, expected: String)] {
        try Fixtures.lines("hook-vectors.txt").compactMap { line in
            guard line.hasPrefix(prefix + " "), let cut = line.range(of: " => ") else { return nil }
            let input = String(line[line.index(line.startIndex, offsetBy: prefix.count + 1)..<cut.lowerBound])
            return (input, String(line[cut.upperBound...]))
        }
    }

    private func json(_ text: String) -> Any? {
        try? JSONSerialization.jsonObject(with: Data(text.utf8), options: [.fragmentsAllowed])
    }

    /// The parse as the vector file writes it.
    private func shape(_ request: HookRequest) -> [String: Any] {
        func value(_ text: String?) -> Any { text.map { $0 as Any } ?? NSNull() }
        switch request {
        case .perm(let r):
            return [
                "kind": "perm", "agent": r.agent, "token": r.token, "session": r.session, "cwd": r.cwd, "tool": r.tool,
                "command": value(r.input.command), "file_path": value(r.input.filePath), "url": value(r.input.url),
                "command_safe": r.input.commandSafe, "truncated": r.input.truncated,
                "text": value(PermissionBroker.textFor(r)),
            ]
        case .session(let r):
            return [
                "kind": "session", "agent": r.agent, "token": r.token, "session": r.session, "event": r.event,
                "cwd": r.cwd, "notification_type": value(r.notificationType),
            ]
        case .usage(let r):
            let windows: [[String: Any]] = r.windows.map {
                ["window": $0.window, "pct": $0.pct, "resets_at": value($0.resetsAt)]
            }
            return [
                "kind": "usage", "agent": r.agent, "token": r.token, "windows": windows,
                "context": r.contextPct.map { $0 as Any } ?? NSNull(),
            ]
        case .spend(let r):
            let t = r.totals
            return [
                "kind": "spend", "agent": r.agent, "token": r.token, "today": [t.tokToday, t.centsToday],
                "week": [t.tokWeek, t.centsWeek], "month": [t.tokMonth, t.centsMonth],
            ]
        }
    }

    func testEveryServerVectorParsesAsWritten() throws {
        let cases = try vectors("SERVER")
        XCTAssertGreaterThan(cases.count, 40)
        for (input, expected) in cases {
            let parsed = HookProtocol.parse(Array(input.utf8))
            if expected == "NONE" {
                XCTAssertNil(parsed, input)
                continue
            }
            guard let parsed, let want = json(expected) as? NSDictionary else {
                XCTFail("Not parsed: \(input)")
                continue
            }
            XCTAssertEqual(NSDictionary(dictionary: shape(parsed)), want, input)
        }
    }

    func testEveryClientVectorBuildsItsRequest() throws {
        let cases = try vectors("CLIENT")
        XCTAssertGreaterThan(cases.count, 5)
        for (input, expected) in cases {
            let parts = input.split(separator: " ", maxSplits: 2).map(String.init)
            XCTAssertEqual(parts.count, 3, input)
            let built = HookClient.request(agent: parts[0], kind: parts[1], input: Data(parts[2].utf8))
            if expected == "NONE" {
                XCTAssertNil(built, input)
                continue
            }
            guard let built, let want = json(expected) as? NSDictionary else {
                XCTFail("Not built: \(input)")
                continue
            }
            XCTAssertEqual(NSDictionary(dictionary: built), want, input)
        }
    }

    private func sessionLine(padTo size: Int) -> [UInt8] {
        let head =
            "{\"v\":1,\"token\":\"T\",\"agent\":\"claude\",\"kind\":\"session\","
            + "\"d\":{\"hook_event_name\":\"Stop\",\"x\":\""
        let tail = "\"}}"
        let pad = String(repeating: "a", count: size - head.utf8.count - tail.utf8.count)
        return Array((head + pad + tail).utf8)
    }

    func testALineOf64KiBIsReadAndOneByteMoreIsNot() {
        XCTAssertNotNil(HookProtocol.parse(sessionLine(padTo: HookProtocol.maxLineBytes)))
        XCTAssertNil(HookProtocol.parse(sessionLine(padTo: HookProtocol.maxLineBytes + 1)))
        XCTAssertNil(HookProtocol.parse([]))
    }

    func testCutNeverSplitsASurrogatePair() {
        XCTAssertEqual(HookProtocol.cut("ab\u{1F600}", 3), "ab")
        XCTAssertEqual(HookProtocol.cut("ab\u{1F600}", 4), "ab\u{1F600}")
        XCTAssertEqual(HookProtocol.cut("abc", 2), "ab")
    }

    func testRepliesAreOneJsonLine() {
        let decision = String(decoding: HookProtocol.decisionReply("allow"), as: UTF8.self)
        XCTAssertEqual(decision, "{\"v\":1,\"decision\":\"allow\"}\n")
        XCTAssertEqual(String(decoding: HookProtocol.okReply(), as: UTF8.self), "{\"v\":1,\"ok\":true}\n")
    }

    func testACodexListIsJoinedWithPosixQuotesWhereAWordWouldSplit() {
        XCTAssertEqual(HookProtocol.joinCommand(["bash", "-lc", "ls -la"]), "bash -lc 'ls -la'")
        XCTAssertEqual(HookProtocol.joinCommand(["echo", ""]), "echo ''")
        XCTAssertEqual(HookProtocol.joinCommand(["echo", "it's"]), "echo 'it'\\''s'")
        XCTAssertEqual(HookProtocol.joinCommand(["echo", "say \"hi\""]), "echo 'say \"hi\"'")
        XCTAssertEqual(HookProtocol.joinCommand(["a\tb", "c\nd"]), "'a\tb' 'c\nd'")
        XCTAssertEqual(HookProtocol.joinCommand(["ls", "-la", "/tmp"]), "ls -la /tmp")
    }

    private func perm(_ d: String) -> PermRequest? {
        let line = "{\"v\":1,\"token\":\"T\",\"agent\":\"codex\",\"kind\":\"perm\",\"d\":" + d + "}"
        guard case .perm(let request)? = HookProtocol.parse(Array(line.utf8)) else { return nil }
        return request
    }

    func testACutAnywhereMarksTheRequestTruncated() throws {
        let long = String(repeating: "a", count: HookProtocol.maxTextLength + 1)
        let exact = String(repeating: "a", count: HookProtocol.maxTextLength)
        func input(_ fields: String) throws -> ToolInput {
            try XCTUnwrap(perm("{\"tool_name\":\"shell\",\"tool_input\":{" + fields + "}}")).input
        }

        XCTAssertFalse(try input("\"command\":\"" + exact + "\"").truncated)
        XCTAssertTrue(try input("\"command\":\"" + long + "\"").truncated)
        XCTAssertTrue(try input("\"file_path\":\"" + long + "\"").truncated)
        XCTAssertTrue(try input("\"url\":\"" + long + "\"").truncated)
        XCTAssertTrue(try input("\"command\":[\"echo\",\"" + exact + "\"]").truncated)
        XCTAssertTrue(try XCTUnwrap(perm("{\"tool_name\":\"shell\",\"truncated\":true}")).input.truncated)
        XCTAssertFalse(try XCTUnwrap(perm("{\"tool_name\":\"shell\",\"truncated\":false}")).input.truncated)
        XCTAssertNil(perm("{\"tool_name\":\"shell\",\"truncated\":1}"))
        XCTAssertNil(perm("{\"tool_name\":\"shell\",\"truncated\":\"yes\"}"))
    }

    func testTheClientSaysWhenItCutAField() throws {
        let long = String(repeating: "a", count: HookClient.inputCap + 1)
        func d(_ agent: String, _ fields: String) throws -> [String: Any] {
            let hook = "{\"tool_name\":\"Bash\",\"tool_input\":{" + fields + "}}"
            let built = try XCTUnwrap(HookClient.request(agent: agent, kind: "perm", input: Data(hook.utf8)))
            return try XCTUnwrap(built["d"] as? [String: Any])
        }

        XCTAssertNil(try d("claude", "\"command\":\"ls\"")["truncated"])
        XCTAssertEqual(try d("claude", "\"command\":\"" + long + "\"")["truncated"] as? Bool, true)
        XCTAssertEqual(try d("claude", "\"url\":\"" + long + "\"")["truncated"] as? Bool, true)
        let joined = try d("codex", "\"command\":[\"echo\",\"" + long + "\"]")
        XCTAssertEqual(joined["truncated"] as? Bool, true)
        let command = try XCTUnwrap((joined["tool_input"] as? [String: Any])?["command"] as? String)
        XCTAssertEqual(command.utf16.count, HookClient.inputCap)
        XCTAssertTrue(command.hasPrefix("echo "))
        XCTAssertNil(try d("codex", "\"command\":[\"echo\",\"hi there\"]")["truncated"])
    }

    func testOnlyHookFirstIsAHook() {
        XCTAssertTrue(HookClient.isHook(["--hook", "claude", "perm"]))
        XCTAssertFalse(HookClient.isHook(["/x/edgepad/scripts/edgepad-hook.mjs", "codex", "session"]))
        XCTAssertFalse(HookClient.isHook([]))
        XCTAssertFalse(HookClient.isHook(["-psn_0_12345"]))
    }
}
