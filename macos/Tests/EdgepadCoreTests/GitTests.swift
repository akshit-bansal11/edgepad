import XCTest

@testable import EdgepadCore

/// Counts the requests that actually reached the (stubbed) network.
private final class GateCounter: @unchecked Sendable {
    private let lock = NSLock()
    private var count = 0

    var value: Int { lock.withLock { count } }

    func add() { lock.withLock { count += 1 } }
}

private final class GateStub: URLProtocol {
    static let counter = GateCounter()

    override class func canInit(with request: URLRequest) -> Bool { true }

    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        Self.counter.add()
        guard let url = request.url,
            let response = HTTPURLResponse(url: url, statusCode: 200, httpVersion: nil, headerFields: nil)
        else { return }
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: Data("{}".utf8))
        client?.urlProtocolDidFinishLoading(self)
    }

    override func stopLoading() {}
}

private final class StubHost: GitHost {
    let id: String
    let domain: String
    let page: GitPage?

    init(_ id: String, _ domain: String, _ page: GitPage?) {
        self.id = id
        self.domain = domain
        self.page = page
    }

    func refresh() async -> GitHubRefreshResult {
        GitHubRefreshResult(result: .ok, changed: false)
    }
}

final class GitTests: XCTestCase {
    private let query = #"{"query":"query { viewer { login } }"}"#

    private func request(_ method: String, _ url: String, _ body: String? = nil) throws -> URLRequest {
        var request = URLRequest(url: try XCTUnwrap(URL(string: url)))
        request.httpMethod = method
        request.httpBody = body.map { Data($0.utf8) }
        return request
    }

    // ---- ReadOnlyGate

    func testTheGateRefusesEveryWritingMethod() throws {
        for method in ["POST", "PUT", "PATCH", "DELETE"] {
            let write = try request(method, "https://api.github.com/repos/o/r/issues/1", "{}")
            XCTAssertFalse(ReadOnlyGate.allows(write), method)
            XCTAssertThrowsError(try ReadOnlyGate.verify(write), method)
        }
    }

    func testTheGateRefusesAGraphQlPostThatCouldMutateOrIsNotAQuery() throws {
        let bodies = [
            #"{"query":"mutation { addStar(input: {starrableId: \"x\"}) { clientMutationId } }"}"#,
            #"{"query":"  MUTATION M { x }"}"#, #"{"query":"mutation { x }"}"#,
            #"{"query":"query { a } mutation { b }"}"#, #"{"query":"query { a } mutation { b }"}"#,
            #"{"operationName":"x"}"#, #"{"query":7}"#, "[]", "not json", "",
        ]
        for body in bodies {
            let post = try request("POST", "https://api.github.com/graphql", body)
            XCTAssertFalse(ReadOnlyGate.allows(post), body)
        }
        XCTAssertFalse(ReadOnlyGate.allows(try request("POST", "https://api.github.com/graphql")))
    }

    func testTheGateAllowsAGraphQlQueryAndOurOwnQueryHasNoMutation() throws {
        XCTAssertTrue(ReadOnlyGate.allows(try request("POST", "https://api.github.com/graphql", query)))
        let own = String(decoding: GitHubApi.queryBody(), as: UTF8.self)
        XCTAssertTrue(ReadOnlyGate.allows(try request("POST", "https://api.github.com/graphql", own)))
        XCTAssertFalse(GitHubApi.query.lowercased().contains("mutation"))
    }

    func testTheGateAllowsReadingAndTheDeviceFlowPosts() throws {
        for (method, url) in [
            ("GET", "https://api.github.com/user"), ("HEAD", "https://api.github.com/user"),
            ("GET", "https://gitlab.com/api/v4/user"),
        ] {
            XCTAssertTrue(ReadOnlyGate.allows(try request(method, url)), url)
        }
        for url in [
            "https://github.com/login/device/code", "https://github.com/login/oauth/access_token",
            "https://gitlab.com/oauth/authorize_device", "https://gitlab.com/oauth/token",
        ] {
            XCTAssertTrue(ReadOnlyGate.allows(try request("POST", url, "client_id=x")), url)
        }
    }

