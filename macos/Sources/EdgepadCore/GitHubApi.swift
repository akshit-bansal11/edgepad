import Foundation

/// How a GitHub refresh went.
public enum GitHubResult: Equatable {
    /// Asked and read.
    case ok
    /// No usable token is saved; nobody was asked.
    case notSignedIn
    /// GitHub refused the token (401). It has been deleted; the screen shows "github:no-permission".
    case noPermission
    /// GitHub's rate limit; the last result is kept and nothing is asked until the limit resets.
    case rateLimited
    /// Anything else; the last good result is kept.
    case failed
    /// A refresh was already running; this one did nothing.
    case busy
}

/// A refresh's outcome, and whether `GitHost.page` changed so the blob is worth sending again.
public struct GitHubRefreshResult: Equatable {
    public let result: GitHubResult
    public let changed: Bool

    public init(result: GitHubResult, changed: Bool) {
        self.result = result
        self.changed = changed
    }
}

/// When the GitHub screen may be refreshed: only while the phone shows it (DASH_WATCH bit 4), at least 60 s apart.
public enum GitHubRefresh {
    public static let every: TimeInterval = 60

    public static func shouldRun(last: Date?, now: Date, watchBit4: Bool) -> Bool {
        guard watchBit4 else { return false }
        guard let last else { return true }
        return now.timeIntervalSince(last) >= every
    }
}

/// The GitHub screen's data, over https from api.github.com only: one GraphQL query that never changes (the account,
/// its newest repositories, and the pull requests and issues that are the user's, wait for the user's review or are
/// assigned to the user). It only reads: every request passes the `ReadOnlyGate`. The token is a GitHub App user token
/// (read-only, and limited to what the App is installed on); it is read from the `SecretStore` for each request and
/// goes only into the Authorization header of these requests; it is never held, logged, or put in anything the phone
/// receives. An expiring token is renewed shortly before its end, and once after a 401, by `GitHubTokenRefresh`; when
/// that fails the token is deleted and the screen says so. Redirects are refused, a reply is capped at 1 MiB and 15 s,
/// and refreshes never overlap. Notifications are not asked: GitHub gives them only to a classic personal access
/// token. The twin of the laptop core's GitHubApi.
public final class GitHubApi: GitHost {
    public static let maxBytes = 1024 * 1024

    /// The one GraphQL query this app sends, the same text as GitHubApi.cs. Constant: nothing from the user, the phone
    /// or a reply is ever put into it, and it has no mutation.
    public static let query = """
        query {
          viewer {
            login
            name
            followers { totalCount }
            repositories(first: 30, ownerAffiliations: OWNER, orderBy: { field: PUSHED_AT, direction: DESC }) {
              totalCount
              nodes {
                nameWithOwner
                isPrivate
                url
                stargazerCount
                primaryLanguage { name }
                issues(states: OPEN) { totalCount }
                pullRequests(states: OPEN) { totalCount }
              }
            }
          }
          mine: search(query: "is:open archived:false author:@me sort:updated-desc", type: ISSUE, first: 20) { nodes { ...item } }
          review: search(query: "is:pr is:open archived:false review-requested:@me sort:updated-desc", type: ISSUE, first: 20) { nodes { ...item } }
          assigned: search(query: "is:open archived:false assignee:@me sort:updated-desc", type: ISSUE, first: 20) { nodes { ...item } }
        }
        fragment item on SearchResultItem {
          __typename
          ... on PullRequest {
            number
            title
            isDraft
            state
            url
            updatedAt
            repository { nameWithOwner }
            commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
          }
          ... on Issue {
            number
            title
            state
            url
            updatedAt
            repository { nameWithOwner }
          }
        }
        """

    public static let graphQlUrl = "https://api.github.com/graphql"
    public static let userUrl = "https://api.github.com/user"

    /// A rate limit is waited out for at least this long, and never longer than `maxBackoff`, whatever the reply
    /// claims.
    public static let minBackoff: TimeInterval = 60
    public static let maxBackoff: TimeInterval = 60 * 60

    private static let timeout: TimeInterval = 15

