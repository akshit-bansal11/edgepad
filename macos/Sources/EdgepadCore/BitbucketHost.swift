import Foundation

/// Bitbucket Cloud, `https://api.bitbucket.org/2.0`. Since 9 June 2026 only API tokens work (app passwords ended): the
/// person pastes an API token and the e-mail of the Atlassian account, sent as Basic authentication
/// (https://support.atlassian.com/bitbucket-cloud/docs/using-api-tokens/). The token's scope names are not listed in the
/// REST reference and are unverified. Endpoints, all from https://developer.atlassian.com/cloud/bitbucket/rest/:
/// `GET /user`, `GET /user/workspaces` (workspace slugs), `GET /repositories/{workspace}?role=member` and
/// `GET /workspaces/{workspace}/pullrequests/{selected_user}?state=OPEN`. Up to three workspaces are read. This shows
/// the account, its repositories newest first, and the open pull requests the account wrote (Mine). Bitbucket documents
/// no stars or follower count, no per-repository open counts in that list, no pull request assignee and no "waiting for
/// my review" list, so those stay 0 or empty, never invented. The twin of the laptop core's BitbucketHost.
public final class BitbucketHost: TokenGitHost {
    public static let apiRoot = "https://api.bitbucket.org/2.0/"
    public static let webDomain = "bitbucket.org"

    public init(
        store: SecretStore, settings: IntegrationSettings, http: HttpSender = CappedHttp(),
        clock: @escaping () -> Date = Date.init
    ) {
        super.init(spec: BitbucketSpec(), store: store, settings: settings, http: http, clock: clock)
    }
}

private struct BitbucketSpec: TokenGitSpec {
    let id = "bitbucket"
    let requiresUser = true

    private static let maxWorkspaces = 3

    func domain(_ api: URL?) -> String { BitbucketHost.webDomain }

    func apiFor(_ configured: String) -> URL? { URL(string: BitbucketHost.apiRoot) }

    func authorization(token: String, user: String) -> String? {
        "Basic " + Data((user + ":" + token).utf8).base64EncodedString()
    }

    func login(_ body: Data) -> String? { Self.login(GitJson.parse(body)) }

    func read(_ get: GitGet, api: URL) async -> GitRead {
        let who = await get(userPath)
        if who.result != .ok {
            return GitRead(snapshot: nil, failure: who)
        }
        let user = GitJson.parse(who.body)
        let account = GitJson.text(user, "account_id") ?? GitJson.text(user, "uuid")
        guard user != nil, let login = Self.login(user), let account, !account.isEmpty else {
            return GitRead(snapshot: nil, failure: .unusable)
        }
        let spaceReply = await get("user/workspaces?pagelen=\(Self.maxWorkspaces)")
        if spaceReply.result != .ok {
            return GitRead(snapshot: nil, failure: spaceReply)
        }
        guard let spaceDoc = GitJson.parse(spaceReply.body) else {
            return GitRead(snapshot: nil, failure: .unusable)
        }
        let slugs = GitJson.items(spaceDoc, "values")
            .compactMap { GitJson.text(GitJson.child($0, "workspace"), "slug") }
            .filter { !$0.isEmpty }
            .prefix(Self.maxWorkspaces)

        var found: [(index: Int, repo: GitRepo, updated: Int64)] = []
        var items: [GitItem] = []
        var size = 0
        for slug in slugs {
            let name = UrlText.escape(slug)
            let repoReply = await get("repositories/\(name)?role=member&sort=-updated_on&pagelen=\(GitLimits.maxRepos)")
            if repoReply.result != .ok {
                return GitRead(snapshot: nil, failure: repoReply)
            }
            guard let repoDoc = GitJson.parse(repoReply.body) else {
                return GitRead(snapshot: nil, failure: .unusable)
            }
            for node in GitJson.items(repoDoc, "values") {
                if let repo = Self.repo(node) {
                    found.append((found.count, repo, GitJson.minutes(GitJson.text(node, "updated_on"))))
                }
            }
            size += GitJson.whole(repoDoc, "size")

            let who = UrlText.escape(account)
            let pullReply = await get(
                "workspaces/\(name)/pullrequests/\(who)?state=OPEN&pagelen=\(GitLimits.maxItemsPerGroup)")
            if pullReply.result != .ok {
                return GitRead(snapshot: nil, failure: pullReply)
            }
            guard let pullDoc = GitJson.parse(pullReply.body) else {
                return GitRead(snapshot: nil, failure: .unusable)
            }
            items += GitJson.items(pullDoc, "values").compactMap { Self.pull($0) }
        }

        found.sort { $0.updated != $1.updated ? $0.updated > $1.updated : $0.index < $1.index }
        let repos = found.prefix(GitLimits.maxRepos).map { $0.repo }
        let newest = items.enumerated().sorted {
            $0.element.updatedMin != $1.element.updatedMin
                ? $0.element.updatedMin > $1.element.updatedMin : $0.offset < $1.offset
        }
        let pulls = newest.prefix(GitLimits.maxItemsPerGroup).map { $0.element }

        let profile = GitProfile(
            login: login, name: GitJson.text(user, "display_name") ?? "", repos: size > 0 ? size : repos.count,
            followers: 0)
        return GitRead(snapshot: GitSnapshot(profile: profile, repos: Array(repos), items: Array(pulls)), failure: nil)
    }

    private static func login(_ user: Any?) -> String? {
        if let nick = GitJson.text(user, "nickname"), !nick.isEmpty { return nick }
        return GitJson.text(user, "display_name")
    }

    private static func repo(_ node: [String: Any]) -> GitRepo? {
        guard let name = GitJson.text(node, "full_name") else { return nil }
        return GitRepo(
            fullName: name, isPrivate: GitJson.flag(node, "is_private"), language: GitJson.text(node, "language") ?? "",
            stars: 0, openIssues: 0, openPulls: 0, url: link(node))
    }

    private static func pull(_ node: [String: Any]) -> GitItem? {
        let destination = GitJson.child(GitJson.child(node, "destination"), "repository")
        let number = GitJson.long(node, "id")
        guard let repo = GitJson.text(destination, "full_name"), number > 0 else { return nil }
        let state: String
        switch GitJson.text(node, "state") {
        case "MERGED": state = GitState.merged
        case "DECLINED", "SUPERSEDED": state = GitState.closed
        default: state = GitState.open
        }
        return GitItem(
            section: .mine, repo: repo, number: number, title: GitJson.text(node, "title") ?? "", state: state,
            isPull: true, draft: GitJson.flag(node, "draft"), ci: CiToken.none.rawValue,
            updatedMin: GitJson.minutes(GitJson.text(node, "updated_on")), url: link(node))
    }

    private static func link(_ node: [String: Any]) -> String {
        let href = GitJson.text(GitJson.child(GitJson.child(node, "links"), "html"), "href")
        return GitJson.url(href, domain: BitbucketHost.webDomain)
    }
}
