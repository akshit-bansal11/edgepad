import XCTest

@testable import EdgepadCore

/// Counts how many times the expired notice was raised.
private final class ExpiryCount: @unchecked Sendable {
    private let lock = NSLock()
    private var count = 0

    var value: Int { lock.withLock { count } }

    func bump() { lock.withLock { count += 1 } }
}

/// Records the order tokens are saved in.
private final class OrderedSecrets: SecretStore {
    private let lock = NSLock()
    private var values: [String: String] = [:]
    private var order: [String] = []

    var saved: [String] { lock.withLock { order } }

    func get(_ name: String) -> String? { lock.withLock { values[name] } }

    func set(_ name: String, _ value: String) -> Bool {
        lock.withLock {
            order.append(name)
            values[name] = value
        }
        return true
    }

    func delete(_ name: String) { lock.withLock { values[name] = nil } }
}

/// A network that lets a test do something (save a server, say) at the moment a request is out.
private final class RacingHttp: HttpSender {
    private let inner: FakeHttp
    var before: ((URLRequest) async -> Void)?

    init(_ inner: FakeHttp) {
        self.inner = inner
    }

    func send(_ request: URLRequest, maxBytes: Int, what: String) async -> HttpReply? {
        await before?(request)
        return await inner.send(request, maxBytes: maxBytes, what: what)
    }
}

final class GitLabTests: XCTestCase {
    private let client = "90603e45237215e265b1cc5a873ec796353c5e3185c14be0341a371a0dc5d000"
    private let token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
    private let refreshToken = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210"
    private let newToken = "aaaaaaaaaaaaaaaabbbbbbbbbbbbbbbbccccccccccccccccdddddddddddddddd"
    private let pasted = "glpat-SelfHostedPastedToken123"
    private let newRefresh = "eeeeeeeeeeeeeeeeffffffffffffffff0000000000000000ffffffffffffffff"

    private let code =
        #"{"device_code":"1234567890abcdef","user_code":"0A44L90H","#
        + #""verification_uri":"https://gitlab.com/oauth/device","expires_in":300,"interval":5}"#
    private let pending = #"{"error":"authorization_pending","error_description":"..."}"#
    private let slowDown = #"{"error":"slow_down"}"#
    private let granted =
        #"{"access_token":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","token_type":"Bearer","#
        + #""expires_in":7200,"refresh_token":"fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210","#
        + #""scope":"read_api read_user","created_at":1763553600}"#
    private let renewed =
        #"{"access_token":"aaaaaaaaaaaaaaaabbbbbbbbbbbbbbbbccccccccccccccccdddddddddddddddd","token_type":"Bearer","#
        + #""expires_in":7200,"refresh_token":"eeeeeeeeeeeeeeeeffffffffffffffff0000000000000000ffffffffffffffff","#
        + #""created_at":1763553600}"#

    private let user =
        #"{"id":1,"username":"ana","name":"Ana B","avatar_url":"https://gitlab.com/uploads/a.png","#
        + #""web_url":"https://gitlab.com/ana","followers":3,"following":0}"#

    private let projects = """
        [{"id":1,"path_with_namespace":"grp/new","visibility":"private","open_issues_count":2,"star_count":5,
          "web_url":"https://gitlab.com/grp/new","last_activity_at":"2026-11-01T00:00:00Z","default_branch":"main"},
         {"id":2,"path_with_namespace":"grp/old","visibility":"public","open_issues_count":0,"star_count":1,
          "web_url":"https://gitlab.com/grp/old","last_activity_at":"2026-01-01T00:00:00Z","default_branch":"main"}]
        """

    private let mergesMine = """
        [{"iid":12,"project_id":1,"title":"Fix","state":"opened","draft":true,
          "web_url":"https://gitlab.com/grp/new/-/merge_requests/12","updated_at":"2026-11-02T00:00:00Z",
          "references":{"full":"grp/new!12"},"head_pipeline":{"status":"failed"}}]
        """

    private let issuesMine = """
        [{"iid":6,"project_id":1,"title":"Bug","state":"opened","web_url":"https://gitlab.com/grp/new/-/issues/6",
          "updated_at":"2026-11-03T00:00:00Z","references":{"full":"grp/new#6"}}]
        """

