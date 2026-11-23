import Foundation

/// A request the `ReadOnlyGate` refused. It means the code asked for something it must not. `message` names the host
/// and path only, never the query string.
public struct ReadOnlyViolation: Error, Equatable, CustomStringConvertible {
    public let message: String

    public var description: String { message }
}

/// Every git host's request passes through here (`CappedHttp.send` checks it before anything leaves the Mac), so
/// Edgepad can read a host and never change anything on it. It refuses every request that is not https, and every one
/// that is not GET or HEAD except two kinds of POST: a GraphQL query to `https://api.github.com/graphql` whose `query`
/// contains no `mutation`, and the OAuth device-flow calls to the host's own sign-in endpoints (GitHub's
/// `/login/device/code` and `/login/oauth/access_token`, GitLab's `/oauth/authorize_device` and `/oauth/token`). The
/// twin of the laptop core's ReadOnlyGate.
public enum ReadOnlyGate {
    private static let deviceFlow: [(host: String, path: String)] = [
        ("github.com", "/login/device/code"),
        ("github.com", "/login/oauth/access_token"),
        ("gitlab.com", "/oauth/authorize_device"),
        ("gitlab.com", "/oauth/token"),
    ]

    private static let graphQl = (host: "api.github.com", path: "/graphql")

    /// Whether this request may go out.
    public static func allows(_ request: URLRequest) -> Bool {
        guard let url = request.url, let parts = URLComponents(url: url, resolvingAgainstBaseURL: false),
            parts.scheme?.lowercased() == "https", let host = parts.host?.lowercased()
        else { return false }
        let method = (request.httpMethod ?? "GET").uppercased()
        if method == "GET" || method == "HEAD" {
            return true
        }
        guard method == "POST" else { return false }
        let path = parts.percentEncodedPath
        if deviceFlow.contains(where: { $0.host == host && $0.path == path }) {
            return true
        }
        return host == graphQl.host && path == graphQl.path && request.httpBody.map(isQuery) ?? false
    }

    /// Throws `ReadOnlyViolation` for a request `allows` refuses.
    public static func verify(_ request: URLRequest) throws {
        guard allows(request) else {
            let parts = request.url.flatMap { URLComponents(url: $0, resolvingAgainstBaseURL: false) }
            let target = (parts?.host ?? "") + (parts?.percentEncodedPath ?? "")
            throw ReadOnlyViolation(message: "\(request.httpMethod ?? "GET") \(target) is not read only")
        }
    }

    /// A JSON object whose `query` string has no `mutation` in it, in any case. Judged after the JSON is decoded, so an
    /// escape cannot hide the word.
    private static func isQuery(_ body: Data) -> Bool {
        guard let root = JsonRead.object(body, maxDepth: 64), let text = root["query"] as? String else { return false }
        return !text.lowercased().contains("mutation")
    }
}
