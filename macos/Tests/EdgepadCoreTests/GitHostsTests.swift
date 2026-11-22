import XCTest

@testable import EdgepadCore

final class GitHostsTests: XCTestCase {
    private let token = "pat_NeverLoggedNeverSent123456"

    private let giteaUser = #"{"id":7,"login":"ana","full_name":"Ana B","followers_count":3}"#

    private let giteaRepos = """
        {"ok":true,"data":[
          {"full_name":"ana/old","private":false,"language":"Go","stars_count":1,"open_issues_count":2,"open_pr_counter":0,
           "html_url":"https://codeberg.org/ana/old","updated_at":"2026-01-01T00:00:00Z"},
          {"full_name":"ana/new","private":true,"language":"Rust","stars_count":5,"open_issues_count":0,"open_pr_counter":1,
           "html_url":"https://codeberg.org/ana/new","updated_at":"2026-11-01T00:00:00Z"}
        ]}
        """

    private let giteaMine = """
        [{"number":4,"title":"Fix","state":"open","html_url":"https://codeberg.org/ana/new/pulls/4",
          "updated_at":"2026-11-02T00:00:00Z","repository":{"full_name":"ana/new"},
          "pull_request":{"merged":false,"draft":true}}]
        """

    private let giteaAssigned = """
        [{"number":9,"title":"Bug","state":"open","html_url":"https://codeberg.org/ana/old/issues/9",
          "updated_at":"2026-11-03T00:00:00Z","repository":{"full_name":"ana/old"},"pull_request":null}]
        """

    private let bitbucketUser = #"{"nickname":"ana","display_name":"Ana B","account_id":"557058:abc","uuid":"{1}"}"#

    private let bitbucketWorkspaces =
        #"{"pagelen":25,"page":1,"size":1,"values":[{"administrator":true,"workspace":{"slug":"ws","uuid":"{2}"}}]}"#

    private let bitbucketRepos = """
        {"size":2,"values":[
          {"full_name":"ws/old","is_private":false,"language":"python","updated_on":"2026-01-01T00:00:00Z",
           "links":{"html":{"href":"https://bitbucket.org/ws/old"}}},
          {"full_name":"ws/new","is_private":true,"language":"","updated_on":"2026-11-01T00:00:00.123456+00:00",
           "links":{"html":{"href":"https://bitbucket.org/ws/new"}}}
        ]}
        """

    private let bitbucketPulls = """
        {"values":[
          {"id":12,"title":"Ship","state":"OPEN","draft":true,"updated_on":"2026-11-02T00:00:00Z",
           "destination":{"repository":{"full_name":"ws/new"}},
           "links":{"html":{"href":"https://bitbucket.org/ws/new/pull-requests/12"}}}
        ]}
        """

    private let giteeUser = #"{"login":"ana","name":"Ana B","followers":4,"public_repos":9}"#

    private let giteeRepos = """
        [{"full_name":"ana/zh","public":true,"language":"Java","stargazers_count":8,"open_issues_count":3,
          "html_url":"https://gitee.com/ana/zh","updated_at":"2026-11-01T00:00:00Z"}]
        """

    private let giteeIssuesMine = """
        [{"number":"I1ABCD","title":"Bug","state":"open","html_url":"https://gitee.com/ana/zh/issues/I1ABCD",
          "updated_at":"2026-11-02T00:00:00Z","repository":{"full_name":"ana/zh"}}]
        """

    private let giteeIssuesAssigned = """
        [{"number":"I2XYZ","title":"Fix","state":"open","html_url":"https://gitee.com/ana/zh/issues/I2XYZ",
          "updated_at":"2026-11-03T00:00:00Z","repository":{"full_name":"ana/zh"}}]
        """

    // UTC minutes since the Unix epoch, worked out by hand: 2026-11-02T00:00Z and 2026-11-03T00:00Z.
    private let nov2: Int64 = 29_892_960
    private let nov3: Int64 = 29_894_400

    private var folder = ""
    private var settings: IntegrationSettings!
    private let clock = TestClock()
    private let secrets = MemorySecrets()

