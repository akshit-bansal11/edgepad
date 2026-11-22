import XCTest

@testable import EdgepadCore

final class GitHubParseTests: XCTestCase {
    static let normal = """
        {"data":{
          "viewer":{"login":"octo-cat","name":"Octo Cat","followers":{"totalCount":12},
            "repositories":{"totalCount":34,"nodes":[
              {"nameWithOwner":"octo-cat/edgepad","isPrivate":false,"url":"https://github.com/octo-cat/edgepad","stargazerCount":5,
               "primaryLanguage":{"name":"C#"},"issues":{"totalCount":2},"pullRequests":{"totalCount":1}},
              {"nameWithOwner":"octo-cat/secret","isPrivate":true,"url":"https://github.com/octo-cat/secret","stargazerCount":0,
               "primaryLanguage":null,"issues":{"totalCount":0},"pullRequests":{"totalCount":0}}
            ]}},
          "review":{"nodes":[
            {"__typename":"PullRequest","number":12,"title":"Fix the pairing timeout","isDraft":false,"state":"OPEN",
             "url":"https://github.com/octo/edgepad/pull/12","updatedAt":"2026-11-19T11:00:00Z","repository":{"nameWithOwner":"octo/edgepad"},
             "commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"SUCCESS"}}}]}},
            {"__typename":"PullRequest","number":7,"title":"Draft idea","isDraft":true,"state":"OPEN",
             "url":"https://github.com/octo/other.repo/pull/7","updatedAt":"2026-11-19T11:00:00Z","repository":{"nameWithOwner":"octo/other.repo"},
             "commits":{"nodes":[{"commit":{"statusCheckRollup":null}}]}}
          ]},
          "mine":{"nodes":[
            {"__typename":"PullRequest","number":3,"title":"Mine","isDraft":false,"state":"MERGED",
             "url":"https://github.com/me/my_repo-2/pull/3","updatedAt":"2026-11-19T11:00:00Z","repository":{"nameWithOwner":"me/my_repo-2"},
             "commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"FAILURE"}}}]}},
            {"__typename":"Issue","number":9,"title":"Crash on pair","state":"OPEN",
             "url":"https://github.com/me/my_repo-2/issues/9","updatedAt":"2026-11-19T11:00:00Z","repository":{"nameWithOwner":"me/my_repo-2"}},
            {}
          ]},
          "assigned":{"nodes":[
            {"__typename":"Issue","number":4,"title":"Assigned to me","state":"CLOSED",
             "url":"https://evil.example/octo/edgepad/issues/4","updatedAt":"not a time","repository":{"nameWithOwner":"octo/edgepad"}}
          ]}
        }}
        """

    /// 2026-11-19T11:00:00Z in UTC minutes since the Unix epoch.
    static let updatedMin: Int64 = 1_795_086_000 / 60

    private func snapshot(_ body: String) -> GitSnapshot? {
        GitHubParse.snapshot(Data(body.utf8))
    }

    /// A reply for a plain account whose "mine" search lists `nodes`, comma-separated JSON.
    private func mine(_ nodes: String) -> String {
        #"{"data":{"viewer":{"login":"a"},"mine":{"nodes":["# + nodes + "]}}}"
    }

    func testANormalReplyGivesTheProfileTheReposAndEachGroupOfItems() throws {
        let result = try XCTUnwrap(snapshot(Self.normal))
        let min = Self.updatedMin
        let github = "https://github.com/"

        XCTAssertEqual(result.profile, GitProfile(login: "octo-cat", name: "Octo Cat", repos: 34, followers: 12))
        XCTAssertEqual(
            result.repos,
            [
                GitRepo(
                    fullName: "octo-cat/edgepad", isPrivate: false, language: "C#", stars: 5, openIssues: 2,
                    openPulls: 1, url: github + "octo-cat/edgepad"),
                GitRepo(
                    fullName: "octo-cat/secret", isPrivate: true, language: "", stars: 0, openIssues: 0, openPulls: 0,
                    url: github + "octo-cat/secret"),
            ])
        XCTAssertEqual(
            result.items,
            [
                GitItem(
                    section: .mine, repo: "me/my_repo-2", number: 3, title: "Mine", state: GitState.merged,
                    isPull: true, draft: false, ci: "fail", updatedMin: min, url: github + "me/my_repo-2/pull/3"),
                GitItem(
                    section: .mine, repo: "me/my_repo-2", number: 9, title: "Crash on pair", state: GitState.open,
                    isPull: false, draft: false, ci: "none", updatedMin: min, url: github + "me/my_repo-2/issues/9"),
                GitItem(
                    section: .review, repo: "octo/edgepad", number: 12, title: "Fix the pairing timeout",
                    state: GitState.open, isPull: true, draft: false, ci: "ok", updatedMin: min,
                    url: github + "octo/edgepad/pull/12"),
                GitItem(
                    section: .review, repo: "octo/other.repo", number: 7, title: "Draft idea", state: GitState.open,
                    isPull: true, draft: true, ci: "none", updatedMin: min, url: github + "octo/other.repo/pull/7"),
                // An address off github.com is kept as empty, and a time that does not read as 0.
                GitItem(
                    section: .assigned, repo: "octo/edgepad", number: 4, title: "Assigned to me",
                    state: GitState.closed, isPull: false, draft: false, ci: "none", updatedMin: 0, url: ""),
            ])
    }