    private let store: SecretStore
    private let http: HttpSender
    private let now: () -> Date
    private let renewal: GitHubTokenRefresh
    private let gate = NSLock()
    private let book = GitBook(host: "github", extraUrl: GitHubApp.installUrl)
    private var snapshot: GitSnapshot?
    private var backoffUntil = Date.distantPast
    private var running = false

    public init(store: SecretStore, http: HttpSender = CappedHttp(), clock: @escaping () -> Date = Date.init) {
        self.store = store
        self.http = http
        self.now = clock
        self.renewal = GitHubTokenRefresh(store: store, clientId: GitHubApp.clientId, http: http, clock: clock)
    }

    public var id: String { "github" }

    public var domain: String { GitHubParse.domain }

    /// The last good result as BLOB GIT and the addresses behind its indexes; nil before the first one and after a
    /// refused token.
    public var page: GitPage? { book.page }

    /// The request body: `{"query": query}`.
    public static func queryBody() -> Data {
        (try? JSONSerialization.data(withJSONObject: ["query": query])) ?? Data()
    }

    /// Asks for the account, repositories, pull requests and issues.
    public func refresh() async -> GitHubRefreshResult {
        let (busy, waiting) = gate.withLock { () -> (Bool, Bool) in
            if running { return (true, false) }
            running = true
            return (false, now() < backoffUntil)
        }
        if busy {
            return GitHubRefreshResult(result: .busy, changed: false)
        }
        defer { gate.withLock { running = false } }
        if waiting {
            return GitHubRefreshResult(result: .rateLimited, changed: false)
        }
        let before = book.page?.rev
        let result = await refreshPulls()
        rebuild()
        return GitHubRefreshResult(result: result, changed: book.page?.rev != before)
    }

    /// The signed-in user's login, for display only; nil when it cannot be read. A refused token is deleted.
    public func login() async -> String? {
        let (result, reply) = await call("GET", Self.userUrl, nil, "Asking GitHub who is signed in")
        guard result == .ok, let reply else { return nil }
        return GitHubParse.login(reply.body)
    }

    private func refreshPulls() async -> GitHubResult {
        let (result, reply) = await call("POST", Self.graphQlUrl, Self.queryBody(), "Asking GitHub for pull requests")
        guard result == .ok, let reply else { return result }
        guard let parsed = GitHubParse.snapshot(reply.body) else { return .failed }
        gate.withLock { snapshot = parsed }
        return .ok
    }

    /// Makes the page from the last good snapshot; a revision is spent only when what the phone sees changed.
    private func rebuild() {
        guard let current = gate.withLock({ snapshot }) else { return }
        book.update(current)
    }

    private func clearScreen() {
        gate.withLock { snapshot = nil }
        book.clear()
    }

    /// One request with the saved token: the token is renewed first when it is about to expire, and the request is
    /// sent once more with a new token after a 401 when a renewal makes one. A token from the old OAuth app is deleted
    /// first (`GitHubApp.migrate`).
    private func call(
        _ method: String, _ url: String, _ body: Data?, _ what: String
    ) async -> (GitHubResult, HttpReply?) {
        GitHubApp.migrate(store, GitHubApp.clientId)
        if renewal.isDue(), await renewal.refresh() == .rejected {
            clearScreen()
            return (.noPermission, nil)
        }
        var attempt = 0
        while true {
            guard let token = store.get(GitHubApp.tokenName), GitHubApp.isToken(token),
                let address = URL(string: url)
            else { return (.notSignedIn, nil) }
            var request = URLRequest(url: address, timeoutInterval: Self.timeout)
            request.httpMethod = method
            request.setValue("Bearer " + token, forHTTPHeaderField: "Authorization")
            request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
            request.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
            request.setValue("Edgepad", forHTTPHeaderField: "User-Agent")
            if let body {
                request.httpBody = body
                request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            }
            let reply = await http.send(request, maxBytes: Self.maxBytes, what: what)
            if reply?.status == 401, attempt == 0, renewal.canRefresh, store.get(GitHubApp.tokenName) == token {
                attempt += 1
                switch await renewal.refresh() {
                case .refreshed: continue
                case .unavailable: return (.failed, reply)
                case .rejected: break
                }
            }
            return (judge(reply, token), reply)
        }
    }