    func testTheGateRefusesAnythingElse() throws {
        let cases = [
            ("POST", "https://github.com/login/oauth/authorize"), ("POST", "https://evil.example/login/device/code"),
            ("POST", "https://evil.example/oauth/token"), ("POST", "https://api.github.com/graphql/extra"),
            ("POST", "https://evil.example/graphql"), ("PUT", "https://github.com/login/device/code"),
            ("POST", "http://github.com/login/device/code"), ("GET", "http://api.github.com/user"),
        ]
        for (method, url) in cases {
            XCTAssertFalse(ReadOnlyGate.allows(try request(method, url, query)), url)
        }
    }

    func testTheRefusalNamesNoQueryString() throws {
        let delete = try request("DELETE", "https://api.github.com/repos/o/r?token=SECRET")
        XCTAssertThrowsError(try ReadOnlyGate.verify(delete)) { error in
            XCTAssertFalse(String(describing: error).contains("SECRET"))
            let expected = ReadOnlyViolation(message: "DELETE api.github.com/repos/o/r is not read only")
            XCTAssertEqual(error as? ReadOnlyViolation, expected)
        }
    }

    func testEveryDashboardRequestPassesTheGate() async throws {
        let config = URLSessionConfiguration.ephemeral
        config.protocolClasses = [GateStub.self]
        let http = CappedHttp(session: URLSession(configuration: config))
        let before = GateStub.counter.value

        let refused = [
            try request("PUT", "https://api.github.com/user/starred/o/r"),
            try request("GET", "http://api.github.com/user"),
        ]
        for write in refused {
            let reply = await http.send(write, maxBytes: 1024, what: "test")
            XCTAssertNil(reply)
        }
        XCTAssertEqual(GateStub.counter.value, before)

        let reply = await http.send(try request("GET", "https://api.github.com/user"), maxBytes: 1024, what: "test")
        XCTAssertEqual(reply?.status, 200)
        XCTAssertEqual(GateStub.counter.value, before + 1)
    }

    // ---- GitBlob

    private func snapshot() -> GitSnapshot {
        GitSnapshot(
            profile: GitProfile(login: "octo", name: "Octo Cat", repos: 34, followers: 12),
            repos: [
                GitRepo(
                    fullName: "octo/edgepad", isPrivate: true, language: "C#", stars: 5, openIssues: 2, openPulls: 1,
                    url: "https://github.com/octo/edgepad")
            ],
            items: [
                GitItem(
                    section: .mine, repo: "octo/edgepad", number: 3, title: "Fix it", state: GitState.merged,
                    isPull: true, draft: true, ci: "fail", updatedMin: 99,
                    url: "https://github.com/octo/edgepad/pull/3"),
                GitItem(
                    section: .assigned, repo: "octo/edgepad", number: 4, title: "Bug", state: GitState.open,
                    isPull: false, draft: false, ci: "none", updatedMin: 0,
                    url: "https://github.com/octo/edgepad/issues/4"),
            ])
    }

    private func rows(_ page: GitPage) -> [[String]] {
        page.body.split(separator: "\n", omittingEmptySubsequences: false).map {
            $0.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
        }
    }

    func testThePageHasTheHeaderThenReposThenTheGroupsAndOneAddressPerIndexedLine() {
        let page = GitBlob.encode("github", snapshot(), 5)
        let lines = rows(page)

        XCTAssertEqual(lines[0], ["H", "github", "octo", "Octo Cat", "34", "12", "5"])
        XCTAssertEqual(lines[1], ["P", "octo/edgepad", "p", "C#", "5", "2", "1"])
        XCTAssertEqual(lines[2], ["M", "octo/edgepad", "3", "Fix it", "d", "fail", "merged", "99"])
        XCTAssertEqual(lines[3], ["A", "octo/edgepad", "4", "Bug", "i", "none", "open", "0"])
        XCTAssertEqual(
            page.urls,
            [
                "https://github.com/octo/edgepad", "https://github.com/octo/edgepad/pull/3",
                "https://github.com/octo/edgepad/issues/4",
            ])
        XCTAssertEqual(page.rev, 5)
        XCTAssertFalse(page.body.contains("https"))
    }

    func testAnUnreadCountIsAnOlderStyleLineThatDoesNotTakeAnIndex() {
        var withUnread = snapshot()
        withUnread.unread = 7
        let page = GitBlob.encode("github", withUnread, 1)
        XCTAssertEqual(page.body.split(separator: "\n", omittingEmptySubsequences: false)[1], "N\t\t7\t\t\tnone")
        XCTAssertEqual(page.urls.count, 3)
    }

