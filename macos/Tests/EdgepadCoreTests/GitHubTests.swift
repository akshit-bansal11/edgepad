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
        lock.withLock { requests.append(seen) }
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
    private let token = "ghu_SecretTokenValue1234567890abcdef"
    private let code =
        #"{"device_code":"3584d83530557fdd1f46af8289938c8ef79f9dc5","user_code":"WDJB-MJHT","#
        + #""verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}"#
    private let pending = #"{"error":"authorization_pending"}"#
    private let slowDown = #"{"error":"slow_down","interval":10}"#
    private let granted = #"{"access_token":"ghu_SecretTokenValue1234567890abcdef","token_type":"bearer"}"#

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

    func testAnEmptyClientIdReportsNotConfiguredWithoutARequest() async {
        let http = FakeHttp { _ in FakeHttp.json200(self.code) }
        let start = await newSignIn(http, clientId: "").start()

        XCTAssertEqual(start.state, .notConfigured)
        XCTAssertTrue(http.seen.isEmpty)
        XCTAssertFalse(GitHubApp.isConfigured(""))
        XCTAssertTrue(GitHubApp.isConfigured(GitHubApp.clientId))
    }

    func testStartAsksGitHubForACodeAtTheFixedAddress() async {
        let http = FakeHttp.script([code])
        let start = await newSignIn(http).start()

        XCTAssertEqual(
            start,
            DeviceCode(state: .started, userCode: "WDJB-MJHT", page: "https://github.com/login/device", expiresIn: 900))
        XCTAssertEqual(http.seen.count, 1)
        XCTAssertEqual(http.seen.first?.method, "POST")
        XCTAssertEqual(http.seen.first?.url, "https://github.com/login/device/code")
        XCTAssertEqual(http.seen.first?.body, "client_id=Ov23liTestClient0001")
        XCTAssertEqual(http.seen.first?.headers["Accept"], "application/json")
    }

    func testTheAppIsTheReadOnlyGitHubApp() {
        XCTAssertEqual(GitHubApp.clientId, "Iv23lie6SY6AFlHwufmK")
        XCTAssertEqual(GitHubApp.installUrl, "https://github.com/apps/edgepad-dashboard/installations/new")
    }

    func testAVerificationUriFromTheServerIsIgnored() async {
        let evil = code.replacingOccurrences(of: "https://github.com/login/device", with: "https://evil.example/login")
        let start = await newSignIn(FakeHttp.script([evil])).start()
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
            let start = await newSignIn(FakeHttp.script([reply])).start()
            XCTAssertEqual(start.state, .failed, to)
        }
    }

    func testAMissingIntervalMeansFiveSeconds() async {
        let noInterval = code.replacingOccurrences(of: #","interval":5"#, with: "")
        let signIn = newSignIn(FakeHttp.script([noInterval, granted]))
        _ = await signIn.start()
        let state = await signIn.wait()
        XCTAssertEqual(state, .signedIn)
        XCTAssertEqual(waits, [5])
    }

    func testARedirectOrServerErrorAtStartFails() async {
        for status in [302, 502] {
            let http = FakeHttp { _ in HttpReply(status: status) }
            let start = await newSignIn(http).start()
            XCTAssertEqual(start.state, .failed)
        }
        let huge = FakeHttp.script([String(repeating: " ", count: GitHubSignIn.maxBytes + 1)])
        let start = await newSignIn(huge).start()
        XCTAssertEqual(start.state, .failed)
    }

    func testPendingThenGrantedSavesTheTokenAndPollsAtTheFixedAddress() async {
        let http = FakeHttp.script([code, pending, pending, granted])
        let signIn = newSignIn(http)

        _ = await signIn.start()
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
        _ = await signIn.start()
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
            _ = await signIn.start()
            let state = await signIn.wait()
            XCTAssertEqual(state, expected, answer)
            XCTAssertNil(secrets.get(GitHubApp.tokenName))
        }
    }

    func testAFailedPollIsTriedAgain() async {
        let signIn = newSignIn(FakeHttp.script([code, nil, nil, granted]))
        _ = await signIn.start()
        let state = await signIn.wait()
        XCTAssertEqual(state, .signedIn)
    }

    func testPollingStopsWhenTheCodeRunsOut() async {
        let http = FakeHttp { request in
            FakeHttp.json200(request.url?.path == "/login/device/code" ? self.code : self.pending)
        }
        let signIn = newSignIn(http)
        _ = await signIn.start()
        let state = await signIn.wait()
        XCTAssertEqual(state, .expired)
        XCTAssertEqual(http.seen.count, 900 / 5)
    }

    func testCancelEndsTheWait() async {
        let signIn = forever(FakeHttp.script([code]))
        _ = await signIn.start()
        let waiting = Task { await signIn.wait() }
        try? await Task.sleep(nanoseconds: 50_000_000)
        signIn.cancel()
        let state = await waiting.value
        XCTAssertEqual(state, .cancelled)
    }

    func testCancellingTheTaskEndsTheWait() async {
        let signIn = forever(FakeHttp.script([code]))
        _ = await signIn.start()
        let waiting = Task { await signIn.wait() }
        try? await Task.sleep(nanoseconds: 50_000_000)
        waiting.cancel()
        let state = await waiting.value
        XCTAssertEqual(state, .cancelled)
    }

    func testANewSignInCancelsTheOldOne() async {
        let signIn = forever(FakeHttp.script([code, code]))
        _ = await signIn.start()
        let waiting = Task { await signIn.wait() }
        try? await Task.sleep(nanoseconds: 50_000_000)
        _ = await signIn.start()
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
        _ = await signIn.start()
        let state = await signIn.wait()
        XCTAssertEqual(state, .failed)
        XCTAssertFalse(signIn.isSignedIn)
    }

    func testAnExpiringTokenIsSavedWithItsRefreshTokenExpiryAndClientId() async {
        let expiring =
            #"{"access_token":"ghu_SecretTokenValue1234567890abcdef","expires_in":28800,"#
            + #""refresh_token":"ghr_RefreshValue1234567890abcdef","refresh_token_expires_in":15897600,"scope":""}"#
        let signIn = newSignIn(FakeHttp.script([code, expiring]))
        _ = await signIn.start()
        let state = await signIn.wait()

        XCTAssertEqual(state, .signedIn)
        XCTAssertEqual(secrets.get(GitHubApp.tokenName), "ghu_SecretTokenValue1234567890abcdef")
        XCTAssertEqual(secrets.get(GitHubApp.refreshName), "ghr_RefreshValue1234567890abcdef")
        XCTAssertEqual(secrets.get(GitHubApp.expiresName), String(Int64(clock.now.timeIntervalSince1970) + 28800))
        XCTAssertEqual(secrets.get(GitHubApp.clientName), client)
    }

    func testHalfOfAnExpiringPairOrAnOutOfRangeLifeIsAFailure() async {
        let answers = [
            #"{"access_token":"ghu_SecretTokenValue1234567890abcdef","expires_in":28800}"#,
            #"{"access_token":"ghu_SecretTokenValue1234567890abcdef","refresh_token":"ghr_Value123"}"#,
            #"{"access_token":"ghu_SecretTokenValue1234567890abcdef","refresh_token":"ghr_bad value","expires_in":28800}"#,
            #"{"access_token":"ghu_SecretTokenValue1234567890abcdef","refresh_token":"ghr_Value123","expires_in":0}"#,
        ]
        for answer in answers {
            let signIn = newSignIn(FakeHttp.script([code, answer]))
            _ = await signIn.start()
            let state = await signIn.wait()
            XCTAssertEqual(state, .failed, answer)
            XCTAssertNil(secrets.get(GitHubApp.tokenName))
        }
    }

    func testATokenFromTheOldOAuthAppIsDeletedOnTheFirstLookAndTheNoteStaysUntilANewSignIn() async {
        _ = secrets.set(GitHubApp.tokenName, "gho_OldOAuthAppToken1234567890")
        let signIn = newSignIn(FakeHttp.script([code, granted]))

        XCTAssertFalse(signIn.isSignedIn)
        XCTAssertNil(secrets.get(GitHubApp.tokenName))
        XCTAssertTrue(signIn.needsReSignIn)

        _ = await signIn.start()
        _ = await signIn.wait()
        XCTAssertTrue(signIn.isSignedIn)
        XCTAssertFalse(signIn.needsReSignIn)
    }

    func testATokenFromThisAppIsKept() async {
        let signIn = newSignIn(FakeHttp.script([code, granted]))
        _ = await signIn.start()
        _ = await signIn.wait()
        let again = newSignIn(FakeHttp.script([]))
        XCTAssertTrue(again.isSignedIn)
        XCTAssertFalse(again.needsReSignIn)
    }

    func testSignOutForgetsTheToken() {
        _ = secrets.set(GitHubApp.tokenName, token)
        _ = secrets.set(GitHubApp.clientName, client)
        let signIn = newSignIn(FakeHttp.script([]))
        XCTAssertTrue(signIn.isSignedIn)
        signIn.signOut()
        XCTAssertNil(secrets.get(GitHubApp.tokenName))
        XCTAssertFalse(signIn.isSignedIn)
    }

    func testTheTokenNeverReachesTheLog() async throws {
        let signIn = newSignIn(FakeHttp.script([code, nil, granted]))
        _ = await signIn.start()
        _ = await signIn.wait()
        let log = (try? String(contentsOfFile: Log.filePath, encoding: .utf8)) ?? ""
        XCTAssertFalse(log.contains(token))
        XCTAssertFalse(log.contains("3584d83530557fdd1f46af8289938c8ef79f9dc5"))
    }
}


