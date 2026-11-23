import XCTest

@testable import EdgepadCore

/// Two connected ends, as the listener and the hook client would hold them.
private func socketPair() -> (near: SocketStream, far: SocketStream) {
    var fds: [Int32] = [-1, -1]
    precondition(socketpair(AF_UNIX, SOCK_STREAM, 0, &fds) == 0)
    return (SocketStream(fd: fds[0]), SocketStream(fd: fds[1]))
}

/// One reply line without its line feed, or nil when the stream ended first or nothing came in time.
private func readReply(_ stream: HookStream, within seconds: TimeInterval = 3) -> String? {
    let deadline = Date().addingTimeInterval(seconds)
    var bytes: [UInt8] = []
    while let chunk = stream.read(limit: 4096, until: deadline), !chunk.isEmpty {
        bytes += chunk
        if let end = bytes.firstIndex(of: UInt8(ascii: "\n")) {
            return String(decoding: bytes[..<end], as: UTF8.self)
        }
    }
    return nil
}

/// A short folder of its own under the temporary folder: a socket path must fit 104 bytes.
private func shortFolder() -> String {
    let name = "ep" + String(UUID().uuidString.prefix(6))
    return (NSTemporaryDirectory() as NSString).appendingPathComponent(name)
}

/// Hands the server the streams a test pushes.
private final class FakeListener: HookListening {
    let endpoint = "/fake/hook.sock"
    private let condition = NSCondition()
    private var waiting: [HookStream] = []
    private var closed = false

    func push(_ stream: HookStream) {
        condition.lock()
        waiting.append(stream)
        condition.signal()
        condition.unlock()
    }

    func accept() -> HookStream? {
        condition.lock()
        defer { condition.unlock() }
        while waiting.isEmpty && !closed {
            condition.wait()
        }
        return closed ? nil : waiting.removeFirst()
    }

    func close() {
        condition.lock()
        closed = true
        condition.broadcast()
        condition.unlock()
    }
}

final class HookServerTests: XCTestCase {
    private let token = "tok_0123456789abcdefghij"
    private var folder = ""
    private var settings: IntegrationSettings!
    private var usage: UsageStore!
    private let sessions = SessionBoard()
    private var spend: SpendBoard!
    private var broker: PermissionBroker!
    private var linked = true
    private var asked: [PermPrompt] = []
    private let gate = NSLock()

