import Foundation

/// Gitee, `https://gitee.com/api/v5`. The person pastes a personal access token. The reference
/// (https://gitee.com/api/v5/swagger) documents the token only as the `access_token` query parameter, so that is what
/// is sent (an Authorization header is not documented). Because the token is then in the address, no message, error
/// text or log line may carry a request address. Read endpoints: `GET /user`, `GET /user/repos` and
/// `GET /user/issues?filter=created|assigned` (Mine and Assigned; `number` is a text id such as `I1ABCD`, sent as the
/// item's label). Pull requests stay empty: the reference has no user-wide pull request list. Stars and open-issue
/// counts come from the repository list; its open pull request count is not in it and stays 0. The reference documents
/// `public` (not `private`) on a repository. The token's required scopes are unverified. The twin of the laptop core's
/// GiteeHost.
public final class GiteeHost: TokenGitHost {
    public static let apiRoot = "https://gitee.com/api/v5/"
    public static let webDomain = "gitee.com"

    public init(
        store: SecretStore, settings: IntegrationSettings, http: HttpSender = CappedHttp(),
        clock: @escaping () -> Date = Date.init
    ) {
        super.init(spec: GiteeSpec(), store: store, settings: settings, http: http, clock: clock)
    }
}

private struct GiteeSpec: TokenGitSpec {
    let id = "gitee"

    func domain(_ api: URL?) -> String { GiteeHost.webDomain }

    func apiFor(_ configured: String) -> URL? { URL(string: GiteeHost.apiRoot) }

    func authorization(token: String, user: String) -> String? { nil }

    // The token travels in the address, so the address must never reach a message: `CappedHttp` logs only the error
    // domain and code, `ReadOnlyGate` names host and path only, and no code here formats a request address.
    func sign(_ url: URL, token: String) -> URL {
        let joined = url.absoluteString + (url.query == nil ? "?" : "&") + "access_token=" + UrlText.escape(token)
        return URL(string: joined) ?? url
    }

    func login(_ body: Data) -> String? { GitJson.text(GitJson.parse(body), "login") }

    func read(_ get: GitGet, api: URL) async -> GitRead {
        let who = await get(userPath)
        if who.result != .ok {
            return GitRead(snapshot: nil, failure: who)
        }
        let user = GitJson.parse(who.body)
        guard user != nil, let login = GitJson.text(user, "login") else {
            return GitRead(snapshot: nil, failure: .unusable)
        }
        let repoReply = await get("user/repos?sort=updated&direction=desc&per_page=\(GitLimits.maxRepos)&page=1")
        if repoReply.result != .ok {
            return GitRead(snapshot: nil, failure: repoReply)
        }
        guard let repoDoc = GitJson.parse(repoReply.body) else {
            return GitRead(snapshot: nil, failure: .unusable)
        }
        // Newest first; two with the same time keep the host's order.
        var recent: [(index: Int, node: [String: Any], updated: Int64)] = []
        for (index, node) in GitJson.items(repoDoc).enumerated() {
            recent.append((index, node, GitJson.minutes(GitJson.text(node, "updated_at"))))
        }
        recent.sort { $0.updated != $1.updated ? $0.updated > $1.updated : $0.index < $1.index }
        let repos = Array(recent.compactMap { Self.repo($0.node) }.prefix(GitLimits.maxRepos))
        let total = GitJson.whole(user, "public_repos")

        let profile = GitProfile(
            login: login, name: GitJson.text(user, "name") ?? "", repos: total > 0 ? total : repos.count,
            followers: GitJson.whole(user, "followers"))
        var items: [GitItem] = []
        for (filter, section) in [("created", GitSection.mine), ("assigned", GitSection.assigned)] {
            let query = "state=open&sort=updated&direction=desc&per_page=\(GitLimits.maxItemsPerGroup)&page=1"
            let reply = await get("user/issues?filter=\(filter)&\(query)")
            if reply.result != .ok {
                return GitRead(snapshot: nil, failure: reply)
            }
            guard let doc = GitJson.parse(reply.body) else {
                return GitRead(snapshot: nil, failure: .unusable)
            }
            items += GitJson.items(doc).prefix(GitLimits.maxItemsPerGroup).compactMap { Self.issue($0, section) }
        }
        return GitRead(snapshot: GitSnapshot(profile: profile, repos: repos, items: items), failure: nil)
    }

    private static func issue(_ node: [String: Any], _ section: GitSection) -> GitItem? {
        guard let repo = GitJson.text(GitJson.child(node, "repository"), "full_name"),
            let id = GitJson.text(node, "number"), (1...20).contains(id.utf8.count),
            id.unicodeScalars.allSatisfy({ GitHubApp.isAsciiLetterOrDigit($0) })
        else { return nil }
        let closed = GitJson.text(node, "state") == "closed" || GitJson.text(node, "state") == "rejected"
        return GitItem(
            section: section, repo: repo, number: 0, title: GitJson.text(node, "title") ?? "",
            state: closed ? GitState.closed : GitState.open, isPull: false, draft: false, ci: CiToken.none.rawValue,
            updatedMin: GitJson.minutes(GitJson.text(node, "updated_at")),
            url: GitJson.url(GitJson.text(node, "html_url"), domain: GiteeHost.webDomain), label: id)
    }

    private static func repo(_ node: [String: Any]) -> GitRepo? {
        guard let name = GitJson.text(node, "full_name") else { return nil }
        return GitRepo(
            fullName: name, isPrivate: !GitJson.flag(node, "public"), language: GitJson.text(node, "language") ?? "",
            stars: GitJson.whole(node, "stargazers_count"), openIssues: GitJson.whole(node, "open_issues_count"),
            openPulls: 0, url: GitJson.url(GitJson.text(node, "html_url"), domain: GiteeHost.webDomain))
    }
}