    private let mergesReview = """
        [{"iid":3,"project_id":9,"title":"Review me","state":"opened","draft":false,
          "web_url":"https://gitlab.com/other/lib/-/merge_requests/3","updated_at":"2026-11-04T00:00:00Z",
          "references":{"full":"other/lib!3"},"head_pipeline":{"status":"running"}}]
        """

    // UTC minutes since the Unix epoch, worked out by hand: 2026-11-02, 03 and 04 at 00:00Z.
    private let nov2: Int64 = 29_892_960
    private let nov3: Int64 = 29_894_400
    private let nov4: Int64 = 29_895_840

    private var folder = ""
    private var settings: IntegrationSettings!
    private let clock = TestClock()
    private let secrets = MemorySecrets()
    private var waits: [Int] = []

    override func setUpWithError() throws {
        try super.setUpWithError()
        folder = (NSTemporaryDirectory() as NSString).appendingPathComponent("gitlab-" + UUID().uuidString)
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        settings = IntegrationSettings(path: folder + "/" + IntegrationSettings.fileName)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: folder)
        try super.tearDownWithError()
    }

    // ---- helpers

    private func host(_ http: HttpSender) -> GitLabHost {
        GitLabHost(store: secrets, settings: settings, http: http, clock: { [clock] in clock.now })
    }

    private func signIn(_ http: HttpSender, _ store: SecretStore? = nil) -> GitLabSignIn {
        GitLabSignIn(
            store: store ?? secrets, settings: settings, http: http, clock: { [clock] in clock.now },
            delay: { [weak self] seconds in
                self?.waits.append(Int(seconds))
                self?.clock.advance(seconds)
            })
    }

    private func expiring(in seconds: TimeInterval) {
        _ = secrets.set(GitLabApp.tokenName, token)
        _ = secrets.set(GitLabApp.refreshName, refreshToken)
        _ = secrets.set(GitLabApp.expiresName, String(Int64(clock.now.timeIntervalSince1970 + seconds)))
        _ = secrets.set(GitLabApp.clientName, client)
    }

    private func serve(_ request: URLRequest) -> HttpReply? {
        let query = request.url?.query ?? ""
        switch request.url?.path ?? "" {
        case "/api/v4/user": return FakeHttp.json200(user)
        case "/api/v4/projects": return FakeHttp.json200(projects, headers: ["x-total": "2"])
        case "/api/v4/merge_requests":
            if query.contains("created_by_me") { return FakeHttp.json200(mergesMine) }
            return FakeHttp.json200(query.contains("reviews_for_me") ? mergesReview : "[]")
        case "/api/v4/issues": return FakeHttp.json200(query.contains("created_by_me") ? issuesMine : "[]")
        default: return nil
        }
    }

    private func request(_ method: String, _ url: String) throws -> URLRequest {
        var request = URLRequest(url: try XCTUnwrap(URL(string: url)))
        request.httpMethod = method
        return request
    }

    private func logText() -> String {
        (try? String(contentsOfFile: Log.filePath, encoding: .utf8)) ?? ""
    }

    // ---- device sign-in

    func testTheDeviceRequestUsesThisClientIdAndTheTwoReadScopesAndNoSecret() async {
        let http = FakeHttp.script([code])
        let start = await signIn(http).start()

        XCTAssertEqual(
            start,
            DeviceCode(state: .started, userCode: "0A44L90H", page: "https://gitlab.com/oauth/device", expiresIn: 300))
        XCTAssertEqual(http.seen.count, 1)
        XCTAssertEqual(http.seen.first?.method, "POST")
        XCTAssertEqual(http.seen.first?.url, "https://gitlab.com/oauth/authorize_device")
        XCTAssertEqual(http.seen.first?.body, "client_id=\(client)&scope=read_api+read_user")
        XCTAssertEqual(GitLabApp.clientId, client)
        XCTAssertFalse((http.seen.first?.body ?? "").lowercased().contains("secret"))
    }

    func testPendingThenSlowDownThenGrantedSavesTheTokensAndPollsGitLabOnly() async {
        let http = FakeHttp.script([code, pending, slowDown, granted])
        let flow = signIn(http)

        _ = await flow.start()
        let state = await flow.wait()

        XCTAssertEqual(state, .signedIn)
        XCTAssertEqual(waits, [5, 5, 10])
        for seen in http.seen.dropFirst() {
            XCTAssertEqual(seen.url, "https://gitlab.com/oauth/token")
            XCTAssertEqual(
                seen.body,
                "client_id=\(client)&device_code=1234567890abcdef"
                    + "&grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code")
        }
        XCTAssertEqual(secrets.get(GitLabApp.tokenName), token)
        XCTAssertEqual(secrets.get(GitLabApp.refreshName), refreshToken)
        XCTAssertEqual(secrets.get(GitLabApp.expiresName), String(Int64(clock.now.timeIntervalSince1970) + 7200))
        XCTAssertEqual(secrets.get(GitLabApp.clientName), client)
        XCTAssertTrue(flow.isSignedIn)
    }

    func testTheSaveOrderIsRefreshAccessExpiryThenClient() {
        let store = OrderedSecrets()

        let until = clock.now.addingTimeInterval(7200)
        XCTAssertTrue(GitLabApp.save(store, client, access: token, refresh: refreshToken, expires: until))

        XCTAssertEqual(
            store.saved, [GitLabApp.refreshName, GitLabApp.tokenName, GitLabApp.expiresName, GitLabApp.clientName])
    }

    func testASignInAfterASelfHostedOneMovesTheAddressBackToGitLabCom() async {
        settings.set(GitTokens.urlKey("gitlab"), "https://git.example.org")
        let flow = signIn(FakeHttp.script([code, granted]))

        _ = await flow.start()
        _ = await flow.wait()

        XCTAssertEqual(settings.text(GitTokens.urlKey("gitlab")), "")
    }

    func testADenialAnExpiredCodeAndAnUnknownErrorEndTheFlowWithNothingSaved() async {
        let cases: [(String, SignInState)] = [
            (#"{"error":"access_denied"}"#, .denied), (#"{"error":"expired_token"}"#, .expired),
            (#"{"error":"invalid_client"}"#, .failed),
        ]
        for (answer, expected) in cases {
            let flow = signIn(FakeHttp.script([code, answer]))
            _ = await flow.start()
            let state = await flow.wait()
            XCTAssertEqual(state, expected, answer)
            XCTAssertNil(secrets.get(GitLabApp.tokenName))
        }
    }

    func testAnAnswerWithoutARefreshTokenIsKeptWithoutAnExpiry() async {
        let plain = #"{"access_token":"0123456789abcdef0123456789abcdef","token_type":"Bearer","expires_in":7200}"#
        let flow = signIn(FakeHttp.script([code, plain]))

        _ = await flow.start()
        let state = await flow.wait()

        XCTAssertEqual(state, .signedIn)
        XCTAssertNil(secrets.get(GitLabApp.refreshName))
        XCTAssertNil(secrets.get(GitLabApp.expiresName))
    }

    // ---- renewal, never with a secret

    func testAnExpiringTokenIsRenewedBeforeItRunsOutWithNoSecretAndTheNewOneIsUsed() async {
        expiring(in: 120)
        let http = FakeHttp { request in
            request.url?.path == "/oauth/token" ? FakeHttp.json200(self.renewed) : self.serve(request)
        }

        let result = await host(http).refresh()

        XCTAssertEqual(result.result, .ok)
        let renew = http.seen[0]
        XCTAssertEqual(renew.method, "POST")
        XCTAssertEqual(renew.url, "https://gitlab.com/oauth/token")
        XCTAssertEqual(renew.body, "client_id=\(client)&grant_type=refresh_token&refresh_token=\(refreshToken)")
        XCTAssertFalse(renew.body.lowercased().contains("secret"))
        for seen in http.seen.dropFirst() {
            XCTAssertEqual(seen.headers["Authorization"], "Bearer " + newToken)
        }
        XCTAssertEqual(secrets.get(GitLabApp.tokenName), newToken)
        XCTAssertEqual(secrets.get(GitLabApp.refreshName), newRefresh)
    }

    func testATokenWithTimeLeftIsNotRenewed() async {
        expiring(in: 3600)
        let http = FakeHttp(serve)

        _ = await host(http).refresh()

        XCTAssertFalse(http.seen.contains { $0.method == "POST" })
    }

    func testOnlyInvalidGrantSignsOut() async {
        expiring(in: 60)
        let http = FakeHttp { request in
            if request.url?.path == "/oauth/token" {
                return HttpReply(status: 400, body: Data(#"{"error":"invalid_grant"}"#.utf8))
            }
            return self.serve(request)
        }

        let result = await host(http).refresh()

        XCTAssertEqual(result.result, .noPermission)
        XCTAssertNil(secrets.get(GitLabApp.tokenName))
        XCTAssertNil(secrets.get(GitLabApp.refreshName))
        XCTAssertNil(secrets.get(GitLabApp.clientName))
    }

    func testARefusedRenewalLeavesAnExpiredStateAndRaisesTheNoticeOnce() async {
        expiring(in: 60)
        let http = FakeHttp { request in
            if request.url?.path == "/oauth/token" {
                return HttpReply(status: 400, body: Data(#"{"error":"invalid_grant"}"#.utf8))
            }
            return self.serve(request)
        }
        let gitlab = host(http)
        let notices = ExpiryCount()
        gitlab.onExpired = { notices.bump() }

        XCTAssertFalse(gitlab.signInExpired)
        let result = await gitlab.refresh()

        XCTAssertEqual(result.result, .noPermission)
        XCTAssertTrue(gitlab.signInExpired)
        XCTAssertEqual(notices.value, 1)
        XCTAssertNil(secrets.get(GitLabApp.tokenName))
    }

    func testAnUnavailableRenewalIsNotAnExpiry() async {
        expiring(in: 60)
        let http = FakeHttp { request in
            if request.url?.path == "/oauth/token" {
                return HttpReply(status: 500, body: Data(#"{"error":"server_error"}"#.utf8))
            }
            return self.serve(request)
        }
        let gitlab = host(http)

        _ = await gitlab.refresh()

        XCTAssertFalse(gitlab.signInExpired)
    }

    func testATokenSavedAfterAnExpiryClearsIt() async {
        _ = secrets.set(GitLabApp.expiredName, "1")
        let gitlab = host(FakeHttp(serve))
        XCTAssertTrue(gitlab.signInExpired)

        let saved = await gitlab.saveServer(token: pasted, url: GitLabApp.comUrl)

        XCTAssertEqual(saved.status, .saved)
        XCTAssertFalse(gitlab.signInExpired)
        XCTAssertNil(secrets.get(GitLabApp.expiredName))
    }

    func testAGitLabComTokenIsCheckedAgainstGitLabComAndKeptWithTheFixedAddress() async {
        let http = FakeHttp(serve)
        let gitlab = host(http)

        let saved = await gitlab.saveServer(token: " " + pasted + " ", url: GitLabApp.comUrl)
        clock.advance(300)
        let result = await gitlab.refresh()

        XCTAssertEqual(saved, GitSaveResult(status: .saved, login: "ana"))
        XCTAssertEqual(result.result, .ok)
        XCTAssertEqual(gitlab.domain, "gitlab.com")
        XCTAssertEqual(secrets.get(GitLabApp.tokenName), pasted)
        XCTAssertNil(secrets.get(GitLabApp.refreshName))
        for seen in http.seen {
            XCTAssertEqual(seen.method, "GET")
            XCTAssertEqual(seen.host, "gitlab.com")
            XCTAssertEqual(seen.headers["Authorization"], "Bearer " + pasted)
        }
    }

    func testAnyOtherRenewalFailureKeepsEveryToken() async {
        let failures: [(Int, String)] = [
            (500, #"{"error":"server_error"}"#), (401, #"{"error":"invalid_client"}"#),
            (502, "<html>bad gateway</html>"),
        ]
        for (status, body) in failures {
            expiring(in: 60)
            let http = FakeHttp { request in
                request.url?.path == "/oauth/token"
                    ? HttpReply(status: status, body: Data(body.utf8)) : self.serve(request)
            }

            _ = await host(http).refresh()

            XCTAssertEqual(secrets.get(GitLabApp.tokenName), token, body)
            XCTAssertEqual(secrets.get(GitLabApp.refreshName), refreshToken, body)
        }
    }

    func testARefusalIsAnsweredByOneRenewalAndOneRetry() async {
        expiring(in: 3600)
        let http = FakeHttp { request in
            if request.url?.path == "/oauth/token" { return FakeHttp.json200(self.renewed) }
            if request.allHTTPHeaderFields?["Authorization"] == "Bearer " + self.token { return HttpReply(status: 401) }
            return self.serve(request)
        }

        let result = await host(http).refresh()

        XCTAssertEqual(result.result, .ok)
        XCTAssertEqual(http.seen.filter { $0.method == "POST" }.count, 1)
        XCTAssertEqual(secrets.get(GitLabApp.tokenName), newToken)
    }

    func testTwoHostsOnOneStoreRenewOnlyOnce() async {
        expiring(in: 60)
        let http = FakeHttp { request in
            request.url?.path == "/oauth/token" ? FakeHttp.json200(self.renewed) : self.serve(request)
        }
        let first = host(http)
        let second = host(http)

        async let one = first.refresh()
        async let two = second.refresh()
        _ = await (one, two)

        XCTAssertEqual(http.seen.filter { $0.url == "https://gitlab.com/oauth/token" }.count, 1)
    }

    // ---- JSON to GitSnapshot

    func testTheDocumentedShapesMapToTheSnapshotAndEveryRequestIsAGet() async throws {
        _ = secrets.set(GitLabApp.tokenName, token)
        let http = FakeHttp(serve)
        let gitlab = host(http)

        let result = await gitlab.refresh()

        XCTAssertEqual(result, GitHubRefreshResult(result: .ok, changed: true))
        XCTAssertEqual(gitlab.domain, "gitlab.com")
        let page = try XCTUnwrap(gitlab.page)
        XCTAssertEqual(
            page.body,
            [
                "H\tgitlab\tana\tAna B\t2\t3\t1", "P\tgrp/new\tp\t\t5\t2\t0", "P\tgrp/old\t\t\t1\t0\t0",
                "M\tgrp/new\t6\tBug\ti\tnone\topen\t\(nov3)", "M\tgrp/new\t12\tFix\td\tfail\topen\t\(nov2)",
                "R\tother/lib\t3\tReview me\t\tpending\topen\t\(nov4)",
            ].joined(separator: "\n"))
        XCTAssertEqual(
            page.urls,
            [
                "https://gitlab.com/grp/new", "https://gitlab.com/grp/old", "https://gitlab.com/grp/new/-/issues/6",
                "https://gitlab.com/grp/new/-/merge_requests/12", "https://gitlab.com/other/lib/-/merge_requests/3",
            ])
        for seen in http.seen {
            XCTAssertEqual(seen.method, "GET")
            XCTAssertTrue(seen.url.hasPrefix("https://gitlab.com/api/v4/"), seen.url)
            XCTAssertEqual(seen.headers["Authorization"], "Bearer " + token)
            XCTAssertFalse(seen.url.contains(token))
        }
        XCTAssertTrue(
            http.seen.contains {
                $0.url.contains("/merge_requests?") && $0.url.contains("scope=reviews_for_me")
                    && $0.url.contains("state=opened")
            })
        XCTAssertTrue(
            http.seen.contains {
                $0.url.contains("/projects?") && $0.url.contains("membership=true")
                    && $0.url.contains("order_by=last_activity_at")
            })
    }

    func testAPipelineStatusMapsToAChecksToken() {
        let cases: [(String?, String)] = [
            ("success", "ok"), ("failed", "fail"), ("running", "pending"), ("pending", "pending"),
            ("created", "pending"), ("canceled", "none"), ("skipped", "none"), (nil, "none"),
        ]
        for (status, expected) in cases {
            XCTAssertEqual(GitLabHost.ci(status), expected, status ?? "nil")
        }
    }

    func testReviewStaysEmptyWhenTheServerDoesNotKnowReviewsForMe() async throws {
        _ = secrets.set(GitLabApp.tokenName, token)
        let http = FakeHttp { request in
            if request.url?.query?.contains("reviews_for_me") ?? false { return HttpReply(status: 400) }
            return self.serve(request)
        }
        let gitlab = host(http)

        let result = await gitlab.refresh()

        XCTAssertEqual(result.result, .ok)
        XCTAssertFalse("\n\(try XCTUnwrap(gitlab.page).body)".contains("\nR\t"))
    }

    func testARevokedTokenSignsOutLikeGitHub() async {
        _ = secrets.set(GitLabApp.tokenName, token)
        let gitlab = host(FakeHttp { _ in HttpReply(status: 401) })

        let result = await gitlab.refresh()

        XCTAssertEqual(result.result, .noPermission)
        XCTAssertNil(secrets.get(GitLabApp.tokenName))
        XCTAssertNil(gitlab.page)
    }

    // ---- self-hosted

    func testABadServerAddressIsRejectedAndNothingIsAskedOrSaved() async {
        let urls = [
            "", "http://git.example.org", "https://user:pw@git.example.org", "https://git.example.org/?a=1",
            "https://git.example.org/#x", "https://evil.example%23.gitlab.com", "ftp://git.example.org",
            "javascript:alert(1)",
        ]
        for url in urls {
            XCTAssertNil(GitTokens.gitlabApi(url), url)
            let http = FakeHttp(serve)

            let saved = await host(http).saveServer(token: token, url: url)

            XCTAssertEqual(saved.status, .badUrl, url)
            XCTAssertTrue(http.seen.isEmpty, url)
            XCTAssertNil(secrets.get(GitLabApp.tokenName))
        }
    }

    func testAGoodServerAddressBecomesTheApiRoot() {
        XCTAssertEqual(
            GitTokens.gitlabApi(" https://git.example.org/ ")?.absoluteString, "https://git.example.org/api/v4/")
        XCTAssertEqual(
            GitTokens.gitlabApi("https://example.org/gitlab/")?.absoluteString, "https://example.org/gitlab/api/v4/")
        XCTAssertEqual(
            GitTokens.gitlabApi("https://git.example.org:8443")?.absoluteString, "https://git.example.org:8443/api/v4/")
    }

    func testASelfHostedServerIsReadWithAPastedTokenAndNeverSeesAPost() async {
        expiring(in: 60)
        let http = FakeHttp(serve)
        let gitlab = host(http)

        let saved = await gitlab.saveServer(token: " " + pasted + " ", url: "https://git.example.org")

        XCTAssertEqual(saved, GitSaveResult(status: .saved, login: "ana"))
        XCTAssertEqual(settings.text(GitTokens.urlKey("gitlab")), "https://git.example.org")
        XCTAssertNil(secrets.get(GitLabApp.refreshName))
        XCTAssertNil(secrets.get(GitLabApp.expiresName))
        XCTAssertEqual(secrets.get(GitLabApp.tokenName), pasted)

        clock.advance(300)
        let result = await gitlab.refresh()
        XCTAssertEqual(result.result, .ok)
        XCTAssertEqual(gitlab.domain, "git.example.org")
        for seen in http.seen {
            XCTAssertEqual(seen.method, "GET")
            XCTAssertEqual(seen.host, "git.example.org")
            XCTAssertTrue(seen.url.hasPrefix("https://git.example.org/api/v4/"), seen.url)
            XCTAssertEqual(seen.headers["Authorization"], "Bearer " + pasted)
        }
    }

    func testASelfHostedRefusalIsReportedAndNothingIsSaved() async {
        let gitlab = host(FakeHttp { _ in HttpReply(status: 401) })

        let saved = await gitlab.saveServer(token: token, url: "https://git.example.org")

        XCTAssertEqual(saved.status, .unauthorized)
        XCTAssertNil(secrets.get(GitLabApp.tokenName))
        XCTAssertEqual(settings.text(GitTokens.urlKey("gitlab")), "")
    }

    func testSignOutForgetsEveryTokenAndKeepsTheServerAddress() {
        expiring(in: 3600)
        settings.set(GitTokens.urlKey("gitlab"), "https://git.example.org")

        host(FakeHttp(serve)).signOut()

        XCTAssertNil(secrets.get(GitLabApp.tokenName))
        XCTAssertNil(secrets.get(GitLabApp.refreshName))
        XCTAssertNil(secrets.get(GitLabApp.expiresName))
        XCTAssertNil(secrets.get(GitLabApp.clientName))
        XCTAssertEqual(settings.text(GitTokens.urlKey("gitlab")), "https://git.example.org")
    }

    // ---- a gitlab.com token never lands beside, or goes to, a server

    func testARenewalFinishingAfterAServerWasSavedStoresNothing() async {
        expiring(in: 60)
        let inner = FakeHttp { request in
            request.url?.path == "/oauth/token" ? FakeHttp.json200(self.renewed) : self.serve(request)
        }
        let racing = RacingHttp(inner)
        let gitlab = host(racing)
        racing.before = { [pasted] request in
            // The person saves a self-hosted server while the renewal is out.
            if request.url?.path == "/oauth/token" {
                let saved = await gitlab.saveServer(token: pasted, url: "https://git.example.org")
                XCTAssertEqual(saved.status, .saved)
            }
        }

        _ = await gitlab.refresh()

        XCTAssertEqual(secrets.get(GitLabApp.tokenName), pasted)
        XCTAssertNil(secrets.get(GitLabApp.refreshName))
        XCTAssertNil(secrets.get(GitLabApp.expiresName))
        XCTAssertNil(secrets.get(GitLabApp.clientName))
        XCTAssertEqual(settings.text(GitTokens.urlKey("gitlab")), "https://git.example.org")
        XCTAssertFalse(inner.seen.contains { $0.headers["Authorization"] == "Bearer " + newToken })
        XCTAssertFalse(inner.seen.contains { $0.host == "gitlab.com" && $0.headers["Authorization"] != nil })
    }

    func testADeviceSignInFinishingAfterAServerWasSavedStoresNothing() async {
        let inner = FakeHttp { request in
            switch request.url?.path ?? "" {
            case "/oauth/authorize_device": return FakeHttp.json200(self.code)
            case "/oauth/token": return FakeHttp.json200(self.granted)
            default: return self.serve(request)
            }
        }
        let racing = RacingHttp(inner)
        let gitlab = host(racing)
        racing.before = { [pasted] request in
            if request.url?.path == "/oauth/token" {
                let saved = await gitlab.saveServer(token: pasted, url: "https://git.example.org")
                XCTAssertEqual(saved.status, .saved)
            }
        }
        let flow = signIn(racing)

        _ = await flow.start()
        let state = await flow.wait()

        XCTAssertEqual(state, .failed)
        XCTAssertEqual(secrets.get(GitLabApp.tokenName), pasted)
        XCTAssertNil(secrets.get(GitLabApp.refreshName))
        XCTAssertNil(secrets.get(GitLabApp.clientName))
        XCTAssertEqual(settings.text(GitTokens.urlKey("gitlab")), "https://git.example.org")
    }

    func testAGitLabComTokenIsNeverSentToAnotherHost() async {
        expiring(in: 3600)
        settings.set(GitTokens.urlKey("gitlab"), "https://git.example.org")
        let http = FakeHttp(serve)

        let result = await host(http).refresh()

        XCTAssertEqual(result.result, .failed)
        XCTAssertTrue(http.seen.isEmpty)
    }

    // ---- the read-only gate

    func testTheGateAllowsOnlyGitLabsDeviceCallsAndReads() throws {
        let cases: [(String, String, Bool)] = [
            ("POST", "https://gitlab.com/oauth/authorize_device", true),
            ("POST", "https://gitlab.com/oauth/token", true),
            ("GET", "https://gitlab.com/api/v4/user", true), ("GET", "https://git.example.org/api/v4/user", true),
            ("POST", "https://git.example.org/oauth/token", false),
            ("POST", "https://git.example.org/oauth/authorize_device", false),
            ("POST", "https://gitlab.com/api/v4/projects", false),
            ("POST", "https://gitlab.com/oauth/token/revoke", false),
            ("PUT", "https://gitlab.com/api/v4/projects/1", false),
            ("PATCH", "https://gitlab.com/api/v4/merge_requests/1", false),
            ("DELETE", "https://gitlab.com/api/v4/projects/1", false), ("GET", "http://gitlab.com/api/v4/user", false),
        ]
        for (method, url, allowed) in cases {
            XCTAssertEqual(ReadOnlyGate.allows(try request(method, url)), allowed, "\(method) \(url)")
        }
    }

    func testARefusedWriteNamesNoTokenAndTheLogNeverHoldsOne() async throws {
        _ = secrets.set(GitLabApp.tokenName, token)
        let delete = try request("DELETE", "https://gitlab.com/api/v4/projects/1?private_token=" + token)

        XCTAssertThrowsError(try ReadOnlyGate.verify(delete)) { error in
            XCTAssertFalse(String(describing: error).contains(token))
        }

        _ = await host(FakeHttp(serve)).refresh()
        XCTAssertFalse(logText().contains(token))
    }
}
