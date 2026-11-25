import Foundation

/// Exchanges the refresh token of a gitlab.com sign-in for a new access token. The application is not confidential, so
/// no client secret is sent or kept (GitLab's refresh example for a public client sends only `client_id` and
/// `refresh_token`: https://docs.gitlab.com/api/oauth2/). Each refresh returns a new refresh token and kills the old
/// one, so refreshes never overlap: a second caller waits for the first one's answer. Only an answer of
/// `invalid_grant` (the refresh token is spent, revoked or unknown) deletes what is saved; any other failure keeps the
/// tokens for the next try, so a busy server never signs anyone out. The twin of the core's GitLabRenewal.
final class GitLabRenewal {
    private static let maxBytes = 16 * 1024
    private static let maxLifeSeconds = 31 * 24 * 3600
    private static let timeout: TimeInterval = 15

    private let store: SecretStore
    private let settings: IntegrationSettings
    private let http: HttpSender
    private let now: () -> Date

    // One flight per store, so refreshes never overlap whoever asks.
    private static let flight = RefreshFlight()

    init(store: SecretStore, settings: IntegrationSettings, http: HttpSender, clock: @escaping () -> Date) {
        self.store = store
        self.settings = settings
        self.http = http
        self.now = clock
    }

    /// Whether a refresh token is saved, so a 401 may be answered by a refresh.
    var canRefresh: Bool {
        GitTokens.isToken(store.get(GitLabApp.refreshName))
    }

    /// Whether the saved access token is about to run out (or has) and can be refreshed.
    func isDue() -> Bool {
        canRefresh && RefreshFlight.isDue(store.get(GitLabApp.expiresName), now: now())
    }

    func refresh() async -> RefreshOutcome {
        await Self.flight.join(store) { await self.exchange() }
    }

    /// The exchange itself, in a task of its own: a caller that is cancelled while it runs cannot lose the new refresh
    /// token, and the 15 s timeout bounds it.
    private func exchange() async -> RefreshOutcome {
        let epoch = GitLabApp.epoch(store)
        guard let sent = store.get(GitLabApp.refreshName), GitTokens.isToken(sent) else {
            GitLabApp.forgetIfCurrent(store, epoch: epoch, sentRefresh: store.get(GitLabApp.refreshName))
            return .rejected
        }
        guard let address = URL(string: GitLabApp.tokenUrl) else { return .unavailable }
        var request = URLRequest(url: address, timeoutInterval: Self.timeout)
        request.httpMethod = "POST"
        let form =
            "client_id=\(UrlText.escape(GitLabApp.clientId))&grant_type=refresh_token"
            + "&refresh_token=\(UrlText.escape(sent))"
        request.httpBody = Data(form.utf8)
        request.setValue("application/x-www-form-urlencoded; charset=utf-8", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("Edgepad", forHTTPHeaderField: "User-Agent")
        let reply = await http.send(request, maxBytes: Self.maxBytes, what: "Renewing the GitLab sign-in")
        guard let reply, let root = JsonRead.object(reply.body, maxDepth: 64) else { return .unavailable }
        if let error = root["error"] as? String {
            // Only a refresh token GitLab no longer knows ends the sign-in; a busy server or a misunderstanding keeps
            // it.
            guard error == "invalid_grant" else { return .unavailable }
            if store.get(GitLabApp.refreshName) != sent { return .refreshed }
            GitLabApp.forgetIfCurrent(store, epoch: epoch, sentRefresh: sent)
            return .rejected
        }
        guard reply.isSuccess, let access = root["access_token"] as? String,
            let next = root["refresh_token"] as? String, GitTokens.isToken(access), GitTokens.isToken(next),
            let life = JsonRead.int32(root["expires_in"]), (1...Self.maxLifeSeconds).contains(life)
        else { return .unavailable }
        let expires = now().addingTimeInterval(TimeInterval(life))
        let saved = GitLabApp.saveIfCurrent(
            store, settings, epoch: epoch, sentRefresh: sent, access: access, refresh: next, expires: expires)
        return saved ? .refreshed : .unavailable
    }
}

/// GitLab, read only, over the REST API v4 (https://docs.gitlab.com/api/). With no server address saved it is
/// gitlab.com, signed in by `GitLabSignIn` (device authorization grant, scopes read_api and read_user); the access
/// token is renewed shortly before it ends and once after a 401 by `GitLabRenewal`. With an address saved it is that
/// self-hosted server, read with a `read_api` personal access token the person pastes (`saveServer`); a server would
/// need its own OAuth application, so it never sees a POST. Either way every request is a GET with
/// `Authorization: Bearer` through the `ReadOnlyGate`. Endpoints: `GET /user`, `GET /projects?membership=true&
/// order_by=last_activity_at`, `GET /merge_requests` with `scope` created_by_me, reviews_for_me and assigned_to_me, and
/// `GET /issues` with `scope` created_by_me and assigned_to_me, all with `state=opened`. A project's language and open
/// merge-request count are not in the projects list, and the merge request list is not documented to carry a
/// pipeline: those read as empty, 0 and no CI unless a reply names `head_pipeline.status`. The twin of the core's
/// GitLabHost.
public final class GitLabHost: TokenGitHost {
    private let secrets: SecretStore
    private let options: IntegrationSettings
    private let renewal: GitLabRenewal

