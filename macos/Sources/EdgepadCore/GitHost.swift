import Foundation

/// Which of the phone's three filters an item belongs to. The wire letters are M, R and A.
public enum GitSection: Equatable {
    case mine
    case review
    case assigned
}

/// The `GitItem.state` tokens. Anything else reads as `open`.
public enum GitState {
    public static let open = "open"
    public static let closed = "closed"
    public static let merged = "merged"
}

/// The bounds of everything a git host sends the phone. Counts are per refresh, so a screen is one bounded list.
public enum GitLimits {
    public static let maxRepos = 30
    public static let maxItemsPerGroup = 20
    public static let maxTitle = 80
    public static let maxName = 40
    public static let maxLanguage = 30
    public static let maxUrl = 300
    public static let maxBytes = 16 * 1024
}

/// The signed-in account. `repos` and `followers` are counts, not lists.
public struct GitProfile: Equatable {
    public let login: String
    public let name: String
    public let repos: Int
    public let followers: Int

    public init(login: String, name: String, repos: Int, followers: Int) {
        self.login = login
        self.name = name
        self.repos = repos
        self.followers = followers
    }
}

/// One repository. `url` is laptop-private (never sent) and empty when the host sent none this app may open.
public struct GitRepo: Equatable {
    public let fullName: String
    public let isPrivate: Bool
    public let language: String
    public let stars: Int
    public let openIssues: Int
    public let openPulls: Int
    public let url: String

    public init(
        fullName: String, isPrivate: Bool, language: String, stars: Int, openIssues: Int, openPulls: Int, url: String
    ) {
        self.fullName = fullName
        self.isPrivate = isPrivate
        self.language = language
        self.stars = stars
        self.openIssues = openIssues
        self.openPulls = openPulls
        self.url = url
    }
}

/// One pull request or issue. `ci` is a `CiToken` raw value, `updatedMin` UTC minutes since the Unix epoch (0 when
/// unknown), `url` laptop-private like `GitRepo.url`. `label` is the text id of a host whose numbers are not integers
/// (Gitee); when set it is sent in place of `number`.
public struct GitItem: Equatable {
    public let section: GitSection
    public let repo: String
    public let number: Int64
    public let title: String
    public let state: String
    public let isPull: Bool
    public let draft: Bool
    public let ci: String
    public let updatedMin: Int64
    public let url: String
    public let label: String?

    public init(
        section: GitSection, repo: String, number: Int64, title: String, state: String, isPull: Bool, draft: Bool,
        ci: String, updatedMin: Int64, url: String, label: String? = nil
    ) {
        self.section = section
        self.repo = repo
        self.number = number
        self.title = title
        self.state = state
        self.isPull = isPull
        self.draft = draft
        self.ci = ci
        self.updatedMin = updatedMin
        self.url = url
        self.label = label
    }
}

/// What one host knows about the account: at most `GitLimits.maxRepos` repositories, recent first, and at most
/// `GitLimits.maxItemsPerGroup` items per section.
public struct GitSnapshot: Equatable {
    public let profile: GitProfile
    public let repos: [GitRepo]
    public let items: [GitItem]
    public var unread: Int?

    public init(profile: GitProfile, repos: [GitRepo], items: [GitItem], unread: Int? = nil) {
        self.profile = profile
        self.repos = repos
        self.items = items
        self.unread = unread
    }
}

/// Opens an address in the person's default browser. The Mac app implements it; `open` says whether it tried.
public protocol UrlOpening: AnyObject {
    func open(_ url: URL) -> Bool
}

/// One git host the laptop is signed in to. Read only: no member can change anything on the host.
public protocol GitHost: AnyObject {
    /// The `DevProvider.id` and the first field of a `GitOpen` text.
    var id: String { get }

    /// The one web host name whose https links this host's pages may open, for example `github.com`.
    var domain: String { get }

    /// The last good result as the blob and the addresses its indexes stand for; nil before the first one and after a
    /// sign-out.
    var page: GitPage? { get }

    /// Asks the host once.
    func refresh() async -> GitHubRefreshResult
}

/// One state of a host as the phone is told it. `body` is the BLOB GIT payload; `urls` holds, in order, the address
/// behind each repository and item line the body kept, so a `GitOpen` index (0 is the first such line) is a position
/// in it. The addresses are never in the body.
public struct GitPage: Equatable {
    public let rev: Int64
    public let body: String
    public let urls: [String]

    public init(rev: Int64, body: String, urls: [String]) {
        self.rev = rev
        self.body = body
        self.urls = urls
    }
}

