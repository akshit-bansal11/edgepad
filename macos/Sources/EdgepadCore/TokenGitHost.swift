import Foundation

/// How saving a pasted token went. The windows turn each into their own translated words; nothing here is English.
public enum GitSaveStatus: Equatable {
    /// The host accepted the token; it and the address and name are saved.
    case saved
    /// The token is empty, too long or has characters no token has. Nobody was asked.
    case badToken
    /// The address is not an https address. Nobody was asked.
    case badUrl
    /// The user name or e-mail is empty or unusable. Nobody was asked.
    case badUser
    /// The host answered 401: the token is wrong, expired or revoked.
    case unauthorized
    /// The host answered 403: the token lacks the read permission it needs.
    case forbidden
    /// The host answered 404: the address is probably not that host's API.
    case notFound
    /// The host could not be reached.
    case network
    /// Anything else, including a reply that was not what that host sends or a token that could not be saved.
    case failed
}

/// What `TokenGitHost.save` found. `login` is for display only and empty unless `status` is `saved`.
public struct GitSaveResult: Equatable {
    public let status: GitSaveStatus
    public let login: String
}

/// One reply, judged. `result` says how it went, `status` is the HTTP status (nil when nothing came back).
struct GitReply {
    let result: GitHubResult
    let status: Int?
    let retryAfter: TimeInterval
    let headers: [String: String]
    let body: Data

    static let unusable = GitReply(result: .failed, status: nil, retryAfter: 0, headers: [:], body: Data())
}

/// What a host read: the snapshot, or the reply that stopped it.
struct GitRead {
    let snapshot: GitSnapshot?
    let failure: GitReply?
}

/// Asks the host's API for one path under its root (always GET) and judges the reply.
typealias GitGet = (String) async -> GitReply

/// The names, limits and checks every token-pasting git host shares.
enum GitTokens {
    static let maxToken = 512
    static let maxUser = 254
    static let maxUrl = 200

    /// The `SecretStore` name of the host's token.
    static func tokenName(_ id: String) -> String { id + ".token" }

    /// The `IntegrationSettings` key of the host's address (Gitea, Forgejo and Codeberg only). Not a secret.
    static func urlKey(_ id: String) -> String { id + "Url" }

    /// The `IntegrationSettings` key of the host's user name or e-mail (Bitbucket only). Not a secret.
    static func userKey(_ id: String) -> String { id + "User" }

    /// Visible ASCII only, 8 to 512 characters: every token these hosts issue.
    static func isToken(_ token: String?) -> Bool {
        guard let token, (8...maxToken).contains(token.unicodeScalars.count) else { return false }
        return token.unicodeScalars.allSatisfy { $0.value > 0x20 && $0.value <= 0x7E }
    }

    /// Visible ASCII, no colon (it separates the name from the token in Basic authentication).
    static func isUser(_ user: String?) -> Bool {
        guard let user, (1...maxUser).contains(user.unicodeScalars.count) else { return false }
        return user.unicodeScalars.allSatisfy { $0.value > 0x20 && $0.value <= 0x7E && $0 != ":" }
    }

    /// The API root (`https://host[:port][/path]/api/v1/`) of a Gitea-compatible server given its web address. Only an
    /// absolute https address with no user info, query or fragment passes; anything else is nil.
    static func giteaApi(_ url: String?) -> URL? {
        let text = (url ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        guard (1...maxUrl).contains(text.utf16.count), let parts = URLComponents(string: text),
            parts.scheme?.lowercased() == "https", parts.user == nil, parts.password == nil, parts.query == nil,
            parts.fragment == nil, let host = parts.host, !host.isEmpty
        else { return nil }
        var path = parts.percentEncodedPath
        while path.hasSuffix("/") {
            path.removeLast()
        }
        var port = ""
        if let number = parts.port, number != 443 {
            port = ":" + String(number)
        }
        return URL(string: "https://" + host.lowercased() + port + path + "/api/v1/")
    }
}

/// Reading a git host's JSON without trusting it: every missing or wrongly typed field reads as absent.
enum GitJson {
    static func parse(_ body: Data) -> Any? {
        JsonRead.parse(body, maxDepth: 12)
    }

    static func text(_ parent: Any?, _ name: String) -> String? {
        (parent as? [String: Any])?[name] as? String
    }

    static func whole(_ parent: Any?, _ name: String) -> Int {
        max(0, JsonRead.int32((parent as? [String: Any])?[name]) ?? 0)
    }

    static func long(_ parent: Any?, _ name: String) -> Int64 {
        JsonRead.int64((parent as? [String: Any])?[name]) ?? 0
    }

    static func flag(_ parent: Any?, _ name: String) -> Bool {
        JsonRead.isTrue((parent as? [String: Any])?[name])
    }

    static func child(_ parent: Any?, _ name: String) -> Any? {
        (parent as? [String: Any])?[name]
    }

    /// The object elements of the named array, or none.
    static func items(_ parent: Any?, _ name: String) -> [[String: Any]] {
        items(child(parent, name))
    }