    func testHostileTextIsCleanedAndCutAndABadRepositoryIsDropped() {
        let hostile = "a|b\tc\nd\u{202E}X" + String(repeating: "x", count: 10 * 1024)
        let result = GitSnapshot(
            profile: GitProfile(login: "octo", name: hostile, repos: 1, followers: 1),
            repos: [
                GitRepo(
                    fullName: "bad repo/name", isPrivate: false, language: "C#", stars: 1, openIssues: 1, openPulls: 1,
                    url: "https://github.com/a/b"),
                GitRepo(
                    fullName: "ok/repo", isPrivate: false, language: hostile, stars: 1, openIssues: 1, openPulls: 1,
                    url: "https://github.com/ok/repo"),
            ],
            items: [
                GitItem(
                    section: .mine, repo: "ok/repo", number: 1, title: hostile, state: "weird", isPull: true,
                    draft: false, ci: "weird", updatedMin: 1, url: "https://github.com/ok/repo/pull/1")
            ])

        let page = GitBlob.encode("github", result, 1)
        let lines = rows(page)

        XCTAssertEqual(lines.map { $0.count }, [7, 7, 8])
        let hidden = lines.joined().joined().unicodeScalars.contains {
            $0.properties.generalCategory == .control || $0.value == 0x202E
        }
        XCTAssertFalse(hidden)
        XCTAssertEqual(lines[2][3].unicodeScalars.count, GitLimits.maxTitle)
        XCTAssertEqual(lines[1][3].unicodeScalars.count, GitLimits.maxLanguage)
        XCTAssertEqual(lines[0][3].unicodeScalars.count, GitLimits.maxName)
        XCTAssertEqual(lines[2][5], "none")
        XCTAssertEqual(lines[2][6], "open")
        XCTAssertEqual(page.urls, ["https://github.com/ok/repo", "https://github.com/ok/repo/pull/1"])
    }

    func testAtMostThirtyReposAndTwentyItemsPerGroupAreSent() {
        let repos = (1...50).map {
            GitRepo(
                fullName: "o/r\($0)", isPrivate: false, language: "C#", stars: $0, openIssues: 0, openPulls: 0,
                url: "https://github.com/o/r\($0)")
        }
        var items: [GitItem] = []
        for section in [GitSection.mine, .review, .assigned] {
            for number in 1...30 {
                items.append(
                    GitItem(
                        section: section, repo: "o/r1", number: Int64(number), title: "t", state: GitState.open,
                        isPull: true, draft: false, ci: "ok", updatedMin: 1,
                        url: "https://github.com/o/r1/pull/\(number)"))
            }
        }
        let profile = GitProfile(login: "octo", name: "", repos: 50, followers: 0)

        let page = GitBlob.encode("github", GitSnapshot(profile: profile, repos: repos, items: items), 1)
        let letters = page.body.split(separator: "\n").compactMap { $0.first }

        XCTAssertEqual(letters.filter { $0 == "P" }.count, GitLimits.maxRepos)
        for letter in ["M", "R", "A"] {
            XCTAssertEqual(letters.filter { String($0) == letter }.count, GitLimits.maxItemsPerGroup, letter)
        }
        XCTAssertEqual(page.urls.count, GitLimits.maxRepos + 3 * GitLimits.maxItemsPerGroup)
    }

    func testWhenThePageIsTooBigTheLastLinesGoFirstAndEveryIndexStillHasItsAddress() {
        let fat = String(repeating: "\u{1F600}", count: GitLimits.maxTitle)
        let repo = "o/" + String(repeating: "r", count: 98)
        let repos = (1...30).map {
            GitRepo(
                fullName: repo, isPrivate: false, language: fat, stars: $0, openIssues: 0, openPulls: 0,
                url: "https://github.com/o/r\($0)")
        }
        var items: [GitItem] = []
        for section in [GitSection.mine, .review, .assigned] {
            for number in 1...20 {
                items.append(
                    GitItem(
                        section: section, repo: repo, number: Int64(number), title: fat, state: GitState.open,
                        isPull: true, draft: false, ci: "ok", updatedMin: 1,
                        url: "https://github.com/\(section)/\(number)"))
            }
        }
        let profile = GitProfile(login: "octo", name: "n", repos: 1, followers: 1)

        let page = GitBlob.encode("github", GitSnapshot(profile: profile, repos: repos, items: items), 1)
        let lines = page.body.split(separator: "\n")

        XCTAssertTrue((1...GitLimits.maxBytes).contains(page.body.utf8.count))
        XCTAssertEqual(lines[0].first, "H")
        XCTAssertTrue(lines.count < 1 + 30 + 60)
        XCTAssertEqual(page.urls.count, lines.count - 1)
        XCTAssertEqual(page.urls.first, "https://github.com/o/r1")

        // Order is kept: nothing is skipped to make room, so a dropped line is always at the end.
        let order = "HPMRA"
        let positions = lines.compactMap { $0.first.flatMap { order.firstIndex(of: $0) } }
        XCTAssertEqual(positions, positions.sorted())
    }