final class GitHubApiTests: XCTestCase {
    private let token = "ghu_SecretTokenValue1234567890abcdef"
    private let refreshToken = "ghr_RefreshTokenValue1234567890abcdef"
    private let newToken = "ghu_RenewedTokenValue1234567890abcdef"
    private let newRefresh = "ghr_RenewedRefreshValue1234567890abcdef"
    private let renewed =
        #"{"access_token":"ghu_RenewedTokenValue1234567890abcdef","expires_in":28800,"#
        + #""refresh_token":"ghr_RenewedRefreshValue1234567890abcdef","refresh_token_expires_in":15897600,"#
        + #""scope":"","token_type":"bearer"}"#
    private let clock = TestClock()
    private let secrets = MemorySecrets()

    override func setUp() {
        _ = secrets.set(GitHubApp.tokenName, token)
        _ = secrets.set(GitHubApp.clientName, GitHubApp.clientId)
    }

    private func newApi(_ http: FakeHttp) -> GitHubApi {
        GitHubApi(store: secrets, http: http, clock: { [clock] in clock.now })
    }

    private func serve(_ pulls: String) -> FakeHttp {
        FakeHttp { request in
            guard request.url?.path == "/graphql" else { return nil }
            return FakeHttp.json200(pulls)
        }
    }

