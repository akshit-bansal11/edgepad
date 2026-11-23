import Foundation

/// Gitea, Forgejo and Codeberg (Forgejo's public server): one client for the Gitea-compatible API under `/api/v1`, with
/// the address the person configures (default `codeberg.org`; https only). The token is a personal access token with
/// the read scopes (`read:user`, `read:repository`, `read:issue`), sent as `Authorization: token ...`. Mine, Assigned
/// and Review come from `/repos/issues/search` (`created`, `assigned` and, for pull requests, `review_requested`); a
/// server too old to know `review_requested` answers 400 and Review stays empty. There is no CI state on this API, so
/// every item's CI is none. Verified against https://codeberg.org/swagger.v1.json (Forgejo, Gitea API 1.22). The twin of
/// the laptop core's GiteaHost.
public final class GiteaHost: TokenGitHost {
    public static let defaultUrl = "https://codeberg.org"

    public init(
        store: SecretStore, settings: IntegrationSettings, http: HttpSender = CappedHttp(),
        clock: @escaping () -> Date = Date.init
    ) {
        super.init(spec: GiteaSpec(), store: store, settings: settings, http: http, clock: clock)
    }
}

private struct GiteaSpec: TokenGitSpec {
    let id = "gitea"
    let apiIsFixed = false

    func domain(_ api: URL?) -> String {
        api.flatMap { URLComponents(url: $0, resolvingAgainstBaseURL: false)?.host } ?? ""
    }

    func apiFor(_ configured: String) -> URL? {
        GitTokens.giteaApi(
            configured.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? GiteaHost.defaultUrl : configured)
    }

    func authorization(token: String, user: String) -> String? { "token " + token }

    func login(_ body: Data) -> String? { GitJson.text(GitJson.parse(body), "login") }

    func read(_ get: GitGet, api: URL) async -> GitRead {
        let who = await get(userPath)
        if who.result != .ok {
            return GitRead(snapshot: nil, failure: who)
        }
        let user = GitJson.parse(who.body)
        let uid = GitJson.long(user, "id")
        guard user != nil, let login = GitJson.text(user, "login"), uid > 0 else {
            return GitRead(snapshot: nil, failure: .unusable)
        }
        let host = domain(api)

        let search = "repos/search?uid=\(uid)&exclusive=true&sort=updated&order=desc&limit=\(GitLimits.maxRepos)"
        let repoReply = await get(search)
        if repoReply.result != .ok {
            return GitRead(snapshot: nil, failure: repoReply)
        }
        guard let repoDoc = GitJson.parse(repoReply.body) else {
            return GitRead(snapshot: nil, failure: .unusable)
        }
        // Newest first; two with the same time keep the host's order.
        var recent: [(index: Int, node: [String: Any], updated: Int64)] = []
        for (index, node) in GitJson.items(repoDoc, "data").enumerated() {
            recent.append((index, node, GitJson.minutes(GitJson.text(node, "updated_at"))))
        }
        recent.sort { $0.updated != $1.updated ? $0.updated > $1.updated : $0.index < $1.index }
        let repos = Array(recent.compactMap { repo($0.node, host) }.prefix(GitLimits.maxRepos))
        let total = GitJson.count(repoReply.headers["x-total-count"]) ?? repos.count

        var items: [GitItem] = []
        let sections: [(query: String, section: GitSection, lenient: Bool)] = [
            ("created=true", .mine, false),
            ("type=pulls&review_requested=true", .review, true),
            ("assigned=true", .assigned, false),
        ]
        for (query, section, lenient) in sections {
            let (found, failure) = await sectionItems(get, host, query, section, lenient)
            if let failure {
                return GitRead(snapshot: nil, failure: failure)
            }
            items += found
        }
        let profile = GitProfile(
            login: login, name: GitJson.text(user, "full_name") ?? "", repos: total,
            followers: GitJson.whole(user, "followers_count"))
        return GitRead(snapshot: GitSnapshot(profile: profile, repos: repos, items: items), failure: nil)
    }

    private func repo(_ node: [String: Any], _ domain: String) -> GitRepo? {
        guard let name = GitJson.text(node, "full_name") else { return nil }
        return GitRepo(
            fullName: name, isPrivate: GitJson.flag(node, "private"), language: GitJson.text(node, "language") ?? "",
            stars: GitJson.whole(node, "stars_count"), openIssues: GitJson.whole(node, "open_issues_count"),
            openPulls: GitJson.whole(node, "open_pr_counter"),
            url: GitJson.url(GitJson.text(node, "html_url"), domain: domain))
    }

    private func sectionItems(
        _ get: GitGet, _ domain: String, _ query: String, _ section: GitSection, _ lenient: Bool
    ) async -> ([GitItem], GitReply?) {
        let reply = await get("repos/issues/search?state=open&limit=\(GitLimits.maxItemsPerGroup)&\(query)")
        if lenient, let status = reply.status, status == 400 || status == 404 || status == 405 {
            return ([], nil)
        }
        if reply.result != .ok {
            return ([], reply)
        }
        guard let doc = GitJson.parse(reply.body) else { return ([], .unusable) }
        var items: [GitItem] = []
        for node in GitJson.items(doc).prefix(GitLimits.maxItemsPerGroup) {
            let number = GitJson.long(node, "number")
            guard let repo = GitJson.text(GitJson.child(node, "repository"), "full_name"), number > 0 else { continue }
            let pull = GitJson.child(node, "pull_request")
            let isPull = pull is [String: Any]
            var state = GitState.open
            if isPull && GitJson.flag(pull, "merged") {
                state = GitState.merged
            } else if GitJson.text(node, "state") == "closed" {
                state = GitState.closed
            }
            items.append(
                GitItem(
                    section: section, repo: repo, number: number, title: GitJson.text(node, "title") ?? "",
                    state: state, isPull: isPull, draft: isPull && GitJson.flag(pull, "draft"),
                    ci: CiToken.none.rawValue, updatedMin: GitJson.minutes(GitJson.text(node, "updated_at")),
                    url: GitJson.url(GitJson.text(node, "html_url"), domain: domain)))
        }
        return (items, nil)
    }
}
