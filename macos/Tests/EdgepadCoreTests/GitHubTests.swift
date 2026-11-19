import XCTest

@testable import EdgepadCore

/// The network, scripted: every request is recorded and answered by `answer`; nil is a network failure.
final class FakeHttp: HttpSender {
    struct Seen {
        let method: String
        let url: String
        let host: String
        let headers: [String: String]
        let body: String
    }

    private let lock = NSLock()
    private var requests: [Seen] = []
    private let answer: (URLRequest) -> HttpReply?

    init(_ answer: @escaping (URLRequest) -> HttpReply?) {
        self.answer = answer
    }

    /// Answers in order; a nil entry is a network failure.
    static func script(_ replies: [String?]) -> FakeHttp {
        var queue = replies
        return FakeHttp { _ in
            guard !queue.isEmpty, let json = queue.removeFirst() else { return nil }
            return json200(json)
        }
    }

    static func json200(_ json: String, headers: [String: String] = [:]) -> HttpReply {
        HttpReply(status: 200, headers: headers, body: Data(json.utf8))
    }

    var seen: [Seen] {
        lock.lock()
        defer { lock.unlock() }
        return requests
    }

    func send(_ request: URLRequest, maxBytes: Int, what: String) async -> HttpReply? {
        let url = request.url?.absoluteString ?? ""
        let seen = Seen(
            method: request.httpMethod ?? "GET", url: url, host: request.url?.host ?? "",
            headers: request.allHTTPHeaderFields ?? [:],
            body: String(decoding: request.httpBody ?? Data(), as: UTF8.self))
        lock.lock()
        requests.append(seen)
        lock.unlock()
        guard let reply = answer(request), reply.body.count <= maxBytes else { return nil }
        return reply
    }
}

/// A secret store in memory.
final class MemorySecrets: SecretStore {
    private let lock = NSLock()
    private var values: [String: String] = [:]
    var refuse = false

    func get(_ name: String) -> String? {
        lock.lock()
        defer { lock.unlock() }
        return values[name]
    }

    func set(_ name: String, _ value: String) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        if refuse { return false }
        values[name] = value
        return true
    }

    func delete(_ name: String) {
        lock.lock()
        defer { lock.unlock() }
        values[name] = nil
    }
}

/// A clock tests move by hand.
final class TestClock {
    private let lock = NSLock()
    private var current = Date(timeIntervalSince1970: 1_795_089_600)

    var now: Date {
        lock.lock()
        defer { lock.unlock() }
        return current
    }

    func advance(_ seconds: TimeInterval) {
        lock.lock()
        defer { lock.unlock() }
        current += seconds
    }
}

final class GitHubSignInTests: XCTestCase {
    private let client = "Ov23liTestClient0001"
    private let token = "gho_SecretTokenValue1234567890abcdef"
    private let code =
        #"{"device_code":"3584d83530557fdd1f46af8289938c8ef79f9dc5","user_code":"WDJB-MJHT","#
        + #""verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}"#
    private let pending = #"{"error":"authorization_pending"}"#
    private let slowDown = #"{"error":"slow_down","interval":10}"#
    private let granted = #"{"access_token":"gho_SecretTokenValue1234567890abcdef","token_type":"bearer"}"#

    private let clock = TestClock()
    private let secrets = MemorySecrets()
    private var waits: [Int] = []

    private func newSignIn(_ http: FakeHttp, clientId: String? = nil) -> GitHubSignIn {
        GitHubSignIn(
            store: secrets, clientId: clientId ?? client, http: http, clock: { [clock] in clock.now },
            delay: { [weak self] seconds in
                self?.waits.append(Int(seconds))
                self?.clock.advance(seconds)
            })
    }

    private func forever(_ http: FakeHttp) -> GitHubSignIn {
        GitHubSignIn(
            store: secrets, clientId: client, http: http, clock: { [clock] in clock.now },
            delay: { _ in try await Task.sleep(nanoseconds: 3_600_000_000_000) })
    }