    func testTheRevisionOnlyGoesUpWhenTheContentChangesAndNeverGoesBack() {
        let book = GitBook(host: "github")
        XCTAssertNil(book.page)

        let first = book.update(snapshot())
        XCTAssertEqual(first.rev, 1)
        XCTAssertEqual(book.update(snapshot()), first)

        let emptied = GitSnapshot(profile: snapshot().profile, repos: snapshot().repos, items: [])
        XCTAssertEqual(book.update(emptied).rev, 2)

        book.clear()
        XCTAssertNil(book.page)
        XCTAssertEqual(book.update(snapshot()).rev, 3)
    }

    func testANewAddressAloneIsANewRevision() {
        let book = GitBook(host: "github")
        let first = book.update(snapshot())
        let moved = GitSnapshot(
            profile: snapshot().profile,
            repos: [
                GitRepo(
                    fullName: "octo/edgepad", isPrivate: true, language: "C#", stars: 5, openIssues: 2, openPulls: 1,
                    url: "https://github.com/octo/edgepad-renamed")
            ], items: snapshot().items)
        XCTAssertEqual(book.update(moved).rev, first.rev + 1)
    }

    // ---- GitOpenGate

    private func hosts(_ page: GitPage) -> (String) -> GitHost? {
        { id in id == "github" ? StubHost(id, "github.com", page) : nil }
    }

    private func page(_ rev: Int64, _ urls: String...) -> GitPage {
        GitPage(rev: rev, body: "H", urls: urls)
    }

    func testOnlyTheFetchedAddressForTheCurrentRevisionAndIndexOpens() {
        let fetched = page(
            4, "https://github.com/octo/edgepad", "https://github.com/octo/edgepad/pull/3",
            "https://github.com/octo/edgepad/issues/4")
        let cases = [
            ("github/4/0", "https://github.com/octo/edgepad"),
            ("github/4/2", "https://github.com/octo/edgepad/issues/4"),
        ]
        for (wire, expected) in cases {
            XCTAssertEqual(GitOpenGate.resolve(wire, hosts: hosts(fetched)), URL(string: expected), wire)
        }
    }

    func testAStaleRevisionAnIndexOutOfRangeAnUnknownHostOrAnyOtherShapeOpensNothing() {
        let fetched = page(4, "https://github.com/octo/edgepad")
        let wires: [String?] = [
            "github/3/0", "github/5/0", "github/4/1", "github/4/-1", "github/4/x", "github/x/0", "github/4/",
            "github/4", "github/4/0/0", "gitlab/4/0", "/4/0", "github//0",
            "github/4/" + String(repeating: "0", count: 100), "", nil,
        ]
        for wire in wires {
            XCTAssertNil(GitOpenGate.resolve(wire, hosts: hosts(fetched)), wire ?? "nil")
        }
    }

    func testAnAddressThatIsNotHttpsOnTheHostsOwnDomainNeverOpens() {
        let addresses = [
            "http://github.com/octo/edgepad", "https://evil.example/octo/edgepad",
            "https://github.com.evil.example/octo/edgepad", "https://user@github.com/octo/edgepad",
            "https://github.com:8443/octo/edgepad", "ftp://github.com/octo/edgepad", "javascript:alert(1)",
            "/octo/edgepad", "",
        ]
        for address in addresses {
            XCTAssertNil(GitOpenGate.resolve("github/4/0", hosts: hosts(page(4, address))), address)
        }
    }

    func testAHostWithNoPageOpensNothing() {
        XCTAssertNil(GitOpenGate.resolve("github/4/0") { StubHost($0, "github.com", nil) })
    }
}
