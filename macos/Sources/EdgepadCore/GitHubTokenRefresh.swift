import Foundation

/// How a refresh of the access token went.
enum RefreshOutcome: Equatable {
    /// A new access token and refresh token are saved.
    case refreshed
    /// GitHub refused the refresh token, or there is none: everything saved was deleted and the user must sign in again.
    case rejected
    /// GitHub could not be asked or answered something unusable; the saved tokens are kept for the next try.
    case unavailable
}

/// Exchanges the refresh token of a GitHub App user token for a new access token (GitHub docs, "Refreshing user access
/// tokens"). A token made by the device flow needs no client secret to be refreshed, so none is sent or kept. Each
/// refresh returns a new refresh token and kills the old one. A token that does not expire (no refresh token saved) is
/// never refreshed. The twin of the laptop core's GitHubTokenRefresh; unlike it, two refreshes may overlap (only the
/// sign-in window's login lookup runs outside the API's own one-at-a-time guard), so a refusal of a refresh token that
/// has meanwhile been replaced counts as the other refresh's success.
final class GitHubTokenRefresh {
    private static let maxBytes = 16 * 1024
    private static let maxLifeSeconds = 31 * 24 * 3600
    private static let timeout: TimeInterval = 15

    /// A token this close to its end is refreshed before it is used.
    private static let margin: TimeInterval = 5 * 60

    private let store: SecretStore
    private let clientId: String
    private let http: HttpSender
    private let now: () -> Date

    init(store: SecretStore, clientId: String, http: HttpSender, clock: @escaping () -> Date) {
        self.store = store
        self.clientId = clientId
        self.http = http
        self.now = clock
    }

    /// Whether a refresh token is saved, so a 401 may be answered by a refresh.
    var canRefresh: Bool {
        GitHubApp.isToken(store.get(GitHubApp.refreshName))
    }

    /// Whether the saved access token is about to run out (or has) and can be refreshed.
    func isDue() -> Bool {
        guard canRefresh, let text = store.get(GitHubApp.expiresName), !text.isEmpty,
            text.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }), let seconds = Int64(text)
        else { return false }
        return now().addingTimeInterval(Self.margin) >= Date(timeIntervalSince1970: TimeInterval(seconds))
    }

    func refresh() async -> RefreshOutcome {
        guard let sent = store.get(GitHubApp.refreshName), GitHubApp.isToken(sent) else {
            GitHubApp.forget(store)
            return .rejected
        }
        guard let address = URL(string: GitHubApp.tokenUrl) else { return .unavailable }
        var request = URLRequest(url: address, timeoutInterval: Self.timeout)
        request.httpMethod = "POST"
        let form =
            "client_id=\(UrlText.escape(clientId))&grant_type=refresh_token&refresh_token=\(UrlText.escape(sent))"
        request.httpBody = Data(form.utf8)
        request.setValue("application/x-www-form-urlencoded; charset=utf-8", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("Edgepad", forHTTPHeaderField: "User-Agent")
        let reply = await http.send(request, maxBytes: Self.maxBytes, what: "Renewing the GitHub sign-in")
        guard let reply, let root = JsonRead.object(reply.body, maxDepth: 64) else { return .unavailable }
        if root["error"] is String {
            // GitHub answers a refused refresh token with 200 and an error; a busy server says nothing of the kind.
            if store.get(GitHubApp.refreshName) != sent { return .refreshed }
            GitHubApp.forget(store)
            return .rejected
        }
        guard reply.isSuccess, let access = root["access_token"] as? String, let next = root["refresh_token"] as? String,
            GitHubApp.isToken(access), GitHubApp.isToken(next), let life = JsonRead.int32(root["expires_in"]),
            (1...Self.maxLifeSeconds).contains(life)
        else { return .unavailable }
        let expires = now().addingTimeInterval(TimeInterval(life))
        let saved = GitHubApp.save(store, clientId, access: access, refresh: next, expires: expires)
        return saved ? .refreshed : .unavailable
    }
}