    override func setUpWithError() throws {
        try super.setUpWithError()
        folder = shortFolder()
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        settings = IntegrationSettings(path: folder + "/integrations.txt")
        settings.setFlag(IntegrationSettings.claude, true)
        settings.setFlag(IntegrationSettings.codex, true)
        usage = UsageStore(path: folder + "/usage.json", now: Date())
        spend = SpendBoard(store: usage)
        broker = PermissionBroker(board: sessions, phoneLinked: { [unowned self] in self.linked })
        broker.asked = { [unowned self] prompt in
            self.gate.lock()
            self.asked.append(prompt)
            self.gate.unlock()
        }
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: folder)
        try super.tearDownWithError()
    }

    private func newServer(_ listener: HookListening = FakeListener(), readDeadline: TimeInterval = 0.5) -> HookServer {
        HookServer(
            listener: listener, token: { [token] in token }, broker: broker, sessions: sessions,
            limits: LimitsBoard(store: usage), spend: spend, usage: usage, settings: settings,
            readDeadline: readDeadline)
    }

    private func line(_ agent: String, _ kind: String, _ d: String, token: String? = nil) -> String {
        "{\"v\":1,\"token\":\"\(token ?? self.token)\",\"agent\":\"\(agent)\",\"kind\":\"\(kind)\",\"d\":\(d)}\n"
    }

    private func stopLine(_ session: String = "s1", token: String? = nil) -> String {
        line("claude", "session", "{\"hook_event_name\":\"SessionStart\",\"session_id\":\"\(session)\"}", token: token)
    }

    /// Serves one connection on another thread; `talk` plays the hook client on the far end.
    private func serve(_ server: HookServer, _ talk: (HookStream) -> Void) {
        let (near, far) = socketPair()
        let served = expectation(description: "served")
        DispatchQueue.global().async {
            server.serve(near)
            near.close()
            served.fulfill()
        }
        talk(far)
        wait(for: [served], timeout: 10)
    }

    private func exchange(_ server: HookServer, _ text: String) -> String? {
        var reply: String?
        serve(server) { far in
            XCTAssertTrue(far.write(Array(text.utf8)))
            reply = readReply(far)
        }
        return reply
    }

    func testARightTokenIsServedAndAnsweredOk() {
        let server = newServer()

        XCTAssertEqual(exchange(server, stopLine()), "{\"v\":1,\"ok\":true}")
        XCTAssertEqual(sessions.snapshot().count, 1)
        XCTAssertGreaterThanOrEqual(usage.lastHeard("claude"), 0)
    }

    func testUsageAndSpendReachTheirBoards() {
        let server = newServer()
        let usageD = "{\"limits\":[{\"kind\":\"five_hour\",\"percentUsed\":12,\"resetsAt\":null}],"
        let spendD =
            "\"today\":{\"tok\":1,\"cents\":2},\"week\":{\"tok\":3,\"cents\":4},\"month\":{\"tok\":5,\"cents\":6}"
        let begin = "{\"hook_event_name\":\"SessionStart\",\"session_id\":\"s1\",\"cwd\":\"/work/app\"}"
        XCTAssertNotNil(exchange(server, line("claude", "session", begin)))
        XCTAssertNotNil(
            exchange(server, line("claude", "usage", usageD + "\"context\":{\"percent\":40},\"cwd\":\"/work/app\"}")))
        XCTAssertNotNil(exchange(server, line("claude", "spend", "{" + spendD + "}")))
        XCTAssertNotNil(exchange(server, line("codex", "spend", "{" + spendD + ",\"all\":{\"tok\":7,\"cents\":8}}")))

        usage.gate.lock()
        XCTAssertEqual(usage.limits[LimitKey(agent: "claude", window: "300")]?.pct, 12)
        // The context fill is no limit window: it belongs to the session that runs in that folder.
        XCTAssertNil(usage.limits[LimitKey(agent: "claude", window: LimitWindow.context)])
        XCTAssertEqual(
            usage.spend["claude"]?.totals,
            SpendTotals(tokToday: 1, centsToday: 2, tokWeek: 3, centsWeek: 4, tokMonth: 5, centsMonth: 6))
        XCTAssertEqual(
            usage.spend["codex"]?.totals,
            SpendTotals(tokToday: 1, centsToday: 2, tokWeek: 3, centsWeek: 4, tokMonth: 5, centsMonth: 6))
        usage.gate.unlock()
        XCTAssertEqual(sessions.snapshot().map(\.contextPct), [40])
    }

    func testAnAgentIsReadyOnceWhenFirstHeard() {
        let server = newServer()
        let ready = expectation(description: "ready")
        server.agentReady = { agent in
            XCTAssertEqual(agent, "claude")
            ready.fulfill()
        }

        XCTAssertNotNil(exchange(server, stopLine()))
        XCTAssertNotNil(exchange(server, stopLine()))
        wait(for: [ready], timeout: 1)
        usage.forget("claude")
        XCTAssertEqual(usage.lastHeard("claude"), -1)
    }

    func testAWrongMissingOrOneBitOffTokenGetsNoReply() {
        let server = newServer()
        var flipped = Array(token.utf8)
        flipped[3] ^= 1
        let wrong = [
            stopLine(token: "nope"), stopLine(token: String(decoding: flipped, as: UTF8.self)),
            "{\"v\":1,\"agent\":\"claude\",\"kind\":\"session\",\"d\":{\"hook_event_name\":\"Stop\"}}\n",
        ]
        for text in wrong {
            XCTAssertNil(exchange(server, text), text)
        }
        XCTAssertTrue(sessions.snapshot().isEmpty)
        XCTAssertEqual(usage.lastHeard("claude"), -1)
    }

    func testTheTokenIsComparedWhole() {
        XCTAssertTrue(HookServer.sameToken("abc", "abc"))
        XCTAssertFalse(HookServer.sameToken("abd", "abc"))
        XCTAssertFalse(HookServer.sameToken("ab", "abc"))
        XCTAssertFalse(HookServer.sameToken("", ""))
    }

    func testALineOver64KiBGetsNoReply() {
        let server = newServer()
        let long = String(repeating: "a", count: HookProtocol.maxLineBytes + 1)

        XCTAssertNil(exchange(server, long + "\n"))
    }

    func testASilentConnectionIsClosedAtTheDeadline() {
        let server = newServer(readDeadline: 0.3)
        let started = Date()

        serve(server) { far in XCTAssertNil(readReply(far, within: 5)) }
        XCTAssertLessThan(Date().timeIntervalSince(started), 3)
    }

    func testOnlyTheFirstOfTwoRequestsOnOneConnectionIsServed() {
        let server = newServer()

        XCTAssertNotNil(exchange(server, stopLine("one") + stopLine("two")))
        XCTAssertEqual(sessions.snapshot().count, 1)
    }

    func testMalformedRequestsGetNoReply() {
        let server = newServer()
        for text in ["not json\n", "[]\n", line("grok", "session", "{\"hook_event_name\":\"Stop\"}")] {
            XCTAssertNil(exchange(server, text), text)
        }
    }

    func testAnAgentSwitchedOffIsDeferredWithoutAsking() {
        settings.setFlag(IntegrationSettings.claude, false)
        let server = newServer()
        let perm = line("claude", "perm", "{\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"ls\"}}")

        XCTAssertEqual(exchange(server, perm), "{\"v\":1,\"decision\":\"defer\"}")
        XCTAssertEqual(exchange(server, stopLine()), "{\"v\":1,\"ok\":true}")
        XCTAssertTrue(asked.isEmpty)
        XCTAssertTrue(sessions.snapshot().isEmpty)
    }

    func testAPhoneAnswerIsTheHooksDecisionAndTheCommandIsNeverLogged() throws {
        let marker = "edgepad-test-" + UUID().uuidString
        broker.asked = { [unowned self] prompt in
            DispatchQueue.global().async { self.broker.answer(id: prompt.id, allow: true) }
        }
        let server = newServer()
        let perm = line("claude", "perm", "{\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"echo \(marker)\"}}")

        XCTAssertEqual(exchange(server, perm), "{\"v\":1,\"decision\":\"allow\"}")
        let log = (try? String(contentsOfFile: Log.filePath, encoding: .utf8)) ?? ""
        XCTAssertFalse(log.contains(marker))
        XCTAssertFalse(log.contains(token))
    }

    func testAHookThatLeavesEndsItsAsk() {
        let server = newServer()
        let left = expectation(description: "gone")
        broker.gone = { _ in left.fulfill() }
        let perm = line("claude", "perm", "{\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"ls\"}}")

        serve(server) { far in
            XCTAssertTrue(far.write(Array(perm.utf8)))
            let deadline = Date().addingTimeInterval(5)
            while broker.pendingCount == 0 && Date() < deadline {
                usleep(10_000)
            }
            far.close()
            wait(for: [left], timeout: 5)
        }
        XCTAssertEqual(broker.pendingCount, 0)
    }

    func testAClientThatLeavesBeforeTheReplyIsNoError() {
        let server = newServer()

        serve(server) { far in
            XCTAssertTrue(far.write(Array(stopLine().utf8)))
            far.close()
        }
        XCTAssertEqual(sessions.snapshot().count, 1)
    }

    func testTheFirstCodexRequestRecordsTrust() {
        let server = newServer()
        let trusted = expectation(description: "trusted")
        server.agentReady = { agent in
            XCTAssertEqual(agent, "codex")
            trusted.fulfill()
        }
        let codex = line("codex", "session", "{\"hook_event_name\":\"SessionStart\",\"session_id\":\"c\"}")

        XCTAssertNotNil(exchange(server, codex))
        wait(for: [trusted], timeout: 1)
        XCTAssertTrue(settings.flag(IntegrationSettings.codexTrusted))
    }

    func testTheNinthConnectionIsClosedUnread() {
        let listener = FakeListener()
        let server = newServer(listener, readDeadline: 10)
        server.start()
        defer { server.dispose() }
        var silent: [SocketStream] = []
        for _ in 0..<HookServer.maxConnections {
            let (near, far) = socketPair()
            listener.push(near)
            silent.append(far)
        }
        let deadline = Date().addingTimeInterval(5)
        while server.activeCount < HookServer.maxConnections && Date() < deadline {
            usleep(10_000)
        }
        XCTAssertEqual(server.activeCount, HookServer.maxConnections)

        let (near, far) = socketPair()
        listener.push(near)
        XCTAssertEqual(far.read(limit: 16, until: Date().addingTimeInterval(3)), [])
        for stream in silent {
            stream.close()
        }
    }

    // ---- the socket

    func testTheListenerMakesAPrivateFolderAndSocket() throws {
        let directory = shortFolder()
        defer { try? FileManager.default.removeItem(atPath: directory) }
        let listener = try UnixHookListener(directory: directory)
        defer { listener.close() }

        var info = stat()
        XCTAssertEqual(lstat(directory, &info), 0)
        XCTAssertEqual(info.st_mode & 0o777, 0o700)
        XCTAssertEqual(lstat(listener.endpoint, &info), 0)
        XCTAssertEqual(info.st_mode & 0o777, 0o600)
        XCTAssertEqual(listener.endpoint, directory + "/hook.sock")
    }

    func testTheListenerRefusesAnOpenFolderAndALink() throws {
        let open = shortFolder()
        let link = shortFolder()
        defer {
            try? FileManager.default.removeItem(atPath: open)
            try? FileManager.default.removeItem(atPath: link)
        }
        XCTAssertEqual(mkdir(open, 0o700), 0)
        XCTAssertEqual(chmod(open, 0o755), 0)
        XCTAssertThrowsError(try UnixHookListener(directory: open))

        XCTAssertEqual(chmod(open, 0o700), 0)
        XCTAssertEqual(symlink(open, link), 0)
        XCTAssertThrowsError(try UnixHookListener(directory: link))
    }

    private func connect(_ path: String) -> SocketStream? {
        let fd = socket(AF_UNIX, SOCK_STREAM, 0)
        guard withUnixAddress(path, { Darwin.connect(fd, $0, $1) }) == 0 else {
            Darwin.close(fd)
            return nil
        }
        return SocketStream(fd: fd)
    }

    func testAPeerOfAnotherUserIsClosedBeforeAnythingIsRead() throws {
        let directory = shortFolder()
        defer { try? FileManager.default.removeItem(atPath: directory) }
        let listener = try UnixHookListener(directory: directory, peer: { _ in geteuid() + 1 })
        let accepted = expectation(description: "accept returned")
        DispatchQueue.global().async {
            XCTAssertNil(listener.accept())
            accepted.fulfill()
        }

        let client = try XCTUnwrap(connect(listener.endpoint))
        XCTAssertEqual(client.read(limit: 1, until: Date().addingTimeInterval(3)), [])
        listener.close()
        wait(for: [accepted], timeout: 5)
    }

    func testThisUsersPeerIsAccepted() throws {
        let directory = shortFolder()
        defer { try? FileManager.default.removeItem(atPath: directory) }
        let listener = try UnixHookListener(directory: directory)
        defer { listener.close() }

        let client = try XCTUnwrap(connect(listener.endpoint))
        let server = try XCTUnwrap(listener.accept())
        XCTAssertTrue(client.write([1, 2, 3]))
        XCTAssertEqual(server.read(limit: 8, until: Date().addingTimeInterval(3)), [1, 2, 3])
    }

    // ---- hook.json

    func testHookJsonIsPrivateNewEachStartRotatesAndIsDeletedOnDispose() throws {
        let file = folder + "/hook.json"
        let first = try HookEndpoint(file: file, endpoint: "/tmp/x/hook.sock")
        var info = stat()
        XCTAssertEqual(lstat(file, &info), 0)
        XCTAssertEqual(info.st_mode & 0o777, 0o600)
        let read = try XCTUnwrap(HookClient.readHookFile(file))
        XCTAssertEqual(read.endpoint, "/tmp/x/hook.sock")
        XCTAssertEqual(read.token, first.token)
        XCTAssertTrue(HookClient.isToken(first.token))

        let before = first.token
        XCTAssertTrue(first.rotate())
        XCTAssertNotEqual(first.token, before)
        XCTAssertEqual(HookClient.readHookFile(file)?.token, first.token)

        let second = try HookEndpoint(file: folder + "/other.json", endpoint: "/tmp/x/hook.sock")
        XCTAssertNotEqual(second.token, first.token)

        first.dispose()
        XCTAssertFalse(FileManager.default.fileExists(atPath: file))
        XCTAssertFalse(first.rotate())
    }

    func testTheClientRefusesAHookJsonOthersCanRead() throws {
        let file = folder + "/hook.json"
        _ = try HookEndpoint(file: file, endpoint: "/tmp/x/hook.sock")
        XCTAssertNotNil(HookClient.readHookFile(file))
        XCTAssertEqual(chmod(file, 0o644), 0)
        XCTAssertNil(HookClient.readHookFile(file))
    }

    // ---- the client

    private func runClient(_ arguments: [String], stdin: String, reply: String?) throws -> (String?, String?) {
        let file = folder + "/hook.json"
        let endpoint = try HookEndpoint(file: file, endpoint: "/tmp/x/hook.sock")
        let (near, far) = socketPair()
        var received: String?
        let answered = expectation(description: "server end")
        DispatchQueue.global().async {
            received = readReply(near)
            if let reply {
                _ = near.write(Array((reply + "\n").utf8))
            }
            answered.fulfill()
        }
        let output = HookClient.run(
            arguments, stdin: { Data(stdin.utf8) }, file: file, connect: { _ in far }, replyWait: 3)
        wait(for: [answered], timeout: 5)
        endpoint.dispose()
        return (output, received)
    }

    func testThePermClientPrintsAllowAndDenyAndNothingForDefer() throws {
        let hook = "{\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"ls\"},\"session_id\":\"s\"}"
        let (allow, sent) = try runClient(
            ["--hook", "claude", "perm"], stdin: hook, reply: "{\"v\":1,\"decision\":\"allow\"}")
        let request = try XCTUnwrap(sent.flatMap { HookProtocol.parse(Array($0.utf8)) })
        XCTAssertEqual(request.agent, "claude")
        let printed = try XCTUnwrap(allow.flatMap { try? JSONSerialization.jsonObject(with: Data($0.utf8)) })
        let decision = ((printed as? [String: Any])?["hookSpecificOutput"] as? [String: Any])?["decision"]
        XCTAssertEqual((decision as? [String: Any])?["behavior"] as? String, "allow")

        let (deny, _) = try runClient(
            ["--hook", "claude", "perm"], stdin: hook, reply: "{\"v\":1,\"decision\":\"deny\"}")
        XCTAssertTrue(deny?.contains(HookClient.denyMessage) ?? false)

        let (later, _) = try runClient(
            ["--hook", "claude", "perm"], stdin: hook, reply: "{\"v\":1,\"decision\":\"defer\"}")
        XCTAssertNil(later)
    }

    func testOtherKindsPrintNothingAndUnknownArgumentsSendNothing() throws {
        let hook = "{\"hook_event_name\":\"Stop\",\"session_id\":\"s\"}"
        let (output, sent) = try runClient(
            ["--hook", "claude", "session"], stdin: hook, reply: "{\"v\":1,\"ok\":true}")
        XCTAssertNil(output)
        XCTAssertNotNil(sent.flatMap { HookProtocol.parse(Array($0.utf8)) })

        var connected = false
        let none = HookClient.run(
            ["--hook", "grok", "perm"], stdin: { Data(hook.utf8) }, file: folder + "/hook.json",
            connect: { _ in
                connected = true
                return nil
            }, replyWait: 1)
        XCTAssertNil(none)
        XCTAssertFalse(connected)
    }
}