    /// The object elements of an array, or none.
    static func items(_ array: Any?) -> [[String: Any]] {
        (array as? [Any])?.compactMap { $0 as? [String: Any] } ?? []
    }

    /// UTC minutes since the Unix epoch of an ISO time; 0 when it does not read.
    static func minutes(_ time: String?) -> Int64 {
        guard var text = time, !text.isEmpty else { return 0 }
        // The formatter reads whole seconds only: drop a fraction such as ".123456" after the time.
        if let dot = text.firstIndex(of: "."), text[..<dot].contains("T") {
            var end = text.index(after: dot)
            while end < text.endIndex, text[end].isASCII, text[end].isNumber {
                end = text.index(after: end)
            }
            text.removeSubrange(dot..<end)
        }
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        guard let date = formatter.date(from: text) ?? formatter.date(from: text + "Z") else { return 0 }
        return max(0, Int64(date.timeIntervalSince1970) / 60)
    }

    /// The address when it is plain https on `domain` (default port, no user info), else empty; the phone can then
    /// never open it.
    static func url(_ text: String?, domain: String) -> String {
        guard let text, !text.isEmpty, text.utf16.count <= GitLimits.maxUrl,
            let url = GitOpenGate.https(text, domain: domain)
        else { return "" }
        return url.absoluteString
    }

    /// Plain ASCII digits that fit an Int32, nothing else.
    static func count(_ text: String?) -> Int? {
        guard let text, !text.isEmpty, text.utf8.count <= 10, text.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }),
            let value = Int(text), value <= Int(Int32.max)
        else { return nil }
        return value
    }
}

/// What differs between the token-pasting git hosts; `TokenGitHost` does the rest.
protocol TokenGitSpec {
    var id: String { get }
    /// Whether the host needs a user name or e-mail beside the token.
    var requiresUser: Bool { get }
    /// Whether the API root never depends on the configured address.
    var apiIsFixed: Bool { get }
    /// The path (relative to the API root) that names the signed-in account, asked when a token is saved.
    var userPath: String { get }
    /// The one web host name this host's links may open; empty when the address is not usable.
    func domain(_ api: URL?) -> String
    /// The API root for a configured web address (ignored by hosts with a fixed API); nil when unusable.
    func apiFor(_ configured: String) -> URL?
    /// The Authorization header value for this token (and name, for Basic); nil for a host that takes the token in the
    /// address.
    func authorization(token: String, user: String) -> String?
    /// The address a request is sent to. A host whose API documents only a token query parameter (Gitee) adds it
    /// here; nothing else may reach a message or log with this address.
    func sign(_ url: URL, token: String) -> URL
    /// The account's login from the `userPath` reply; nil when it is not one.
    func login(_ body: Data) -> String?
    /// Reads everything one refresh sends the phone with GETs under `api`.
    func read(_ get: GitGet, api: URL) async -> GitRead
}

extension TokenGitSpec {
    var requiresUser: Bool { false }
    var apiIsFixed: Bool { true }
    var userPath: String { "user" }

    func sign(_ url: URL, token: String) -> URL { url }
}

/// A git host that is read with a token the person pastes (Gitea, Forgejo, Codeberg, Bitbucket, Gitee). It only
/// reads: every request is a GET through the `ReadOnlyGate`. The token lives in the `SecretStore` alone; it is read for
/// each refresh, goes only into the Authorization header of a request to the configured API, and is never logged,
/// returned, kept in `IntegrationSettings` or sent to the phone. Settings hold the host's address and the user name
/// only. Redirects are refused, a reply is capped at 1 MiB and 15 s, a refresh never overlaps another, and one refresh
/// is one bounded list: a host that fails part-way keeps the last good page. The twin of the laptop core's TokenGitHost.
public class TokenGitHost: GitHost {
    static let maxBytes = 1024 * 1024

    /// A rate limit is waited out for at least this long, and never longer than `maxBackoff`, whatever the reply
    /// claims.
    static let minBackoff: TimeInterval = 60
    static let maxBackoff: TimeInterval = 60 * 60

    private static let timeout: TimeInterval = 15

    private let spec: TokenGitSpec
    private let store: SecretStore
    private let settings: IntegrationSettings
    private let http: HttpSender
    private let now: () -> Date
    private let gate = NSLock()
    private let book: GitBook
    private var backoffUntil = Date.distantPast
    private var running = false

    init(
        spec: TokenGitSpec, store: SecretStore, settings: IntegrationSettings, http: HttpSender,
        clock: @escaping () -> Date
    ) {
        self.spec = spec
        self.store = store
        self.settings = settings
        self.http = http
        self.now = clock
        book = GitBook(host: spec.id)
    }

    public var id: String { spec.id }

    /// The one web host name this host's links may open; empty when the address is not usable.
    public var domain: String { spec.domain(api) }

    public var page: GitPage? { book.page }

    /// The API root from the saved settings; nil when it is unusable.
    private var api: URL? { spec.apiFor(settings.text(GitTokens.urlKey(spec.id))) }