    /// Success is ok; 401 deletes the token; 403 or 429 with rate-limit headers starts a back-off; anything else fails.
    private func judge(_ reply: HttpReply?, _ sent: String) -> GitHubResult {
        guard let reply else { return .failed }
        if reply.isSuccess { return .ok }
        if reply.status == 401 {
            // Only the token this request carried goes: one saved by a sign-in while it was out is a different one.
            let saved = store.get(GitHubApp.tokenName)
            if saved == nil || saved == sent {
                GitHubApp.forget(store)
                clearScreen()
            }
            return .noPermission
        }
        if reply.status == 403 || reply.status == 429, let end = rateLimitEnd(reply) {
            gate.withLock { backoffUntil = end }
            return .rateLimited
        }
        return .failed
    }

    /// When a rate-limited reply says the limit resets (Retry-After seconds, else x-ratelimit-reset epoch seconds),
    /// clamped to 1 min..1 h; nil when it is not a rate limit.
    private func rateLimitEnd(_ reply: HttpReply) -> Date? {
        let at = now()
        let target: Date
        if let seconds = Self.digits(reply.header("Retry-After")) {
            target = at.addingTimeInterval(TimeInterval(min(seconds, Int64(Self.maxBackoff))))
        } else if reply.header("x-ratelimit-remaining") == "0" {
            let reset = Self.digits(reply.header("x-ratelimit-reset"))
            target = reset.map { Date(timeIntervalSince1970: TimeInterval($0)) } ?? at
        } else {
            return nil
        }
        return min(max(target, at.addingTimeInterval(Self.minBackoff)), at.addingTimeInterval(Self.maxBackoff))
    }

    /// Plain ASCII digits that fit an Int64, nothing else.
    private static func digits(_ text: String?) -> Int64? {
        guard let text, !text.isEmpty, text.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }) else { return nil }
        return Int64(text)
    }
}

/// Reads GitHub's replies into a `GitSnapshot`. Everything here came from the network: every string is cleaned and
/// cut, a repository that is not plain `owner/name` drops its row, an address that is not https on github.com is kept
/// as empty, and a reply that does not read gives nil. The twin of the laptop core's GitHubParse.
public enum GitHubParse {
    /// The GraphQL reply nests ten deep; two to spare.
    public static let maxDepth = 12
    public static let maxTitle = 80

    /// The web host every address kept from a reply must be on.
    public static let domain = "github.com"

    private static let maxRepoPart = 100
    private static let maxLogin = 39

    /// The account, its newest repositories and its pull requests and issues, from the reply to `GitHubApi.query`; nil
    /// when the reply carries no data or no viewer. A row that does not read is dropped, never the whole reply.
    public static func snapshot(_ body: Data) -> GitSnapshot? {
        guard let root = JsonRead.object(body, maxDepth: maxDepth), let data = root["data"] as? [String: Any],
            let viewer = data["viewer"] as? [String: Any], let login = viewer["login"] as? String, isLogin(login)
        else { return nil }
        var repos: [GitRepo] = []
        var repoTotal = 0
        if let repositories = viewer["repositories"] as? [String: Any] {
            repoTotal = GitJson.whole(repositories, "totalCount")
            for node in nodes(repositories) where repos.count < GitLimits.maxRepos {
                if let repo = repo(node) { repos.append(repo) }
            }
        }
        let followers = (viewer["followers"] as? [String: Any]).map { GitJson.whole($0, "totalCount") } ?? 0
        var items: [GitItem] = []
        addItems(&items, data, "mine", .mine)
        addItems(&items, data, "review", .review)
        addItems(&items, data, "assigned", .assigned)
        let name = DashWire.clean(GitJson.text(viewer, "name") ?? "", GitLimits.maxName)
        let profile = GitProfile(login: login, name: name, repos: repoTotal, followers: followers)
        return GitSnapshot(profile: profile, repos: repos, items: items)
    }

    /// The login of a `GET /user` reply when it is a plausible GitHub login (1..39 letters, digits, hyphens); else nil.
    public static func login(_ body: Data) -> String? {
        guard let root = JsonRead.object(body, maxDepth: maxDepth), let login = root["login"] as? String,
            isLogin(login)
        else { return nil }
        return login
    }