    func testThePlaceholderClientIdReportsNotConfiguredWithoutARequest() async {
        let http = FakeHttp { _ in FakeHttp.json200(self.code) }
        let start = await newSignIn(http, clientId: GitHubApp.clientId).start(includePrivate: false)

        XCTAssertEqual(start.state, .notConfigured)
        XCTAssertTrue(http.seen.isEmpty)
        XCTAssertFalse(GitHubApp.isConfigured(GitHubApp.clientId))
    }

    func testStartAsksGitHubForACodeAtTheFixedAddress() async {
        let http = FakeHttp.script([code])
        let start = await newSignIn(http).start(includePrivate: false)

        XCTAssertEqual(
            start,
            DeviceCode(state: .started, userCode: "WDJB-MJHT", page: "https://github.com/login/device", expiresIn: 900))
        XCTAssertEqual(http.seen.count, 1)
        XCTAssertEqual(http.seen.first?.method, "POST")
        XCTAssertEqual(http.seen.first?.url, "https://github.com/login/device/code")
        XCTAssertEqual(http.seen.first?.body, "client_id=Ov23liTestClient0001&scope=public_repo%20notifications")
        XCTAssertEqual(http.seen.first?.headers["Accept"], "application/json")
    }

    func testPrivateRepositoriesAskForTheRepoScope() async {
        let http = FakeHttp.script([code])
        _ = await newSignIn(http).start(includePrivate: true)
        XCTAssertEqual(http.seen.first?.body, "client_id=Ov23liTestClient0001&scope=repo%20notifications")
    }

    func testAVerificationUriFromTheServerIsIgnored() async {
        let evil = code.replacingOccurrences(of: "https://github.com/login/device", with: "https://evil.example/login")
        let start = await newSignIn(FakeHttp.script([evil])).start(includePrivate: false)
        XCTAssertEqual(start.page, "https://github.com/login/device")
    }

