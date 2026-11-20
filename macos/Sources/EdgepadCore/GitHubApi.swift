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

/// A refresh's outcome, and whether `GitHubApi.lines` changed so the blob is worth sending again.
public struct GitHubRefreshResult: Equatable {
    public let result: GitHubResult
    public let changed: Bool
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

/// The GitHub screen's data, over https from api.github.com only: one GraphQL query that never changes (pull requests
/// waiting for the user's review, and the user's own open ones) and one REST call for the unread notification count,
/// kept by ETag. The token is read from the `SecretStore` for each refresh and goes only into the Authorization header
/// of these requests; it is never held, logged, or put in anything the phone receives. Redirects are refused, a reply
/// is capped at 1 MiB and 15 s, and refreshes never overlap. The twin of the laptop core's GitHubApi.
public final class GitHubApi {
    public static let maxBytes = 1024 * 1024

    /// The one GraphQL query this app sends, the same text as GitHubApi.cs. Constant: nothing from the user, the phone
    /// or a reply is ever put into it.
    public static let query = """
        query {
          review: search(query: "is:pr is:open archived:false review-requested:@me sort:updated-desc", type: ISSUE, first: 10) { nodes { ...pr } }
          mine: search(query: "is:pr is:open archived:false author:@me sort:updated-desc", type: ISSUE, first: 10) { nodes { ...pr } }
        }
        fragment pr on PullRequest {
          number
          title
          isDraft
          repository { nameWithOwner }
          commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
        }
        """

    public static let graphQlUrl = "https://api.github.com/graphql"
    public static let notificationsUrl = "https://api.github.com/notifications?per_page=50"
    public static let userUrl = "https://api.github.com/user"

    /// A rate limit is waited out for at least this long, and never longer than `maxBackoff`, whatever the reply
    /// claims.
    public static let minBackoff: TimeInterval = 60
    public static let maxBackoff: TimeInterval = 60 * 60

    private static let maxETag = 200
    private static let timeout: TimeInterval = 15

    private let store: SecretStore
    private let http: HttpSender
    private let now: () -> Date
    private let gate = NSLock()
    private var pulls: [GitHubLine] = []
    private var unread: Int?
    private var etag: String?
    private var backoffUntil = Date.distantPast
    private var running = false

    public init(store: SecretStore, http: HttpSender = CappedHttp(), clock: @escaping () -> Date = Date.init) {
        self.store = store
        self.http = http
        self.now = clock
    }

    /// The last good result as BLOB GITHUB lines: the N line once the count is known, then R and M lines.
    public var lines: [GitHubLine] {
        gate.lock()
        defer { gate.unlock() }
        guard let count = unread else { return pulls }
        let none = CiToken.none.rawValue
        return [GitHubLine(section: "N", repo: "", number: Int64(count), title: "", draft: false, ci: none)] + pulls
    }

    /// The request body: `{"query": query}`.
    public static func queryBody() -> Data {
        (try? JSONSerialization.data(withJSONObject: ["query": query])) ?? Data()
    }

    /// Asks for the pull requests, then the notification count.
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
        let before = GitHubBlob.encode(lines)
        var result = await refreshPulls()
        if result == .ok || result == .failed {
            let notifications = await refreshUnread()
            let replace = result == .ok || !(notifications == .ok || notifications == .failed)
            result = replace ? notifications : result
        }
        return GitHubRefreshResult(result: result, changed: GitHubBlob.encode(lines) != before)
    }

    /// The signed-in user's login, for display only; nil when it cannot be read. A refused token is deleted.
    public func login() async -> String? {
        guard let request = authorized("GET", Self.userUrl) else { return nil }
        let reply = await http.send(request, maxBytes: Self.maxBytes, what: "Asking GitHub who is signed in")
        guard judge(reply) == .ok, let reply else { return nil }
        return GitHubParse.login(reply.body)
    }

    private func refreshPulls() async -> GitHubResult {
        guard var request = authorized("POST", Self.graphQlUrl) else { return .notSignedIn }
        request.httpBody = Self.queryBody()
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        let reply = await http.send(request, maxBytes: Self.maxBytes, what: "Asking GitHub for pull requests")
        let result = judge(reply)
        guard result == .ok, let reply else { return result }
        guard let parsed = GitHubParse.pulls(reply.body) else { return .failed }
        gate.withLock { pulls = parsed }
        return .ok
    }