    public init(
        store: SecretStore, settings: IntegrationSettings, http: HttpSender = CappedHttp(),
        clock: @escaping () -> Date = Date.init
    ) {
        secrets = store
        options = settings
        renewal = GitLabRenewal(store: store, settings: settings, http: http, clock: clock)
        super.init(spec: GitLabSpec(), store: store, settings: settings, http: http, clock: clock)
    }

    /// The self-hosted server's address as saved; empty for gitlab.com.
    public var server: String { options.text(GitTokens.urlKey("gitlab")) }

    override var tokenLock: NSLock { GitLabApp.lock(secrets) }

    // Under the lock, just before an address and its token are written: a gitlab.com sign-in or renewal still in flight
    // is dropped, and its tokens go.
    override func willStore() {
        GitLabApp.bumpLocked(secrets)
        GitLabApp.forgetRenewal(secrets)
    }

    // A gitlab.com token (the one with a record of the application that issued it) goes to gitlab.com only, never to a
    // server address.
    override func mayAttach(_ url: URL, _ token: String) -> Bool {
        guard secrets.get(GitLabApp.clientName) != nil, secrets.get(GitLabApp.tokenName) == token else { return true }
        return url.scheme == "https" && url.host?.lowercased() == "gitlab.com"
    }

    override func renewDue() -> Bool { server.isEmpty && renewal.isDue() }

    override func canRenew() -> Bool { server.isEmpty && renewal.canRefresh }

    /// Whether GitLab refused the renewal of the gitlab.com sign-in and nothing has been signed in since: the person is
    /// told to sign in again (or use a token) instead of the host quietly vanishing.
    public var signInExpired: Bool { server.isEmpty && GitLabApp.isExpired(secrets) }

    /// Called each time a renewal is refused, on whatever thread asked. The app shows a notice.
    public var onExpired: (() -> Void)?

    override func renew() async -> RefreshOutcome {
        let outcome = await renewal.refresh()
        if outcome == .rejected && signInExpired { onExpired?() }
        return outcome
    }

    /// Checks a `read_api` token for the self-hosted server at `url` and saves both when the server accepts it; an
    /// empty address is refused (gitlab.com signs in by the device flow).
    public func saveServer(token: String, url: String) async -> GitSaveResult {
        guard !url.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return GitSaveResult(status: .badUrl, login: "")
        }
        // `willStore` (under the same lock the sign-in and the renewal save under) drops a gitlab.com sign-in still in
        // flight and its tokens.
        return await save(token: token, url: url, user: "")
    }

    /// Forgets the token, its refresh token and the page; the server address stays for the next sign-in.
    public override func signOut() {
        super.signOut()
        GitLabApp.lock(secrets).withLock {
            GitLabApp.bumpLocked(secrets)
            GitLabApp.forgetRenewal(secrets)
        }
    }

    /// The pipeline state of a merge request as a `CiToken`, from GitLab's pipeline statuses
    /// (https://docs.gitlab.com/api/pipelines/): success is ok, failed is fail, a pipeline that has not finished is
    /// pending, the rest is none.
    static func ci(_ status: String?) -> String {
        switch status {
        case "success": return CiToken.ok.rawValue
        case "failed": return CiToken.fail.rawValue
        case "created", "waiting_for_resource", "preparing", "pending", "running", "scheduled":
            return CiToken.pending.rawValue
        default: return CiToken.none.rawValue
        }
    }
}

private struct GitLabSpec: TokenGitSpec {
    let id = "gitlab"
    let apiIsFixed = false

    func domain(_ api: URL?) -> String {
        api.flatMap { URLComponents(url: $0, resolvingAgainstBaseURL: false)?.host } ?? ""
    }

    func apiFor(_ configured: String) -> URL? {
        configured.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            ? URL(string: GitLabApp.api) : GitTokens.gitlabApi(configured)
    }

    func authorization(token: String, user: String) -> String? { "Bearer " + token }

    func login(_ body: Data) -> String? { GitJson.text(GitJson.parse(body), "username") }