    public var isSignedIn: Bool {
        GitTokens.isToken(store.get(GitTokens.tokenName(spec.id))) && api != nil
            && (!spec.requiresUser || GitTokens.isUser(settings.text(GitTokens.userKey(spec.id))))
    }

    /// Asks the host for the account, repositories, pull requests and issues.
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
        let user = settings.text(GitTokens.userKey(spec.id))
        guard let token = store.get(GitTokens.tokenName(spec.id)), GitTokens.isToken(token), let api,
            !spec.requiresUser || GitTokens.isUser(user)
        else { return GitHubRefreshResult(result: .notSignedIn, changed: false) }

        let before = book.page?.rev
        let read = await spec.read(getter(api, token, user), api: api)
        if let snapshot = read.snapshot {
            book.update(snapshot)
            return GitHubRefreshResult(result: .ok, changed: book.page?.rev != before)
        }
        let failure = read.failure ?? GitReply.unusable
        if failure.status == 401 {
            store.delete(GitTokens.tokenName(spec.id))
            book.clear()
        } else if failure.result == .rateLimited {
            let until = now().addingTimeInterval(failure.retryAfter)
            gate.withLock { backoffUntil = until }
        }
        return GitHubRefreshResult(result: failure.result, changed: book.page?.rev != before)
    }

    /// Checks a pasted token with one GET of the account and, only when the host accepts it, saves the address and
    /// user name in the settings and then the token in the secret store (in that order, so a token is never saved
    /// beside a wrong address). Nothing is saved otherwise.
    public func save(token: String, url: String, user: String) async -> GitSaveResult {
        let secret = token.trimmingCharacters(in: .whitespacesAndNewlines)
        let name = user.trimmingCharacters(in: .whitespacesAndNewlines)
        guard GitTokens.isToken(secret) else { return GitSaveResult(status: .badToken, login: "") }
        guard let api = spec.apiFor(url) else { return GitSaveResult(status: .badUrl, login: "") }
        if spec.requiresUser && !GitTokens.isUser(name) {
            return GitSaveResult(status: .badUser, login: "")
        }
        let reply = await getter(api, secret, name)(spec.userPath)
        if reply.result != .ok {
            let status: GitSaveStatus
            switch reply.status {
            case 401: status = .unauthorized
            case 403: status = .forbidden
            case 404: status = .notFound
            case nil: status = .network
            default: status = .failed
            }
            return GitSaveResult(status: status, login: "")
        }
        guard let login = spec.login(reply.body) else { return GitSaveResult(status: .failed, login: "") }
        var keep = url.trimmingCharacters(in: .whitespacesAndNewlines)
        while keep.hasSuffix("/") {
            keep.removeLast()
        }
        guard settings.set(GitTokens.urlKey(spec.id), spec.apiIsFixed ? "" : keep),
            settings.set(GitTokens.userKey(spec.id), spec.requiresUser ? name : ""),
            store.set(GitTokens.tokenName(spec.id), secret)
        else { return GitSaveResult(status: .failed, login: "") }
        book.clear()
        return GitSaveResult(status: .saved, login: login)
    }

    /// Forgets the token and the page; the address and name stay for the next sign-in.
    public func signOut() {
        store.delete(GitTokens.tokenName(spec.id))
        book.clear()
    }

    private func getter(_ api: URL, _ token: String, _ user: String) -> GitGet {
        { [spec, http] path in
            guard let address = URL(string: api.absoluteString + path) else { return .unusable }
            var request = URLRequest(url: spec.sign(address, token: token), timeoutInterval: Self.timeout)
            request.httpMethod = "GET"
            if let authorization = spec.authorization(token: token, user: user) {
                request.setValue(authorization, forHTTPHeaderField: "Authorization")
            }
            request.setValue("application/json", forHTTPHeaderField: "Accept")
            request.setValue("Edgepad", forHTTPHeaderField: "User-Agent")
            return Self.judge(await http.send(request, maxBytes: Self.maxBytes, what: "Asking a git host"))
        }
    }

    /// Reads each reply's judgement into a `GitReply`: 401 and 403 are refusals, 429 (or 403 with Retry-After) a rate
    /// limit, any other failure is `failed`.
    private static func judge(_ reply: HttpReply?) -> GitReply {
        guard let reply else { return .unusable }
        let retry = GitJson.count(reply.header("Retry-After")).map { TimeInterval(min(Double($0), maxBackoff)) }
        let wait = retry.map { $0 > minBackoff ? $0 : minBackoff } ?? minBackoff
        let result: GitHubResult
        if reply.isSuccess {
            result = .ok
        } else if reply.status == 401 {
            result = .noPermission
        } else if reply.status == 429 {
            result = .rateLimited
        } else if reply.status == 403 {
            result = retry == nil ? .noPermission : .rateLimited
        } else {
            result = .failed
        }
        return GitReply(
            result: result, status: reply.status, retryAfter: wait, headers: reply.headers, body: reply.body)
    }
}