/// The body of a BLOB GIT: UTF-8 lines joined by line feeds, fields by tabs, at most `GitLimits.maxBytes`, the last
/// lines dropped until it fits. Line one is `H host login name repos followers rev`. An optional `N` line (GitHub's
/// unread count) keeps the older six-field shape: `N, empty, count, empty, empty, none`. Then
/// `P repo private language stars issues pulls` (private is `p` or empty) for each repository, and
/// `M|R|A repo number title flags ci state updatedMin` for each item (flags: `d` draft, `i` an issue). Every free-text
/// field is cleaned and cut. Byte for byte the laptop core's GitBlob.
public enum GitBlob {
    public static func encode(_ host: String, _ snapshot: GitSnapshot, _ rev: Int64) -> GitPage {
        var rows: [(row: String, url: String?)] = [
            (
                join([
                    "H", DashWire.clean(host, 16), DashWire.clean(snapshot.profile.login, 39),
                    DashWire.clean(snapshot.profile.name, GitLimits.maxName), num(Int64(snapshot.profile.repos)),
                    num(Int64(snapshot.profile.followers)), num(rev),
                ]), nil
            )
        ]
        if let unread = snapshot.unread {
            rows.append((join(["N", "", num(Int64(unread)), "", "", CiToken.none.rawValue]), nil))
        }
        let repos = snapshot.repos.filter { GitHubParse.isRepo($0.fullName) }.prefix(GitLimits.maxRepos)
        for repo in repos {
            rows.append(
                (
                    join([
                        "P", repo.fullName, repo.isPrivate ? "p" : "",
                        DashWire.clean(repo.language, GitLimits.maxLanguage), num(Int64(repo.stars)),
                        num(Int64(repo.openIssues)), num(Int64(repo.openPulls)),
                    ]), repo.url
                ))
        }
        for section in [GitSection.mine, .review, .assigned] {
            let items = snapshot.items.filter {
                $0.section == section && ($0.number > 0 || !($0.label ?? "").isEmpty) && GitHubParse.isRepo($0.repo)
            }
            for item in items.prefix(GitLimits.maxItemsPerGroup) {
                let flags = (item.draft ? "d" : "") + (item.isPull ? "" : "i")
                let state = item.state == GitState.closed || item.state == GitState.merged ? item.state : GitState.open
                let known = [CiToken.ok.rawValue, CiToken.fail.rawValue, CiToken.pending.rawValue]
                let ci = known.contains(item.ci) ? item.ci : CiToken.none.rawValue
                let id = (item.label ?? "").isEmpty ? num(item.number) : DashWire.clean(item.label ?? "", 20)
                rows.append(
                    (
                        join([
                            letter(section), item.repo, id, DashWire.clean(item.title, GitLimits.maxTitle), flags, ci,
                            state, num(item.updatedMin),
                        ]), item.url
                    ))
            }
        }
        let body = DashWire.fit(rows.map { $0.row }, "\n", GitLimits.maxBytes)
        let kept = body.isEmpty ? 0 : body.utf8.filter { $0 == 10 }.count + 1
        return GitPage(rev: rev, body: body, urls: rows.prefix(kept).compactMap { $0.url })
    }

    private static func letter(_ section: GitSection) -> String {
        switch section {
        case .review: return "R"
        case .assigned: return "A"
        case .mine: return "M"
        }
    }

    private static func num(_ value: Int64) -> String {
        String(max(0, value))
    }

    private static func join(_ fields: [String]) -> String {
        fields.joined(separator: "\t")
    }
}

/// One host's pages. `update` gives the new snapshot a revision only when what the phone would see (or open) changed,
/// so a refresh that finds nothing new sends nothing. The revision never goes back, not even across a `clear`, so a
/// `GitOpen` aimed at a page from before a sign-out can never match a page after it.
public final class GitBook {
    private let host: String
    private let gate = NSLock()
    private var rev: Int64 = 0
    private var key: String?
    private var current: GitPage?

    public init(host: String) {
        self.host = host
    }

    public var page: GitPage? {
        gate.withLock { current }
    }

    @discardableResult
    public func update(_ snapshot: GitSnapshot) -> GitPage {
        gate.withLock {
            let probe = GitBlob.encode(host, snapshot, 0)
            let next = probe.body + "\u{0001}" + probe.urls.joined(separator: "\u{0001}")
            if let current, next == key {
                return current
            }
            key = next
            rev += 1
            let made = GitBlob.encode(host, snapshot, rev)
            current = made
            return made
        }
    }

    public func clear() {
        gate.withLock {
            current = nil
            key = nil
        }
    }
}

/// Decides whether a phone's `GitOpen` text (`host/listRevision/index`) may open anything. The phone names a place in
/// a list, never an address: the laptop opens only an https address it fetched itself, for exactly the revision it
/// last sent, at an index that list holds, on the host's own domain. Anything else is refused and the caller drops and
/// counts it.
public enum GitOpenGate {
    private static let maxWire = 64
    private static let maxHost = 16

    /// - Parameters:
    ///   - wire: The phone's text.
    ///   - hosts: Finds the connected host with this id, or nil.
    /// - Returns: The address to open, or nil when nothing may be opened.
    public static func resolve(_ wire: String?, hosts: (String) -> GitHost?) -> URL? {
        guard let wire, !wire.isEmpty, wire.utf16.count <= maxWire else { return nil }
        let parts = wire.split(separator: "/", omittingEmptySubsequences: false).map(String.init)
        guard parts.count == 3, (1...maxHost).contains(parts[0].utf16.count), let rev = whole(parts[1]),
            let index = whole(parts[2]), index >= 0, let host = hosts(parts[0]), let page = host.page,
            page.rev == rev, index < Int64(page.urls.count)
        else { return nil }
        return https(page.urls[Int(index)], domain: host.domain)
    }

    /// An optional minus sign and ASCII digits, nothing else, that fits an Int64.
    static func whole(_ text: String) -> Int64? {
        let digits = text.hasPrefix("-") ? String(text.dropFirst()) : text
        guard !digits.isEmpty, text.utf8.count <= 19, digits.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }) else {
            return nil
        }
        return Int64(text)
    }

    /// The address when it is plain https (default port, no user info) on `domain`, else nil.
    static func https(_ text: String, domain: String) -> URL? {
        guard var parts = URLComponents(string: text), parts.scheme?.lowercased() == "https", parts.user == nil,
            parts.password == nil, parts.port == nil || parts.port == 443,
            let host = parts.host, host.caseInsensitiveCompare(domain) == .orderedSame
        else { return nil }
        parts.port = nil
        return parts.url
    }
}