    func testAnEmptyAccountGivesNoRowsButStillAProfile() throws {
        let result = try XCTUnwrap(snapshot(#"{"data":{"viewer":{"login":"a"},"mine":{"nodes":[]}}}"#))
        XCTAssertEqual(result.profile, GitProfile(login: "a", name: "", repos: 0, followers: 0))
        XCTAssertTrue(result.repos.isEmpty)
        XCTAssertTrue(result.items.isEmpty)
    }

    func testAReplyWithoutAViewerIsNil() {
        let bodies = [
            #"{"errors":[{"message":"Bad credentials"}]}"#, #"{"data":null}"#, #"{"data":{"mine":{"nodes":[]}}}"#,
            #"{"data":{"viewer":{"login":"evil name"}}}"#, "[]", "not json", "",
        ]
        for body in bodies {
            XCTAssertNil(snapshot(body), body)
        }
    }

    func testHostileTitlesAreCleanedAndCut() throws {
        let escape = String(UnicodeScalar(92))
        let title =
            "Tab" + escape + "there" + escape + "u001b[31mred" + escape + "u001b[0m new" + escape + "nline " + escape
            + "u202eevil" + String(repeating: "x", count: 100)
        let node = #"{"__typename":"Issue","number":1,"title":""# + title + #"","repository":{"nameWithOwner":"a/b"}}"#

        let item = try XCTUnwrap(snapshot(mine(node))?.items.first)

        let hidden = item.title.unicodeScalars.contains {
            $0.properties.generalCategory == .control || $0.value == 0x202E
        }
        XCTAssertFalse(hidden)
        XCTAssertTrue(item.title.hasPrefix("Tab here [31mred [0m new line evil"), item.title)
        XCTAssertEqual(item.title.unicodeScalars.count, GitHubParse.maxTitle)
        XCTAssertEqual(item.ci, "none")
    }

    func testARowWithAnOddRepositoryIsDropped() throws {
        for repo in ["octo/my repo", "octo", "a/b/c", "octo/rep@", "/repo"] {
            let bad = #"{"__typename":"Issue","number":1,"title":"x","repository":{"nameWithOwner":""# + repo + #""}}"#
            let fine = #"{"__typename":"Issue","number":2,"title":"y","repository":{"nameWithOwner":"ok/fine"}}"#
            let items = try XCTUnwrap(snapshot(mine(bad + "," + fine))?.items, repo)
            XCTAssertEqual(items.map { $0.repo }, ["ok/fine"], repo)
        }
    }

    func testARowMissingItsNumberTypeOrRepositoryIsDropped() throws {
        let nodes = [
            #"{"__typename":"Issue","number":0,"title":"x","repository":{"nameWithOwner":"a/b"}}"#,
            #"{"__typename":"Issue","number":-4,"title":"x","repository":{"nameWithOwner":"a/b"}}"#,
            #"{"__typename":"Issue","number":"5","title":"x","repository":{"nameWithOwner":"a/b"}}"#,
            #"{"__typename":"Issue","title":"x","repository":{"nameWithOwner":"a/b"}}"#,
            #"{"__typename":"Issue","number":5,"title":"x","repository":null}"#,
            #"{"__typename":"Repository","number":5,"title":"x","repository":{"nameWithOwner":"a/b"}}"#,
            #"{"number":5,"title":"x","repository":{"nameWithOwner":"a/b"}}"#, "17",
        ]
        for node in nodes {
            let result = try XCTUnwrap(snapshot(mine(node)), node)
            XCTAssertTrue(result.items.isEmpty, node)
        }
    }

    func testAtMostTwentyItemsPerGroupAndThirtyReposAreKept() throws {
        let node = #"{"__typename":"Issue","number":1,"title":"t","repository":{"nameWithOwner":"a/b"}}"#
        let nodes = Array(repeating: node, count: 30).joined(separator: ",")
        let repos = (1...40).map { #"{"nameWithOwner":"a/r\#($0)"}"# }.joined(separator: ",")
        let body =
            #"{"data":{"viewer":{"login":"a","repositories":{"totalCount":40,"nodes":["# + repos
            + #"]}},"mine":{"nodes":["# + nodes + "]}}}"

        let result = try XCTUnwrap(snapshot(body))

        XCTAssertEqual(result.items.count, GitLimits.maxItemsPerGroup)
        XCTAssertEqual(result.repos.count, GitLimits.maxRepos)
        XCTAssertEqual(result.profile.repos, 40)
    }

    func testAReplyNestedPastTheLimitIsRefused() {
        let deep = String(repeating: "[", count: 40) + String(repeating: "]", count: 40)
        XCTAssertNil(snapshot(mine(deep)))
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