    override func setUpWithError() throws {
        try super.setUpWithError()
        folder = (NSTemporaryDirectory() as NSString).appendingPathComponent("githosts-" + UUID().uuidString)
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        settings = IntegrationSettings(path: folder + "/" + IntegrationSettings.fileName)
        for id in ["gitea", "bitbucket", "gitee"] {
            _ = secrets.set(GitTokens.tokenName(id), token)
        }
        settings.set(GitTokens.userKey("bitbucket"), "me@example.org")
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: folder)
        try super.tearDownWithError()
    }

    // ---- servers

    private func serveGitea(_ request: URLRequest) -> HttpReply? {
        // Any path prefix: a Forgejo behind a sub-path answers under /forge/api/v1.
        let path = request.url?.path ?? ""
        let query = request.url?.query ?? ""
        guard let root = path.range(of: "/api/v1") else { return nil }
        switch String(path[root.lowerBound...]) {
        case "/api/v1/user": return FakeHttp.json200(giteaUser)
        case "/api/v1/repos/search": return FakeHttp.json200(giteaRepos, headers: ["x-total-count": "12"])
        case "/api/v1/repos/issues/search":
            if query.contains("created=true") { return FakeHttp.json200(giteaMine) }
            return FakeHttp.json200(query.contains("assigned=true") ? giteaAssigned : "[]")
        default: return nil
        }
    }

    private func serveBitbucket(_ request: URLRequest) -> HttpReply? {
        let path = request.url?.path ?? ""
        switch path {
        case "/2.0/user": return FakeHttp.json200(bitbucketUser)
        case "/2.0/user/workspaces": return FakeHttp.json200(bitbucketWorkspaces)
        case "/2.0/repositories/ws": return FakeHttp.json200(bitbucketRepos)
        default:
            return path.hasPrefix("/2.0/workspaces/ws/pullrequests/") ? FakeHttp.json200(bitbucketPulls) : nil
        }
    }

    private func serveGitee(_ request: URLRequest) -> HttpReply? {
        switch request.url?.path ?? "" {
        case "/api/v5/user": return FakeHttp.json200(giteeUser)
        case "/api/v5/user/repos": return FakeHttp.json200(giteeRepos)
        case "/api/v5/user/issues":
            let created = request.url?.query?.contains("filter=created") ?? false
            return FakeHttp.json200(created ? giteeIssuesMine : giteeIssuesAssigned)
        default: return nil
        }
    }

    private func gitea(_ http: FakeHttp, _ store: MemorySecrets? = nil) -> GiteaHost {
        GiteaHost(store: store ?? secrets, settings: settings, http: http, clock: { [clock] in clock.now })
    }

    private func bitbucket(_ http: FakeHttp, _ store: MemorySecrets? = nil) -> BitbucketHost {
        BitbucketHost(store: store ?? secrets, settings: settings, http: http, clock: { [clock] in clock.now })
    }

    private func gitee(_ http: FakeHttp, _ store: MemorySecrets? = nil) -> GiteeHost {
        GiteeHost(store: store ?? secrets, settings: settings, http: http, clock: { [clock] in clock.now })
    }

    private func logText() -> String {
        (try? String(contentsOfFile: Log.filePath, encoding: .utf8)) ?? ""
    }

    // ---- Gitea

    func testGiteaMapsItsReplyAndOnlyEverReads() async throws {
        let http = FakeHttp(serveGitea)
        let host = gitea(http)

        let result = await host.refresh()

        XCTAssertEqual(result, GitHubRefreshResult(result: .ok, changed: true))
        XCTAssertEqual(host.domain, "codeberg.org")
        let page = try XCTUnwrap(host.page)
        XCTAssertEqual(
            page.body,
            [
                "H\tgitea\tana\tAna B\t12\t3\t1", "P\tana/new\tp\tRust\t5\t0\t1", "P\tana/old\t\tGo\t1\t2\t0",
                "M\tana/new\t4\tFix\td\tnone\topen\t\(nov2)", "A\tana/old\t9\tBug\ti\tnone\topen\t\(nov3)",
            ].joined(separator: "\n"))
        XCTAssertEqual(
            page.urls,
            [
                "https://codeberg.org/ana/new", "https://codeberg.org/ana/old", "https://codeberg.org/ana/new/pulls/4",
                "https://codeberg.org/ana/old/issues/9",
            ])
        for seen in http.seen {
            XCTAssertEqual(seen.method, "GET")
            XCTAssertTrue(seen.url.hasPrefix("https://codeberg.org/api/v1/"), seen.url)
            XCTAssertEqual(seen.headers["Authorization"], "token " + token)
            XCTAssertFalse(seen.url.contains(token))
        }
    }

    func testGiteaReviewStaysEmptyWhenTheServerDoesNotKnowIt() async throws {
        let http = FakeHttp { request in
            if request.url?.query?.contains("review_requested") ?? false { return HttpReply(status: 400) }
            return self.serveGitea(request)
        }
        let host = gitea(http)

        let result = await host.refresh()

        XCTAssertEqual(result.result, .ok)
        XCTAssertFalse("\n\(try XCTUnwrap(host.page).body)".contains("\nR\t"))
    }

    func testGiteaTalksToTheConfiguredHttpsHostOnly() async throws {
        settings.set(GitTokens.urlKey("gitea"), "https://git.example.org/forge/")
        let http = FakeHttp(serveGitea)
        let host = gitea(http)

        let result = await host.refresh()

        XCTAssertEqual(result.result, .ok)
        XCTAssertEqual(host.domain, "git.example.org")
        for seen in http.seen {
            XCTAssertEqual(seen.host, "git.example.org")
            XCTAssertTrue(seen.url.hasPrefix("https://git.example.org/forge/api/v1/"), seen.url)
        }
        // The replies name codeberg.org, which is not this host's domain, so none of their links may ever be opened.
        XCTAssertTrue(try XCTUnwrap(host.page).urls.allSatisfy { $0.isEmpty })
    }

    func testANonHttpsOrOddBaseUrlIsRejectedAndNothingIsAskedOrSaved() async {
        let urls = [
            "http://git.example.org", "ftp://git.example.org", "git.example.org", "//git.example.org",
            "https://user:pass@git.example.org", "https://git.example.org/?a=b", "https://git.example.org/#x",
            "javascript:alert(1)",
        ]
        for url in urls {
            XCTAssertNil(GitTokens.giteaApi(url), url)
            let http = FakeHttp(serveGitea)
            let host = gitea(http, MemorySecrets())

            let saved = await host.save(token: token, url: url, user: "")

            XCTAssertEqual(saved.status, .badUrl, url)
            XCTAssertTrue(http.seen.isEmpty, url)
            XCTAssertEqual(settings.text(GitTokens.urlKey("gitea")), "")
        }
    }

    func testAGoodBaseUrlBecomesTheApiRoot() {
        let forge = GitTokens.giteaApi(" https://git.example.org/forge/ ")
        XCTAssertEqual(forge?.absoluteString, "https://git.example.org/forge/api/v1/")
        XCTAssertEqual(GitTokens.giteaApi("https://codeberg.org")?.absoluteString, "https://codeberg.org/api/v1/")
    }

    // ---- Bitbucket

    func testBitbucketShowsWhatItHasAndLeavesTheRestEmpty() async throws {
        let http = FakeHttp(serveBitbucket)
        let host = bitbucket(http)

        let result = await host.refresh()

        XCTAssertEqual(result.result, .ok)
        let page = try XCTUnwrap(host.page)
        XCTAssertEqual(
            page.body,
            [
                "H\tbitbucket\tana\tAna B\t2\t0\t1", "P\tws/new\tp\t\t0\t0\t0", "P\tws/old\t\tpython\t0\t0\t0",
                "M\tws/new\t12\tShip\td\tnone\topen\t\(nov2)",
            ].joined(separator: "\n"))
        XCTAssertEqual(
            page.urls,
            [
                "https://bitbucket.org/ws/new", "https://bitbucket.org/ws/old",
                "https://bitbucket.org/ws/new/pull-requests/12",
            ])
        let basic = "Basic " + Data(("me@example.org:" + token).utf8).base64EncodedString()
        for seen in http.seen {
            XCTAssertEqual(seen.method, "GET")
            XCTAssertEqual(seen.host, "api.bitbucket.org")
            XCTAssertEqual(seen.headers["Authorization"], basic)
        }
        XCTAssertTrue(http.seen.contains { $0.url.contains("/2.0/workspaces/ws/pullrequests/557058%3Aabc") })
    }

    func testBitbucketWithoutAUserNameAsksNobody() async {
        settings.set(GitTokens.userKey("bitbucket"), "")
        let http = FakeHttp(serveBitbucket)

        let result = await bitbucket(http).refresh()

        XCTAssertEqual(result.result, .notSignedIn)
        XCTAssertTrue(http.seen.isEmpty)
    }

    func testBitbucketSavingNeedsAUserNameAndKeepsItInSettingsOnly() async {
        let store = MemorySecrets()
        let http = FakeHttp(serveBitbucket)
        let host = bitbucket(http, store)

        let colon = await host.save(token: token, url: "", user: "a:b")
        let empty = await host.save(token: token, url: "", user: "")
        XCTAssertEqual(colon.status, .badUser)
        XCTAssertEqual(empty.status, .badUser)
        XCTAssertTrue(http.seen.isEmpty)

        let saved = await host.save(token: token, url: "", user: "you@example.org")
        XCTAssertEqual(saved.status, .saved)
        XCTAssertEqual(settings.text(GitTokens.userKey("bitbucket")), "you@example.org")
        XCTAssertEqual(store.get(GitTokens.tokenName("bitbucket")), token)
    }

    // ---- Gitee

    func testGiteeShowsTheProfileAndRepositoriesAndNeverInventsItems() async throws {
        let http = FakeHttp(serveGitee)
        let host = gitee(http)

        let result = await host.refresh()

        XCTAssertEqual(result.result, .ok)
        let page = try XCTUnwrap(host.page)
        XCTAssertEqual(
            page.body,
            [
                "H\tgitee\tana\tAna B\t9\t4\t1", "P\tana/zh\t\tJava\t8\t3\t0",
                "M\tana/zh\tI1ABCD\tBug\ti\tnone\topen\t\(nov2)", "A\tana/zh\tI2XYZ\tFix\ti\tnone\topen\t\(nov3)",
            ].joined(separator: "\n"))
        XCTAssertEqual(
            page.urls,
            [
                "https://gitee.com/ana/zh", "https://gitee.com/ana/zh/issues/I1ABCD",
                "https://gitee.com/ana/zh/issues/I2XYZ",
            ])
        for seen in http.seen {
            XCTAssertEqual(seen.method, "GET")
            XCTAssertEqual(seen.host, "gitee.com")
            XCTAssertNil(seen.headers["Authorization"])
            XCTAssertTrue(seen.url.contains("access_token=" + token), "the documented token parameter")
        }
    }

    func testAFailingGiteeRequestNeverPutsItsAddressOrTokenInAnErrorOrTheLog() async throws {
        let host = gitee(FakeHttp { _ in nil })
        let result = await host.refresh()
        XCTAssertEqual(result.result, .failed)

        let refused = try XCTUnwrap(URL(string: "https://gitee.com/api/v5/user?access_token=" + token))
        var put = URLRequest(url: refused)
        put.httpMethod = "PUT"
        XCTAssertThrowsError(try ReadOnlyGate.verify(put)) { error in
            XCTAssertFalse(String(describing: error).contains(self.token))
            XCTAssertFalse(String(describing: error).contains("access_token"))
        }
        XCTAssertFalse(logText().contains(token))
    }

    // ---- shared behaviour

    func testTheGateRefusesEveryWritingRequestToEveryHost() throws {
        let urls = [
            "https://codeberg.org/api/v1/repos/a/b/issues", "https://api.bitbucket.org/2.0/repositories/a/b",
            "https://gitee.com/api/v5/repos/a/b/issues",
        ]
        for method in ["PUT", "POST", "PATCH", "DELETE"] {
            for url in urls {
                var request = URLRequest(url: try XCTUnwrap(URL(string: url + "?access_token=" + token)))
                request.httpMethod = method
                request.httpBody = Data("{}".utf8)
                XCTAssertFalse(ReadOnlyGate.allows(request), method + " " + url)
                XCTAssertThrowsError(try ReadOnlyGate.verify(request)) { error in
                    XCTAssertFalse(String(describing: error).contains(self.token))
                }
            }
        }
    }

    func testAPlainHttpRequestIsRefusedByTheGate() throws {
        let request = URLRequest(url: try XCTUnwrap(URL(string: "http://codeberg.org/api/v1/user")))
        XCTAssertFalse(ReadOnlyGate.allows(request))
    }

    func testA401DeletesTheTokenAndClearsThePage() async {
        var refuse = false
        let http = FakeHttp { request in refuse ? HttpReply(status: 401) : self.serveGitee(request) }
        let host = gitee(http)
        let first = await host.refresh()
        XCTAssertEqual(first.result, .ok)
        XCTAssertNotNil(host.page)

        refuse = true
        clock.advance(61)
        let result = await host.refresh()

        XCTAssertEqual(result, GitHubRefreshResult(result: .noPermission, changed: true))
        XCTAssertNil(secrets.get(GitTokens.tokenName("gitee")))
        XCTAssertNil(host.page)
        let after = await host.refresh()
        XCTAssertEqual(after.result, .notSignedIn)
    }

    func testA403WithoutARetryAfterKeepsTheTokenAndReportsNoPermission() async {
        let host = gitea(FakeHttp { _ in HttpReply(status: 403) })

        let result = await host.refresh()

        XCTAssertEqual(result.result, .noPermission)
        XCTAssertEqual(secrets.get(GitTokens.tokenName("gitea")), token)
    }

    func testA429BacksOffForAtLeastAMinuteAndNeverLongerThanAnHour() async {
        let http = FakeHttp { _ in HttpReply(status: 429) }
        let host = bitbucket(http)

        let first = await host.refresh()
        XCTAssertEqual(first.result, .rateLimited)
        XCTAssertEqual(http.seen.count, 1)

        clock.advance(59)
        let waiting = await host.refresh()
        XCTAssertEqual(waiting.result, .rateLimited)
        XCTAssertEqual(http.seen.count, 1)

        clock.advance(2)
        _ = await host.refresh()
        XCTAssertEqual(http.seen.count, 2)

        let slow = FakeHttp { _ in HttpReply(status: 429, headers: ["Retry-After": "999999"]) }
        let patient = bitbucket(slow)
        _ = await patient.refresh()
        clock.advance(59 * 60)
        _ = await patient.refresh()
        XCTAssertEqual(slow.seen.count, 1)
        clock.advance(2 * 60)
        _ = await patient.refresh()
        XCTAssertEqual(slow.seen.count, 2)
    }

    func testAFailurePartWayKeepsTheLastGoodPage() async {
        var fail = false
        let http = FakeHttp { request in
            if fail && (request.url?.path.hasSuffix("/user/repos") ?? false) { return HttpReply(status: 500) }
            return self.serveGitee(request)
        }
        let host = gitee(http)
        _ = await host.refresh()
        let good = host.page

        fail = true
        clock.advance(61)
        let result = await host.refresh()

        XCTAssertEqual(result, GitHubRefreshResult(result: .failed, changed: false))
        XCTAssertEqual(host.page, good)
    }

    func testARefreshWithinAMinuteOfTheLastAsksNobodyAndOneAfterItDoes() async {
        let http = FakeHttp(serveGitea)
        let host = gitea(http)
        let first = await host.refresh()
        XCTAssertEqual(first.result, .ok)
        let asked = http.seen.count

        // What a phone toggling the watch bit does: a restarted timer fires at once, again and again.
        for _ in 0..<5 {
            clock.advance(10)
            let again = await host.refresh()
            XCTAssertEqual(again.result, .rateLimited)
        }
        XCTAssertEqual(http.seen.count, asked)

        clock.advance(11)
        let later = await host.refresh()
        XCTAssertEqual(later.result, .ok)
        XCTAssertGreaterThan(http.seen.count, asked)
    }

    func testTheRefreshRightAfterASuccessfulSaveIsNotHeldBack() async {
        let http = FakeHttp(serveGitea)
        let host = gitea(http)
        let first = await host.refresh()
        XCTAssertEqual(first.result, .ok)

        let saved = await host.save(token: token, url: "https://codeberg.org", user: "")
        XCTAssertEqual(saved.status, .saved)
        let asked = http.seen.count

        let after = await host.refresh()
        XCTAssertEqual(after.result, .ok)
        XCTAssertGreaterThan(http.seen.count, asked)
    }

    func testAFailedTokenSaveLeavesNoTokenBesideTheNewAddress() async {
        settings.set(GitTokens.urlKey("gitea"), "https://old.example.org")
        let http = FakeHttp(serveGitea)
        let host = gitea(http)
        secrets.refuse = true

        let other = "pat_AnotherTokenForTheNewHost12345"
        let saved = await host.save(token: other, url: "https://new.example.org", user: "")

        XCTAssertEqual(saved.status, .failed)
        XCTAssertNil(secrets.get(GitTokens.tokenName("gitea")))
        XCTAssertFalse(host.isSignedIn)
        let before = http.seen.count
        let next = await host.refresh()
        XCTAssertEqual(next.result, .notSignedIn)
        XCTAssertEqual(http.seen.count, before)
    }

    func testA401ForAnOldTokenKeepsANewerOneSavedWhileTheRequestWasOut() async {
        let newer = "pat_NewerTokenValue1234567890abc"
        let http = FakeHttp { [secrets] _ in
            _ = secrets.set(GitTokens.tokenName("gitee"), newer)
            return HttpReply(status: 401)
        }

        let result = await gitee(http).refresh()

        XCTAssertEqual(result.result, .noPermission)
        XCTAssertEqual(secrets.get(GitTokens.tokenName("gitee")), newer)
    }

    func testAnAddressWhoseHostHasAPercentIsRefusedBeforeAnyRequest() async {
        for url in [
            "https://evil.example%23.codeberg.org", "https://evil.example%2f.codeberg.org/forge",
            "https://codeberg.org%40evil.example",
        ] {
            XCTAssertNil(GitTokens.giteaApi(url), url)
            let http = FakeHttp(serveGitea)

            let saved = await gitea(http, MemorySecrets()).save(token: token, url: url, user: "")

            XCTAssertEqual(saved.status, .badUrl, url)
            XCTAssertTrue(http.seen.isEmpty, url)
        }
    }

    func testALongLoginIsCutBeforeItIsShownOrKept() async {
        let long = String(repeating: "a", count: 100)
        let http = FakeHttp { _ in FakeHttp.json200("{\"id\":7,\"login\":\"" + long + "\"}") }

        let saved = await gitea(http, MemorySecrets()).save(token: token, url: "https://codeberg.org", user: "")

        XCTAssertEqual(saved.status, .saved)
        XCTAssertEqual(saved.login.count, 39)
    }

    func testNoTokenAsksNobody() async {
        let http = FakeHttp(serveGitea)

        let result = await gitea(http, MemorySecrets()).refresh()

        XCTAssertEqual(result.result, .notSignedIn)
        XCTAssertTrue(http.seen.isEmpty)
    }

    func testSavingChecksTheTokenAndMapsEachRefusal() async {
        let cases: [(Int, GitSaveStatus)] = [(401, .unauthorized), (403, .forbidden), (404, .notFound), (500, .failed)]
        for (code, expected) in cases {
            let store = MemorySecrets()
            let http = FakeHttp { _ in HttpReply(status: code) }

            let saved = await gitee(http, store).save(token: token, url: "", user: "")

            XCTAssertEqual(saved, GitSaveResult(status: expected, login: ""), String(code))
            XCTAssertNil(store.get(GitTokens.tokenName("gitee")))
            XCTAssertEqual(http.seen.count, 1)
            XCTAssertEqual(http.seen.first?.method, "GET")
        }
    }

    func testSavingWhenTheNetworkIsDownSaysSoAndKeepsTheTokenOutOfTheLog() async {
        let store = MemorySecrets()

        let saved = await gitee(FakeHttp { _ in nil }, store).save(token: token, url: "", user: "")

        XCTAssertEqual(saved.status, .network)
        XCTAssertNil(store.get(GitTokens.tokenName("gitee")))
        XCTAssertFalse(logText().contains(token))
    }

    func testARefreshWhenTheNetworkIsDownKeepsTheTokenOutOfTheLog() async {
        let http = FakeHttp { _ in nil }
        let hosts: [GitHost] = [gitea(http), bitbucket(http), gitee(http)]
        for host in hosts {
            let result = await host.refresh()
            XCTAssertEqual(result.result, .failed, host.id)
        }
        XCTAssertFalse(logText().contains(token))
    }

    func testASavedTokenGoesToTheSecretStoreAndNeverToTheSettingsFile() async throws {
        let store = MemorySecrets()
        let host = gitea(FakeHttp(serveGitea), store)

        let saved = await host.save(token: " " + token + " ", url: "https://codeberg.org/", user: "")

        XCTAssertEqual(saved, GitSaveResult(status: .saved, login: "ana"))
        XCTAssertEqual(store.get(GitTokens.tokenName("gitea")), token)
        XCTAssertEqual(settings.text(GitTokens.urlKey("gitea")), "https://codeberg.org")
        let file = try String(contentsOfFile: folder + "/" + IntegrationSettings.fileName, encoding: .utf8)
        XCTAssertFalse(file.contains(token))
        XCTAssertTrue(host.isSignedIn)

        host.signOut()
        XCTAssertFalse(host.isSignedIn)
        XCTAssertNil(host.page)
    }

    func testABadTokenIsRefusedBeforeAnyRequest() async {
        for bad in ["", "short", "has space in it", "tab\there123"] {
            let http = FakeHttp(serveGitee)

            let saved = await gitee(http, MemorySecrets()).save(token: bad, url: "", user: "")

            XCTAssertEqual(saved.status, .badToken, bad)
            XCTAssertTrue(http.seen.isEmpty)
        }
    }
}