    func read(_ get: GitGet, api: URL) async -> GitRead {
        let who = await get(userPath)
        if who.result != .ok {
            return GitRead(snapshot: nil, failure: who)
        }
        let user = GitJson.parse(who.body)
        guard user != nil, let login = GitJson.text(user, "username") else {
            return GitRead(snapshot: nil, failure: .unusable)
        }
        let host = domain(api)

        let projects = await get(
            "projects?membership=true&order_by=last_activity_at&sort=desc&per_page=\(GitLimits.maxRepos)")
        if projects.result != .ok {
            return GitRead(snapshot: nil, failure: projects)
        }
        guard let projectDoc = GitJson.parse(projects.body) else {
            return GitRead(snapshot: nil, failure: .unusable)
        }
        let repos = Array(GitJson.items(projectDoc).compactMap { repo($0, host) }.prefix(GitLimits.maxRepos))
        let total = GitJson.count(projects.headers["x-total"]) ?? repos.count

        var items: [GitItem] = []
        let sections: [(section: GitSection, merges: String, issues: String?, lenient: Bool)] = [
            (.mine, "created_by_me", "created_by_me", false),
            (.review, "reviews_for_me", nil, true),
            (.assigned, "assigned_to_me", "assigned_to_me", false),
        ]
        for (section, merges, issues, lenient) in sections {
            var found: [GitItem] = []
            let (pulls, pullFailure) = await listItems(get, host, "merge_requests", merges, section, true, lenient)
            if let pullFailure { return GitRead(snapshot: nil, failure: pullFailure) }
            found += pulls
            if let issues {
                let (list, failure) = await listItems(get, host, "issues", issues, section, false, false)
                if let failure { return GitRead(snapshot: nil, failure: failure) }
                found += list
            }
            // Newest first; two with the same time keep the host's order.
            let ordered = found.enumerated().sorted {
                $0.element.updatedMin != $1.element.updatedMin
                    ? $0.element.updatedMin > $1.element.updatedMin : $0.offset < $1.offset
            }
            items += ordered.prefix(GitLimits.maxItemsPerGroup).map { $0.element }
        }
        let profile = GitProfile(
            login: login, name: GitJson.text(user, "name") ?? "", repos: total,
            followers: GitJson.whole(user, "followers"))
        return GitRead(snapshot: GitSnapshot(profile: profile, repos: repos, items: items), failure: nil)
    }

    private func repo(_ node: [String: Any], _ domain: String) -> GitRepo? {
        guard let name = GitJson.text(node, "path_with_namespace") else { return nil }
        return GitRepo(
            fullName: name, isPrivate: GitJson.text(node, "visibility") == "private", language: "",
            stars: GitJson.whole(node, "star_count"), openIssues: GitJson.whole(node, "open_issues_count"),
            openPulls: 0, url: GitJson.url(GitJson.text(node, "web_url"), domain: domain))
    }

    private func listItems(
        _ get: GitGet, _ domain: String, _ path: String, _ scope: String, _ section: GitSection, _ pull: Bool,
        _ lenient: Bool
    ) async -> ([GitItem], GitReply?) {
        let query = "scope=\(scope)&state=opened&order_by=updated_at&sort=desc&per_page=\(GitLimits.maxItemsPerGroup)"
        let reply = await get("\(path)?\(query)")
        if lenient, let status = reply.status, status == 400 || status == 404 {
            return ([], nil)
        }
        if reply.result != .ok {
            return ([], reply)
        }
        guard let doc = GitJson.parse(reply.body) else { return ([], .unusable) }
        var items: [GitItem] = []
        for node in GitJson.items(doc).prefix(GitLimits.maxItemsPerGroup) {
            // references.full is "group/project!12" for a merge request and "group/project#12" for an issue.
            let full = GitJson.text(GitJson.child(node, "references"), "full")
            let number = GitJson.long(node, "iid")
            guard let full, let cut = full.lastIndex(of: pull ? "!" : "#"), cut > full.startIndex, number > 0
            else { continue }
            var state = GitState.open
            switch GitJson.text(node, "state") {
            case "merged": state = GitState.merged
            case "closed": state = GitState.closed
            default: break
            }
            let pipeline = GitJson.text(GitJson.child(node, "head_pipeline"), "status")
            let ci = pull ? GitLabHost.ci(pipeline) : CiToken.none.rawValue
            items.append(
                GitItem(
                    section: section, repo: String(full[..<cut]), number: number,
                    title: GitJson.text(node, "title") ?? "", state: state, isPull: pull,
                    draft: pull && GitJson.flag(node, "draft"), ci: ci,
                    updatedMin: GitJson.minutes(GitJson.text(node, "updated_at")),
                    url: GitJson.url(GitJson.text(node, "web_url"), domain: domain)))
        }
        return (items, nil)
    }
}