    func testAReplyOutsideTheDocumentedRangesFails() async {
        let escape = String(UnicodeScalar(92))
        let cases = [
            (#""user_code":"WDJB-MJHT""#, #""user_code":"wdjb-mjht""#),
            (#""user_code":"WDJB-MJHT""#, #""user_code":"WDJB MJHT""#),
            (#""user_code":"WDJB-MJHT""#, #""user_code":"WDJ""#),
            (#""user_code":"WDJB-MJHT""#, #""user_code":"ABCDEFGHIJKLMNOPQRSTU""#),
            (#""user_code":"WDJB-MJHT""#, #""user_code":"WDJB-MJHT"# + escape + #"n""#),
            (#""user_code":"WDJB-MJHT""#, #""user_code":"WDJB"# + escape + #"u202E-MJHT""#),
            (#""interval":5"#, #""interval":0"#), (#""interval":5"#, #""interval":61"#),
            (#""interval":5"#, #""interval":"5""#), (#""expires_in":900"#, #""expires_in":59"#),
            (#""expires_in":900"#, #""expires_in":1801"#), (#""expires_in":900"#, #""expires_in":null"#),
            (#""device_code":"3584d83530557fdd1f46af8289938c8ef79f9dc5""#, #""device_code":"""#),
            (#""device_code":"3584d83530557fdd1f46af8289938c8ef79f9dc5""#, #""device_code":"a b""#),
        ]
        for (from, to) in cases {
            let reply = code.replacingOccurrences(of: from, with: to)
            let start = await newSignIn(FakeHttp.script([reply])).start(includePrivate: false)
            XCTAssertEqual(start.state, .failed, to)
        }
    }

    func testAMissingIntervalMeansFiveSeconds() async {
        let noInterval = code.replacingOccurrences(of: #","interval":5"#, with: "")
        let signIn = newSignIn(FakeHttp.script([noInterval, granted]))
        _ = await signIn.start(includePrivate: false)
        let state = await signIn.wait()
        XCTAssertEqual(state, .signedIn)
        XCTAssertEqual(waits, [5])
    }

    func testARedirectOrServerErrorAtStartFails() async {
        for status in [302, 502] {
            let http = FakeHttp { _ in HttpReply(status: status) }
            let start = await newSignIn(http).start(includePrivate: false)
            XCTAssertEqual(start.state, .failed)
        }
        let huge = FakeHttp.script([String(repeating: " ", count: GitHubSignIn.maxBytes + 1)])
        let start = await newSignIn(huge).start(includePrivate: false)
        XCTAssertEqual(start.state, .failed)
    }

    func testPendingThenGrantedSavesTheTokenAndPollsAtTheFixedAddress() async {
        let http = FakeHttp.script([code, pending, pending, granted])
        let signIn = newSignIn(http)

        _ = await signIn.start(includePrivate: false)
        let state = await signIn.wait()

        XCTAssertEqual(state, .signedIn)
        XCTAssertEqual(secrets.get(GitHubApp.tokenName), token)
        XCTAssertTrue(signIn.isSignedIn)
        XCTAssertEqual(http.seen.count, 4)
        let form =
            "client_id=Ov23liTestClient0001&device_code=3584d83530557fdd1f46af8289938c8ef79f9dc5"
            + "&grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code"
        for seen in http.seen.dropFirst() {
            XCTAssertEqual(seen.url, "https://github.com/login/oauth/access_token")
            XCTAssertEqual(seen.body, form)
        }
        XCTAssertTrue(http.seen.allSatisfy { $0.host == "github.com" })
        XCTAssertEqual(waits, [5, 5, 5])
    }

    func testSlowDownAddsFiveSecondsToEveryLaterWait() async {
        let signIn = newSignIn(FakeHttp.script([code, slowDown, pending, granted]))
        _ = await signIn.start(includePrivate: false)
        let state = await signIn.wait()
        XCTAssertEqual(state, .signedIn)
        XCTAssertEqual(waits, [5, 10, 10])
    }

    func testEveryEndingIsReportedAndNothingIsSaved() async {
        let cases: [(String, SignInState)] = [
            (#"{"error":"expired_token"}"#, .expired), (#"{"error":"access_denied"}"#, .denied),
            (#"{"error":"incorrect_device_code"}"#, .failed), (#"{"error":"device_flow_disabled"}"#, .failed),
            (#"{"access_token":"gho_bad token"}"#, .failed), ("[]", .failed),
        ]
        for (answer, expected) in cases {
            let signIn = newSignIn(FakeHttp.script([code, answer]))
            _ = await signIn.start(includePrivate: false)
            let state = await signIn.wait()
            XCTAssertEqual(state, expected, answer)
            XCTAssertNil(secrets.get(GitHubApp.tokenName))
        }
    }

    func testAFailedPollIsTriedAgain() async {
        let signIn = newSignIn(FakeHttp.script([code, nil, nil, granted]))
        _ = await signIn.start(includePrivate: false)
        let state = await signIn.wait()
        XCTAssertEqual(state, .signedIn)
    }

    func testPollingStopsWhenTheCodeRunsOut() async {
        let http = FakeHttp { request in
            FakeHttp.json200(request.url?.path == "/login/device/code" ? self.code : self.pending)
        }
        let signIn = newSignIn(http)
        _ = await signIn.start(includePrivate: false)
        let state = await signIn.wait()
        XCTAssertEqual(state, .expired)
        XCTAssertEqual(http.seen.count, 900 / 5)
    }

    func testCancelEndsTheWait() async {
        let signIn = forever(FakeHttp.script([code]))
        _ = await signIn.start(includePrivate: false)
        let waiting = Task { await signIn.wait() }
        try? await Task.sleep(nanoseconds: 50_000_000)
        signIn.cancel()
        let state = await waiting.value
        XCTAssertEqual(state, .cancelled)
    }

    func testCancellingTheTaskEndsTheWait() async {
        let signIn = forever(FakeHttp.script([code]))
        _ = await signIn.start(includePrivate: false)
        let waiting = Task { await signIn.wait() }
        try? await Task.sleep(nanoseconds: 50_000_000)
        waiting.cancel()
        let state = await waiting.value
        XCTAssertEqual(state, .cancelled)
    }

    func testANewSignInCancelsTheOldOne() async {
        let signIn = forever(FakeHttp.script([code, code]))
        _ = await signIn.start(includePrivate: false)
        let waiting = Task { await signIn.wait() }
        try? await Task.sleep(nanoseconds: 50_000_000)
        _ = await signIn.start(includePrivate: false)
        let state = await waiting.value
        XCTAssertEqual(state, .cancelled)
    }

    func testWaitingWithoutAStartFails() async {
        let state = await newSignIn(FakeHttp.script([])).wait()
        XCTAssertEqual(state, .failed)
    }

    func testATokenThatCannotBeSavedIsAFailure() async {
        secrets.refuse = true
        let signIn = newSignIn(FakeHttp.script([code, granted]))
        _ = await signIn.start(includePrivate: false)
        let state = await signIn.wait()
        XCTAssertEqual(state, .failed)
        XCTAssertFalse(signIn.isSignedIn)
    }

    func testSignOutForgetsTheToken() {
        _ = secrets.set(GitHubApp.tokenName, token)
        let signIn = newSignIn(FakeHttp.script([]))
        XCTAssertTrue(signIn.isSignedIn)
        signIn.signOut()
        XCTAssertNil(secrets.get(GitHubApp.tokenName))
        XCTAssertFalse(signIn.isSignedIn)
    }

    func testTheTokenNeverReachesTheLog() async throws {
        let signIn = newSignIn(FakeHttp.script([code, nil, granted]))
        _ = await signIn.start(includePrivate: false)
        _ = await signIn.wait()
        let log = (try? String(contentsOfFile: Log.filePath, encoding: .utf8)) ?? ""
        XCTAssertFalse(log.contains(token))
        XCTAssertFalse(log.contains("3584d83530557fdd1f46af8289938c8ef79f9dc5"))
    }
}

final class GitHubApiTests: XCTestCase {
    private let token = "gho_SecretTokenValue1234567890abcdef"
    private let twoUnread = #"[{"id":"1","unread":true},{"id":"2","unread":true}]"#
    private let clock = TestClock()
    private let secrets = MemorySecrets()

    override func setUp() {
        _ = secrets.set(GitHubApp.tokenName, token)
    }

    private func newApi(_ http: FakeHttp) -> GitHubApi {
        GitHubApi(store: secrets, http: http, clock: { [clock] in clock.now })
    }

    private func serve(_ pulls: String, _ unread: String) -> FakeHttp {
        FakeHttp { request in FakeHttp.json200(request.url?.path == "/graphql" ? pulls : unread) }
    }

    func testARefreshSendsTheConstantQueryThenAsksForNotifications() async throws {
        let http = serve(GitHubParseTests.normal, twoUnread)
        let api = newApi(http)

        let result = await api.refresh()

        XCTAssertEqual(result, GitHubRefreshResult(result: .ok, changed: true))
        XCTAssertEqual(http.seen.count, 2)
        let graphql = http.seen[0]
        XCTAssertEqual(graphql.method, "POST")
        XCTAssertEqual(graphql.url, "https://api.github.com/graphql")
        let body = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(graphql.body.utf8)) as? [String: Any])
        XCTAssertEqual(body["query"] as? String, GitHubApi.query)
        XCTAssertEqual(body.count, 1)
        XCTAssertEqual(graphql.headers["Content-Type"], "application/json")
        XCTAssertEqual(http.seen[1].method, "GET")
        XCTAssertEqual(http.seen[1].url, "https://api.github.com/notifications?per_page=50")
        for seen in http.seen {
            XCTAssertEqual(seen.host, "api.github.com")
            XCTAssertEqual(seen.headers["Authorization"], "Bearer " + token)
            XCTAssertEqual(seen.headers["Accept"], "application/vnd.github+json")
            XCTAssertEqual(seen.headers["X-GitHub-Api-Version"], "2022-11-28")
            XCTAssertEqual(seen.headers["User-Agent"], "Edgepad")
        }
        XCTAssertEqual(
            api.lines,
            [
                GitHubLine(section: "N", repo: "", number: 2, title: "", draft: false, ci: "none"),
                GitHubLine(
                    section: "R", repo: "octo/edgepad", number: 12, title: "Fix the pairing timeout", draft: false,
                    ci: "ok"),
                GitHubLine(
                    section: "R", repo: "octo/other.repo", number: 7, title: "Draft idea", draft: true, ci: "none"),
                GitHubLine(section: "M", repo: "me/my_repo-2", number: 3, title: "Mine", draft: false, ci: "fail"),
            ])
    }

    func testWithoutATokenNobodyIsAsked() async {
        secrets.delete(GitHubApp.tokenName)
        let http = serve(GitHubParseTests.normal, twoUnread)
        let result = await newApi(http).refresh()
        XCTAssertEqual(result.result, .notSignedIn)
        XCTAssertTrue(http.seen.isEmpty)
    }

    func testATokenThatCouldInjectAHeaderIsNeverSent() async {
        _ = secrets.set(GitHubApp.tokenName, "gho_x\r\nX-Evil: 1")
        let http = serve(GitHubParseTests.normal, twoUnread)
        let result = await newApi(http).refresh()
        XCTAssertEqual(result.result, .notSignedIn)
        XCTAssertTrue(http.seen.isEmpty)
    }

    func testTheETagIsSentBackAndANotModifiedKeepsTheCount() async {
        var first = true
        let http = FakeHttp { request in
            if request.url?.path == "/graphql" { return FakeHttp.json200(GitHubParseTests.normal) }
            if first {
                first = false
                return FakeHttp.json200(self.twoUnread, headers: ["ETag": #""abc123""#])
            }
            return HttpReply(status: 304)
        }
        let api = newApi(http)

        _ = await api.refresh()
        let again = await api.refresh()

        XCTAssertEqual(again, GitHubRefreshResult(result: .ok, changed: false))
        XCTAssertNil(http.seen[1].headers["If-None-Match"])
        XCTAssertEqual(http.seen[3].headers["If-None-Match"], #""abc123""#)
        XCTAssertEqual(api.lines.first?.number, 2)
    }

    func testARefusedTokenIsDeletedAndTheScreenEmptied() async {
        var refuse = false
        let http = FakeHttp { request in
            if refuse { return HttpReply(status: 401) }
            return FakeHttp.json200(request.url?.path == "/graphql" ? GitHubParseTests.normal : self.twoUnread)
        }
        let api = newApi(http)
        _ = await api.refresh()
        refuse = true

        let result = await api.refresh()

        XCTAssertEqual(result, GitHubRefreshResult(result: .noPermission, changed: true))
        XCTAssertNil(secrets.get(GitHubApp.tokenName))
        XCTAssertTrue(api.lines.isEmpty)
    }

    func testARateLimitKeepsTheLastResultAndWaitsForTheReset() async {
        var limited = false
        let reset = String(Int64(clock.now.timeIntervalSince1970) + 600)
        let http = FakeHttp { request in
            if limited {
                return HttpReply(status: 403, headers: ["x-ratelimit-remaining": "0", "x-ratelimit-reset": reset])
            }
            return FakeHttp.json200(request.url?.path == "/graphql" ? GitHubParseTests.normal : self.twoUnread)
        }
        let api = newApi(http)

        _ = await api.refresh()
        let kept = api.lines
        limited = true
        let first = await api.refresh()
        XCTAssertEqual(first.result, .rateLimited)
        XCTAssertEqual(api.lines, kept)
        XCTAssertEqual(http.seen.count, 3)

        clock.advance(9 * 60)
        let second = await api.refresh()
        XCTAssertEqual(second.result, .rateLimited)
        XCTAssertEqual(http.seen.count, 3)

        limited = false
        clock.advance(2 * 60)
        let third = await api.refresh()
        XCTAssertEqual(third.result, .ok)
        XCTAssertEqual(http.seen.count, 5)
    }

    func testARetryAfterIsHonouredButNeverPastAnHour() async {
        let http = FakeHttp { _ in HttpReply(status: 429, headers: ["Retry-After": "999999"]) }
        let api = newApi(http)

        let first = await api.refresh()
        XCTAssertEqual(first.result, .rateLimited)
        clock.advance(59 * 60)
        let second = await api.refresh()
        XCTAssertEqual(second.result, .rateLimited)
        XCTAssertEqual(http.seen.count, 1)
        clock.advance(2 * 60)
        _ = await api.refresh()
        XCTAssertEqual(http.seen.count, 2)
    }

    func testAForbiddenWithoutRateLimitHeadersIsAFailureAndNoBackOff() async {
        let http = FakeHttp { _ in HttpReply(status: 403) }
        let api = newApi(http)
        let first = await api.refresh()
        XCTAssertEqual(first.result, .failed)
        _ = await api.refresh()
        XCTAssertEqual(http.seen.count, 4)
    }

    func testAnyOtherErrorKeepsTheLastGoodResult() async {
        for status in [500, 302, 404] {
            var broken = false
            let http = FakeHttp { request in
                if broken { return HttpReply(status: status) }
                return FakeHttp.json200(request.url?.path == "/graphql" ? GitHubParseTests.normal : self.twoUnread)
            }
            let api = newApi(http)
            _ = await api.refresh()
            let kept = api.lines
            broken = true
            let result = await api.refresh()
            XCTAssertEqual(result, GitHubRefreshResult(result: .failed, changed: false))
            XCTAssertEqual(api.lines, kept)
        }
    }

    func testAReplyOverOneMibIsRefused() async {
        let huge = String(repeating: " ", count: GitHubApi.maxBytes + 1)
        let result = await newApi(serve(huge, twoUnread)).refresh()
        XCTAssertEqual(result.result, .failed)
    }

    func testTheLoginIsReadForDisplayAndARefusalDeletesTheToken() async {
        let login = await newApi(FakeHttp { _ in FakeHttp.json200(#"{"login":"octo-cat"}"#) }).login()
        XCTAssertEqual(login, "octo-cat")

        let refused = await newApi(FakeHttp { _ in HttpReply(status: 401) }).login()
        XCTAssertNil(refused)
        XCTAssertNil(secrets.get(GitHubApp.tokenName))
    }

    func testRefreshRunsOnlyWhileWatchedAndAMinuteApart() {
        let base = Date(timeIntervalSince1970: 1_000_000)
        let cases: [(Int?, Int, Bool, Bool)] = [
            (nil, 0, true, true), (nil, 0, false, false), (0, 59, true, false), (0, 60, true, true),
            (0, 600, false, false), (0, -5, true, false),
        ]
        for (last, now, watched, expected) in cases {
            let lastDate = last.map { base + TimeInterval($0) }
            XCTAssertEqual(
                GitHubRefresh.shouldRun(last: lastDate, now: base + TimeInterval(now), watchBit4: watched), expected)
        }
    }
}

final class GitHubParseTests: XCTestCase {
    static let normal = """
        {"data":{
          "review":{"nodes":[
            {"number":12,"title":"Fix the pairing timeout","isDraft":false,"repository":{"nameWithOwner":"octo/edgepad"},
             "commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"SUCCESS"}}}]}},
            {"number":7,"title":"Draft idea","isDraft":true,"repository":{"nameWithOwner":"octo/other.repo"},
             "commits":{"nodes":[{"commit":{"statusCheckRollup":null}}]}}
          ]},
          "mine":{"nodes":[
            {"number":3,"title":"Mine","isDraft":false,"repository":{"nameWithOwner":"me/my_repo-2"},
             "commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"FAILURE"}}}]}},
            {}
          ]}
        }}
        """

    private func mine(_ node: String) -> Data {
        Data((#"{"data":{"review":{"nodes":[]},"mine":{"nodes":["# + node + "]}}}").utf8)
    }

    func testAnEmptyReplyGivesNoLines() {
        XCTAssertEqual(GitHubParse.pulls(Data(#"{"data":{"review":{"nodes":[]},"mine":{"nodes":[]}}}"#.utf8)), [])
    }

    func testAReplyWithoutDataIsNil() {
        for body in [#"{"errors":[{"message":"Bad credentials"}]}"#, #"{"data":null}"#, "[]", "not json", ""] {
            XCTAssertNil(GitHubParse.pulls(Data(body.utf8)), body)
        }
    }

    func testHostileTitlesAreCleanedAndCut() throws {
        let escape = String(UnicodeScalar(92))
        let title =
            "Tab" + escape + "there" + escape + "u001b[31mred" + escape + "u001b[0m new" + escape + "nline " + escape
            + "u202eevil" + String(repeating: "x", count: 100)
        let node = #"{"number":1,"title":""# + title + #"","isDraft":false,"repository":{"nameWithOwner":"a/b"}}"#

        let lines = try XCTUnwrap(GitHubParse.pulls(mine(node)))

        XCTAssertEqual(lines.count, 1)
        let clean = try XCTUnwrap(lines.first).title
        let hidden = clean.unicodeScalars.contains { $0.properties.generalCategory == .control || $0.value == 0x202E }
        XCTAssertFalse(hidden)
        XCTAssertTrue(clean.hasPrefix("Tab here [31mred [0m new line evil"), clean)
        XCTAssertEqual(clean.unicodeScalars.count, GitHubParse.maxTitle)
        XCTAssertEqual(lines.first?.ci, "none")
    }

    func testARowWithAnOddRepositoryIsDropped() {
        for repo in ["octo/my repo", "octo", "a/b/c", "octo/rep@", "/repo"] {
            let node = #"{"number":1,"title":"x","repository":{"nameWithOwner":""# + repo + #""}}"#
            XCTAssertEqual(GitHubParse.pulls(mine(node)), [], repo)
        }
    }

    func testARowMissingItsNumberOrRepositoryIsDropped() {
        let nodes = [
            #"{"number":0,"title":"x","repository":{"nameWithOwner":"a/b"}}"#,
            #"{"number":-4,"title":"x","repository":{"nameWithOwner":"a/b"}}"#,
            #"{"number":"5","title":"x","repository":{"nameWithOwner":"a/b"}}"#,
            #"{"title":"x","repository":{"nameWithOwner":"a/b"}}"#,
            #"{"number":5,"title":"x","repository":null}"#, "17",
        ]
        for node in nodes {
            XCTAssertEqual(GitHubParse.pulls(mine(node)), [], node)
        }
    }

    func testAtMostTenRowsPerSectionAreKept() {
        let node = #"{"number":1,"title":"x","repository":{"nameWithOwner":"a/b"}}"#
        let lines = GitHubParse.pulls(mine(Array(repeating: node, count: 15).joined(separator: ",")))
        XCTAssertEqual(lines?.count, GitHubBlob.maxPerSection)
    }

    func testAReplyNestedPastTheLimitIsRefused() {
        let deep = String(repeating: "[", count: 20) + String(repeating: "]", count: 20)
        XCTAssertNil(GitHubParse.pulls(Data((#"{"data":"# + deep + "}").utf8)))
    }

    func testRollupStatesMapToCiTokens() {
        let cases: [(String?, String)] = [
            ("SUCCESS", "ok"), ("FAILURE", "fail"), ("ERROR", "fail"), ("PENDING", "pending"), ("EXPECTED", "pending"),
            ("success", "none"), (nil, "none"),
        ]
        for (state, expected) in cases {
            XCTAssertEqual(GitHubParse.ci(state), expected)
        }
    }

    func testTheUnreadCountIsTheListLengthCappedAtFifty() {
        XCTAssertEqual(GitHubParse.unread(Data("[{},{},5]".utf8)), 2)
        let eighty = "[" + Array(repeating: "{}", count: 80).joined(separator: ",") + "]"
        XCTAssertEqual(GitHubParse.unread(Data(eighty.utf8)), 50)
        XCTAssertNil(GitHubParse.unread(Data("{}".utf8)))
    }

    func testTheLoginMustLookLikeOne() {
        let cases: [(String, String?)] = [
            (#"{"login":"octo-cat"}"#, "octo-cat"), (#"{"login":"evil name"}"#, nil), (#"{"login":""}"#, nil),
            (#"{"login":"a234567890123456789012345678901234567890"}"#, nil), (#"{"id":1}"#, nil),
        ]
        for (body, expected) in cases {
            XCTAssertEqual(GitHubParse.login(Data(body.utf8)), expected, body)
        }
    }
}