    /// The refresh answers with `renewal`, the query with the normal reply.
    private func routes(_ renewal: String) -> FakeHttp {
        FakeHttp { request in
            FakeHttp.json200(request.url?.path == "/graphql" ? GitHubParseTests.normal : renewal)
        }
    }

    private func expiring(in seconds: TimeInterval) {
        _ = secrets.set(GitHubApp.refreshName, refreshToken)
        _ = secrets.set(GitHubApp.expiresName, String(Int64(clock.now.timeIntervalSince1970 + seconds)))
    }

    func testARefreshSendsTheConstantQueryAndNothingElse() async throws {
        let http = serve(GitHubParseTests.normal)
        let api = newApi(http)

        let result = await api.refresh()

        XCTAssertEqual(result, GitHubRefreshResult(result: .ok, changed: true))
        XCTAssertEqual(http.seen.count, 1)
        let graphql = http.seen[0]
        XCTAssertEqual(graphql.method, "POST")
        XCTAssertEqual(graphql.url, "https://api.github.com/graphql")
        let body = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(graphql.body.utf8)) as? [String: Any])
        XCTAssertEqual(body["query"] as? String, GitHubApi.query)
        XCTAssertEqual(body.count, 1)
        XCTAssertEqual(graphql.headers["Content-Type"], "application/json")
        XCTAssertFalse(http.seen.contains { $0.url.contains("notifications") })
        for seen in http.seen {
            XCTAssertEqual(seen.host, "api.github.com")
            XCTAssertEqual(seen.headers["Authorization"], "Bearer " + token)
            XCTAssertEqual(seen.headers["Accept"], "application/vnd.github+json")
            XCTAssertEqual(seen.headers["X-GitHub-Api-Version"], "2022-11-28")
            XCTAssertEqual(seen.headers["User-Agent"], "Edgepad")
        }
        XCTAssertFalse(GitHubApi.query.lowercased().contains("mutation"))
        let min = GitHubParseTests.updatedMin
        let page = try XCTUnwrap(api.page)
        XCTAssertEqual(page.rev, 1)
        XCTAssertEqual(
            page.body,
            [
                "H\tgithub\tocto-cat\tOcto Cat\t34\t12\t1",
                "P\tocto-cat/edgepad\t\tC#\t5\t2\t1",
                "P\tocto-cat/secret\tp\t\t0\t0\t0",
                "M\tme/my_repo-2\t3\tMine\t\tfail\tmerged\t\(min)",
                "M\tme/my_repo-2\t9\tCrash on pair\ti\tnone\topen\t\(min)",
                "R\tocto/edgepad\t12\tFix the pairing timeout\t\tok\topen\t\(min)",
                "R\tocto/other.repo\t7\tDraft idea\td\tnone\topen\t\(min)",
                "A\tocto/edgepad\t4\tAssigned to me\ti\tnone\tclosed\t0",
            ].joined(separator: "\n"))
        XCTAssertEqual(
            page.urls,
            [
                "https://github.com/octo-cat/edgepad", "https://github.com/octo-cat/secret",
                "https://github.com/me/my_repo-2/pull/3", "https://github.com/me/my_repo-2/issues/9",
                "https://github.com/octo/edgepad/pull/12", "https://github.com/octo/other.repo/pull/7", "",
                "https://github.com/apps/edgepad-dashboard/installations/new",
            ])
    }

    func testTheInstallAddressIsTheOnlyExtraOpenableOne() async throws {
        let api = newApi(serve(GitHubParseTests.normal))
        _ = await api.refresh()
        let page = try XCTUnwrap(api.page)
        let find: (String) -> GitHost? = { $0 == "github" ? api : nil }
        let last = page.urls.count - 1
        let opened = GitOpenGate.resolve("github/\(page.rev)/\(last)", hosts: find)
        XCTAssertEqual(opened?.absoluteString, GitHubApp.installUrl)
        XCTAssertNil(GitOpenGate.resolve("github/\(page.rev)/\(last + 1)", hosts: find))
        XCTAssertNil(GitOpenGate.resolve("github/1/https://evil.example", hosts: find))
    }

    func testTheRevisionOnlyGoesUpWhenThePageChanges() async {
        let api = newApi(serve(GitHubParseTests.normal))

        _ = await api.refresh()
        let again = await api.refresh()

        XCTAssertEqual(again, GitHubRefreshResult(result: .ok, changed: false))
        XCTAssertEqual(api.page?.rev, 1)
    }

    func testWithoutATokenNobodyIsAsked() async {
        secrets.delete(GitHubApp.tokenName)
        let http = serve(GitHubParseTests.normal)
        let result = await newApi(http).refresh()
        XCTAssertEqual(result.result, .notSignedIn)
        XCTAssertTrue(http.seen.isEmpty)
    }

    func testATokenThatCouldInjectAHeaderIsNeverSent() async {
        _ = secrets.set(GitHubApp.tokenName, "ghu_x\r\nX-Evil: 1")
        let http = serve(GitHubParseTests.normal)
        let result = await newApi(http).refresh()
        XCTAssertEqual(result.result, .notSignedIn)
        XCTAssertTrue(http.seen.isEmpty)
    }

    func testATokenOfTheNewKindWithNoRecordIsKeptAndTheRecordIsWritten() {
        secrets.delete(GitHubApp.clientName)
        GitHubApp.migrate(secrets, GitHubApp.clientId)
        XCTAssertEqual(secrets.get(GitHubApp.tokenName), token)
        XCTAssertEqual(secrets.get(GitHubApp.clientName), GitHubApp.clientId)
    }

    func testAnOldKindOfTokenIsDeletedEvenWithTheNewRecord() {
        _ = secrets.set(GitHubApp.tokenName, "gho_OldOAuthAppToken1234567890")
        GitHubApp.migrate(secrets, GitHubApp.clientId)
        XCTAssertNil(secrets.get(GitHubApp.tokenName))
        XCTAssertTrue(GitHubApp.needsReSignIn(secrets))
    }

    func testASaveWritesTheRefreshTokenFirstThenTheTokenTheExpiryAndTheRecord() {
        let recording = RecordingSecrets()
        let saved = GitHubApp.save(
            recording, GitHubApp.clientId, access: token, refresh: refreshToken, expires: clock.now.addingTimeInterval(60))
        XCTAssertTrue(saved)
        XCTAssertEqual(
            recording.written, [GitHubApp.refreshName, GitHubApp.tokenName, GitHubApp.expiresName, GitHubApp.clientName])
    }

    func testOtherRefreshErrorsKeepTheTokensAndOnlyBadRefreshTokenSignsOut() async {
        expiring(in: 60)
        let http = FakeHttp { _ in FakeHttp.json200(#"{"error":"temporarily_unavailable"}"#) }

        let result = await newApi(http).refresh()

        XCTAssertNotEqual(result.result, .noPermission)
        XCTAssertEqual(secrets.get(GitHubApp.tokenName), token)
        XCTAssertEqual(secrets.get(GitHubApp.refreshName), refreshToken)
    }

    func testA401OnTheRefreshSignsOut() async {
        expiring(in: 60)
        let http = FakeHttp { _ in HttpReply(status: 401) }

        let result = await newApi(http).refresh()

        XCTAssertEqual(result.result, .noPermission)
        XCTAssertNil(secrets.get(GitHubApp.refreshName))
    }

    func testTwoClientsOnOneStoreRefreshOnlyOnce() async {
        expiring(in: 60)
        let http = routes(renewed)
        let first = newApi(http)
        let second = newApi(http)

        async let one = first.refresh()
        async let two = second.refresh()
        _ = await (one, two)

        XCTAssertEqual(http.seen.filter { $0.url == "https://github.com/login/oauth/access_token" }.count, 1)
        XCTAssertEqual(secrets.get(GitHubApp.tokenName), newToken)
    }

    func testATokenFromTheOldOAuthAppIsDeletedAndNobodyIsAsked() async {
        _ = secrets.set(GitHubApp.tokenName, "gho_OldOAuthAppToken1234567890")
        secrets.delete(GitHubApp.clientName)
        let http = serve(GitHubParseTests.normal)

        let result = await newApi(http).refresh()

        XCTAssertEqual(result.result, .notSignedIn)
        XCTAssertTrue(http.seen.isEmpty)
        XCTAssertNil(secrets.get(GitHubApp.tokenName))
        XCTAssertTrue(GitHubApp.needsReSignIn(secrets))
    }

    func testAnExpiringTokenIsRenewedBeforeItRunsOutWithNoSecretAndTheNewOneIsUsed() async {
        expiring(in: 120)
        let http = routes(renewed)

        let result = await newApi(http).refresh()

        XCTAssertEqual(result.result, .ok)
        XCTAssertEqual(http.seen.count, 2)
        XCTAssertEqual(http.seen[0].url, "https://github.com/login/oauth/access_token")
        XCTAssertEqual(http.seen[0].method, "POST")
        XCTAssertEqual(
            http.seen[0].body, "client_id=\(GitHubApp.clientId)&grant_type=refresh_token&refresh_token=\(refreshToken)")
        XCTAssertFalse(http.seen[0].body.lowercased().contains("secret"))
        XCTAssertEqual(http.seen[1].headers["Authorization"], "Bearer " + newToken)
        XCTAssertEqual(secrets.get(GitHubApp.tokenName), newToken)
        XCTAssertEqual(secrets.get(GitHubApp.refreshName), newRefresh)
        XCTAssertEqual(secrets.get(GitHubApp.expiresName), String(Int64(clock.now.timeIntervalSince1970) + 28800))
    }

    func testATokenWithTimeLeftIsNotRenewed() async {
        expiring(in: 2 * 3600)
        let http = routes(renewed)

        _ = await newApi(http).refresh()

        XCTAssertEqual(http.seen.count, 1)
        XCTAssertEqual(http.seen[0].url, "https://api.github.com/graphql")
    }

    func testARefusalIsAnsweredByOneRenewalAndOneRetryThenTheTokenGoes() async {
        expiring(in: 2 * 3600)
        let http = FakeHttp { request in
            request.url?.path == "/graphql" ? HttpReply(status: 401) : FakeHttp.json200(self.renewed)
        }

        let result = await newApi(http).refresh()

        XCTAssertEqual(result.result, .noPermission)
        XCTAssertEqual(http.seen.count, 3)
        XCTAssertNil(secrets.get(GitHubApp.tokenName))
        XCTAssertNil(secrets.get(GitHubApp.refreshName))
    }

    func testARefusalThatARenewalFixesIsRetriedOnce() async {
        expiring(in: 2 * 3600)
        var graphql = 0
        let http = FakeHttp { request in
            if request.url?.path != "/graphql" { return FakeHttp.json200(self.renewed) }
            graphql += 1
            return graphql == 1 ? HttpReply(status: 401) : FakeHttp.json200(GitHubParseTests.normal)
        }

        let result = await newApi(http).refresh()

        XCTAssertEqual(result.result, .ok)
        XCTAssertEqual(http.seen.count, 3)
        XCTAssertEqual(http.seen[2].headers["Authorization"], "Bearer " + newToken)
    }

    func testARefusedRefreshTokenSignsOutWithAClearResult() async {
        expiring(in: 60)
        let http = FakeHttp { _ in
            FakeHttp.json200(#"{"error":"bad_refresh_token","error_description":"The refresh token is incorrect."}"#)
        }
        let api = newApi(http)

        let result = await api.refresh()

        XCTAssertEqual(result.result, .noPermission)
        XCTAssertEqual(http.seen.count, 1)
        XCTAssertNil(secrets.get(GitHubApp.tokenName))
        XCTAssertNil(secrets.get(GitHubApp.refreshName))
        XCTAssertNil(api.page)
    }

    func testARenewalThatCannotReachGitHubKeepsTheTokens() async {
        expiring(in: 60)
        let http = FakeHttp { request in
            request.url?.path == "/graphql" ? FakeHttp.json200(GitHubParseTests.normal) : nil
        }

        _ = await newApi(http).refresh()

        XCTAssertEqual(secrets.get(GitHubApp.tokenName), token)
        XCTAssertEqual(secrets.get(GitHubApp.refreshName), refreshToken)
    }

    func testATokenThatDoesNotExpireIsNeverRenewedAndARefusalDeletesIt() async {
        let http = FakeHttp { _ in HttpReply(status: 401) }

        let result = await newApi(http).refresh()

        XCTAssertEqual(result.result, .noPermission)
        XCTAssertEqual(http.seen.count, 1)
        XCTAssertEqual(http.seen[0].url, "https://api.github.com/graphql")
        XCTAssertNil(secrets.get(GitHubApp.tokenName))
    }

    func testARefusedTokenIsDeletedAndTheScreenEmptied() async {
        var refuse = false
        let http = FakeHttp { _ in
            refuse ? HttpReply(status: 401) : FakeHttp.json200(GitHubParseTests.normal)
        }
        let api = newApi(http)
        _ = await api.refresh()
        refuse = true

        let result = await api.refresh()

        XCTAssertEqual(result, GitHubRefreshResult(result: .noPermission, changed: true))
        XCTAssertNil(secrets.get(GitHubApp.tokenName))
        XCTAssertNil(api.page)
        XCTAssertEqual(http.seen.count, 2)
    }

    func testARateLimitKeepsTheLastResultAndWaitsForTheReset() async {
        var limited = false
        let reset = String(Int64(clock.now.timeIntervalSince1970) + 600)
        let http = FakeHttp { _ in
            if limited {
                return HttpReply(status: 403, headers: ["x-ratelimit-remaining": "0", "x-ratelimit-reset": reset])
            }
            return FakeHttp.json200(GitHubParseTests.normal)
        }
        let api = newApi(http)

        _ = await api.refresh()
        let kept = api.page
        limited = true
        let first = await api.refresh()
        XCTAssertEqual(first.result, .rateLimited)
        XCTAssertEqual(api.page, kept)
        XCTAssertEqual(http.seen.count, 2)

        clock.advance(9 * 60)
        let second = await api.refresh()
        XCTAssertEqual(second.result, .rateLimited)
        XCTAssertEqual(http.seen.count, 2)

        limited = false
        clock.advance(2 * 60)
        let third = await api.refresh()
        XCTAssertEqual(third.result, .ok)
        XCTAssertEqual(http.seen.count, 3)
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
        XCTAssertEqual(http.seen.count, 2)
    }

    func testAnyOtherErrorKeepsTheLastGoodResult() async {
        for status in [500, 302, 404] {
            var broken = false
            let http = FakeHttp { _ in
                broken ? HttpReply(status: status) : FakeHttp.json200(GitHubParseTests.normal)
            }
            let api = newApi(http)
            _ = await api.refresh()
            let kept = api.page
            broken = true
            let result = await api.refresh()
            XCTAssertEqual(result, GitHubRefreshResult(result: .failed, changed: false))
            XCTAssertEqual(api.page, kept)
        }
    }

    func testAReplyOverOneMibIsRefused() async {
        let huge = String(repeating: " ", count: GitHubApi.maxBytes + 1)
        let result = await newApi(serve(huge)).refresh()
        XCTAssertEqual(result.result, .failed)
    }

    func testTheLoginIsReadForDisplayAndARefusalDeletesTheToken() async {
        let login = await newApi(FakeHttp { _ in FakeHttp.json200(#"{"login":"octo-cat"}"#) }).login()
        XCTAssertEqual(login, "octo-cat")

        let refused = await newApi(FakeHttp { _ in HttpReply(status: 401) }).login()
        XCTAssertNil(refused)
        XCTAssertNil(secrets.get(GitHubApp.tokenName))
    }

    func testARefusalOfTheOldTokenKeepsANewerOneSavedWhileTheRequestWasOut() async {
        let newer = "ghu_NewerTokenValue1234567890abcdefghij"
        let secrets = secrets
        let http = FakeHttp { _ in
            _ = secrets.set(GitHubApp.tokenName, newer)
            return HttpReply(status: 401)
        }

        let login = await newApi(http).login()

        XCTAssertNil(login)
        XCTAssertEqual(secrets.get(GitHubApp.tokenName), newer)
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

/// Records the order the names were written in.
final class RecordingSecrets: SecretStore {
    private(set) var written: [String] = []

    func get(_ name: String) -> String? { nil }

    func set(_ name: String, _ value: String) -> Bool {
        written.append(name)
        return true
    }

    func delete(_ name: String) {}
}