    /// A `statusCheckRollup.state` as a `CiToken`: SUCCESS ok, FAILURE and ERROR fail, PENDING and EXPECTED pending,
    /// anything else none.
    public static func ci(_ state: String?) -> String {
        switch state {
        case "SUCCESS": return CiToken.ok.rawValue
        case "FAILURE", "ERROR": return CiToken.fail.rawValue
        case "PENDING", "EXPECTED": return CiToken.pending.rawValue
        default: return CiToken.none.rawValue
        }
    }

    /// Exactly `owner/name`, each part 1..100 of ASCII letters, digits, "_", "." and "-".
    public static func isRepo(_ repo: String?) -> Bool {
        let parts = (repo ?? "").split(separator: "/", omittingEmptySubsequences: false)
        return parts.count == 2
            && parts.allSatisfy { part in
                (1...maxRepoPart).contains(part.utf8.count) && part.unicodeScalars.allSatisfy(PermAskBody.isToolChar)
            }
    }

    /// 1..39 letters, digits and hyphens.
    public static func isLogin(_ login: String) -> Bool {
        (1...maxLogin).contains(login.utf16.count)
            && login.unicodeScalars.allSatisfy { GitHubApp.isAsciiLetterOrDigit($0) || $0 == "-" }
    }

    private static func addItems(
        _ items: inout [GitItem], _ data: [String: Any], _ alias: String, _ section: GitSection
    ) {
        guard let search = data[alias] as? [String: Any] else { return }
        var added = 0
        for node in nodes(search) where added < GitLimits.maxItemsPerGroup {
            if let item = item(node, section) {
                items.append(item)
                added += 1
            }
        }
    }

    private static func item(_ node: Any, _ section: GitSection) -> GitItem? {
        guard let object = node as? [String: Any], let number = JsonRead.int64(object["number"]), number > 0,
            let repository = object["repository"] as? [String: Any], let repo = repository["nameWithOwner"] as? String,
            isRepo(repo), let type = object["__typename"] as? String, type == "PullRequest" || type == "Issue"
        else { return nil }
        let pull = type == "PullRequest"
        let state: String
        switch object["state"] as? String {
        case "CLOSED": state = GitState.closed
        case "MERGED": state = GitState.merged
        default: state = GitState.open
        }
        return GitItem(
            section: section, repo: repo, number: number,
            title: DashWire.clean(GitJson.text(object, "title") ?? "", maxTitle), state: state, isPull: pull,
            draft: pull && JsonRead.isTrue(object["isDraft"]),
            ci: pull ? ci(rollup(object)) : CiToken.none.rawValue,
            updatedMin: GitJson.minutes(GitJson.text(object, "updatedAt")),
            url: GitJson.url(GitJson.text(object, "url"), domain: domain))
    }

    private static func repo(_ node: Any) -> GitRepo? {
        guard let object = node as? [String: Any], let name = object["nameWithOwner"] as? String, isRepo(name) else {
            return nil
        }
        let language = (object["primaryLanguage"] as? [String: Any]).flatMap { GitJson.text($0, "name") }
        return GitRepo(
            fullName: name, isPrivate: JsonRead.isTrue(object["isPrivate"]),
            language: DashWire.clean(language ?? "", GitLimits.maxLanguage),
            stars: GitJson.whole(object, "stargazerCount"),
            openIssues: (object["issues"] as? [String: Any]).map { GitJson.whole($0, "totalCount") } ?? 0,
            openPulls: (object["pullRequests"] as? [String: Any]).map { GitJson.whole($0, "totalCount") } ?? 0,
            url: GitJson.url(GitJson.text(object, "url"), domain: domain))
    }

    private static func nodes(_ parent: [String: Any]) -> [Any] {
        parent["nodes"] as? [Any] ?? []
    }

    private static func rollup(_ pr: [String: Any]) -> String? {
        guard let commits = pr["commits"] as? [String: Any], let nodes = commits["nodes"] as? [Any],
            let last = nodes.last as? [String: Any], let commit = last["commit"] as? [String: Any],
            let rollup = commit["statusCheckRollup"] as? [String: Any]
        else { return nil }
        return rollup["state"] as? String
    }
}
