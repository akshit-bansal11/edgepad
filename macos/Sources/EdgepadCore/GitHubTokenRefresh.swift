import Foundation

/// How a refresh of the access token went.
enum RefreshOutcome: Equatable {
    /// A new access token and refresh token are saved.
    case refreshed
    /// GitHub refused the refresh token, or there is none: everything saved was deleted; sign in again.
    case rejected
    /// GitHub could not be asked or answered something unusable; the saved tokens are kept for the next try.
    case unavailable
}

/// Exchanges the refresh token of a GitHub App user token for a new access token (GitHub docs, "Refreshing user access
/// tokens"). A token made by the device flow needs no client secret to be refreshed, so none is sent or kept. Each
/// refresh returns a new refresh token and kills the old one, so refreshes never overlap: a second caller waits for the
/// first one's answer. A token that does not expire (no refresh token saved) is never refreshed. The twin of the laptop
/// core's GitHubTokenRefresh.
final class GitHubTokenRefresh {
    private static let maxBytes = 16 * 1024
    private static let maxLifeSeconds = 31 * 24 * 3600
    private static let timeout: TimeInterval = 15

    private let store: SecretStore
    private let clientId: String
    private let http: HttpSender
    private let now: () -> Date

    // One flight per store, shared by every GitHubApi on it (the window's login lookup builds its own), so refreshes
    // never overlap.
    private static let flight = RefreshFlight()

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
        canRefresh && RefreshFlight.isDue(store.get(GitHubApp.expiresName), now: now())
    }

    func refresh() async -> RefreshOutcome {
        await Self.flight.join(store) { await self.exchange() }
    }

    /// The exchange itself, in a task of its own: a caller that is cancelled while it runs cannot lose the new refresh
    /// token, and the 15 s timeout bounds it.
    private func exchange() async -> RefreshOutcome {
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
        guard let reply else { return .unavailable }
        if reply.status == 401 {
            GitHubApp.forget(store)
            return .rejected
        }
        guard let root = JsonRead.object(reply.body, maxDepth: 64) else { return .unavailable }
        if let error = root["error"] as? String {
            // GitHub answers a refused refresh token with 200 and bad_refresh_token. Any other error (a busy server, a
            // misunderstanding) keeps the tokens for the next try.
            guard error == "bad_refresh_token" else { return .unavailable }
            if store.get(GitHubApp.refreshName) != sent { return .refreshed }
            GitHubApp.forget(store)
            return .rejected
        }
        guard reply.isSuccess, let access = root["access_token"] as? String,
            let next = root["refresh_token"] as? String, GitHubApp.isToken(access), GitHubApp.isToken(next),
            let life = JsonRead.int32(root["expires_in"]),
            (1...Self.maxLifeSeconds).contains(life)
        else { return .unavailable }
        let expires = now().addingTimeInterval(TimeInterval(life))
        let saved = GitHubApp.save(store, clientId, access: access, refresh: next, expires: expires)
        return saved ? .refreshed : .unavailable
    }
}