    private func refreshUnread() async -> GitHubResult {
        guard var request = authorized("GET", Self.notificationsUrl) else { return .notSignedIn }
        let known = gate.withLock { unread == nil ? nil : etag }
        if let known {
            request.setValue(known, forHTTPHeaderField: "If-None-Match")
        }
        let reply = await http.send(request, maxBytes: Self.maxBytes, what: "Asking GitHub for notifications")
        if reply?.status == 304 { return .ok }
        let result = judge(reply)
        guard result == .ok, let reply else { return result }
        guard let count = GitHubParse.unread(reply.body) else { return .failed }
        let tag = reply.header("ETag")
        let usable =
            tag.map { (1...Self.maxETag).contains($0.utf8.count) && $0.utf8.allSatisfy { $0 >= 0x20 && $0 <= 0x7E } }
            ?? false
        gate.withLock {
            unread = count
            etag = usable ? tag : nil
        }
        return .ok
    }

    /// A request carrying the saved token; nil when there is no usable one.
    private func authorized(_ method: String, _ url: String) -> URLRequest? {
        let token = store.get(GitHubApp.tokenName)
        guard GitHubApp.isToken(token), let token, let address = URL(string: url) else { return nil }
        var request = URLRequest(url: address, timeoutInterval: Self.timeout)
        request.httpMethod = method
        request.setValue("Bearer " + token, forHTTPHeaderField: "Authorization")
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
        request.setValue("Edgepad", forHTTPHeaderField: "User-Agent")
        return request
    }

    /// Success is ok; 401 deletes the token; 403 or 429 with rate-limit headers starts a back-off; anything else fails.
    private func judge(_ reply: HttpReply?) -> GitHubResult {
        guard let reply else { return .failed }
        if reply.isSuccess { return .ok }
        if reply.status == 401 {
            store.delete(GitHubApp.tokenName)
            gate.lock()
            pulls = []
            unread = nil
            etag = nil
            gate.unlock()
            return .noPermission
        }
        if reply.status == 403 || reply.status == 429, let end = rateLimitEnd(reply) {
            gate.lock()
            backoffUntil = end
            gate.unlock()
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

/// Reads GitHub's replies into BLOB GITHUB lines. Everything here came from the network: every string is cleaned and
/// cut, a repository that is not plain `owner/name` drops its row, and a reply that does not read gives nil. The twin
/// of the laptop core's GitHubParse.
public enum GitHubParse {
    /// The GraphQL reply nests ten deep; two to spare.
    public static let maxDepth = 12
    public static let maxTitle = 80
    public static let maxUnread = 50

    private static let maxRepoPart = 100
    private static let maxLogin = 39

    /// Review-requested pull requests as R lines, then the user's own as M lines; nil when the reply carries no data.
    public static func pulls(_ body: Data) -> [GitHubLine]? {
        guard let root = JsonRead.object(body, maxDepth: maxDepth), let data = root["data"] as? [String: Any] else {
            return nil
        }
        return section(data, "review", "R") + section(data, "mine", "M")
    }

    /// How many unread notifications a `GET /notifications` reply lists, at most 50; nil when it is not a list.
    public static func unread(_ body: Data) -> Int? {
        guard let list = JsonRead.parse(body, maxDepth: maxDepth) as? [Any] else { return nil }
        return min(list.filter { $0 is [String: Any] }.count, maxUnread)
    }

    /// The login of a `GET /user` reply when it is a plausible GitHub login (1..39 letters, digits, hyphens); else nil.
    public static func login(_ body: Data) -> String? {
        guard let root = JsonRead.object(body, maxDepth: maxDepth), let login = root["login"] as? String,
            (1...maxLogin).contains(login.utf16.count),
            login.unicodeScalars.allSatisfy({ GitHubApp.isAsciiLetterOrDigit($0) || $0 == "-" })
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

    private static func section(_ data: [String: Any], _ alias: String, _ section: String) -> [GitHubLine] {
        guard let search = data[alias] as? [String: Any], let nodes = search["nodes"] as? [Any] else { return [] }
        var lines: [GitHubLine] = []
        for node in nodes where lines.count < GitHubBlob.maxPerSection {
            if let line = line(node, section) { lines.append(line) }
        }
        return lines
    }

    private static func line(_ node: Any, _ section: String) -> GitHubLine? {
        guard let pr = node as? [String: Any], let number = JsonRead.int64(pr["number"]), number > 0,
            let repository = pr["repository"] as? [String: Any], let repo = repository["nameWithOwner"] as? String,
            isRepo(repo)
        else { return nil }
        let title = DashWire.clean(pr["title"] as? String ?? "", maxTitle)
        let draft = JsonRead.isTrue(pr["isDraft"])
        return GitHubLine(section: section, repo: repo, number: number, title: title, draft: draft, ci: ci(rollup(pr)))
    }

    private static func rollup(_ pr: [String: Any]) -> String? {
        guard let commits = pr["commits"] as? [String: Any], let nodes = commits["nodes"] as? [Any],
            let last = nodes.last as? [String: Any], let commit = last["commit"] as? [String: Any],
            let rollup = commit["statusCheckRollup"] as? [String: Any]
        else { return nil }
        return rollup["state"] as? String
    }
}
