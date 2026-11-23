import Foundation

/// The gitlab.com OAuth application Edgepad signs in through, and the names its tokens are saved under. The twin of the
/// laptop core's GitLabApp.
public enum GitLabApp {
    /// The Application ID of the "Edgepad" application on gitlab.com, the same value as GitLabApp.cs: not confidential,
    /// device authorization grant on, scopes read_api and read_user only. An Application ID is public by design; no
    /// client secret is used or kept anywhere.
    public static let clientId = "90603e45237215e265b1cc5a873ec796353c5e3185c14be0341a371a0dc5d000"

    /// The two scopes asked for, space separated. Both are read only.
    public static let scope = "read_api read_user"

    /// The access token. A self-hosted server's pasted token is kept under the same name by `GitTokens.tokenName`.
    public static let tokenName = "gitlab.token"
    public static let refreshName = "gitlab.refresh"

    /// When the access token expires, Unix seconds (absent when it does not expire).
    public static let expiresName = "gitlab.expires"

    /// The Application ID that issued the saved token.
    public static let clientName = "gitlab.client"

    /// Set to "1" when GitLab refused the refresh token: the tokens are gone and the person must sign in again.
    /// Cleared by any sign-in, token or sign-out.
    public static let expiredName = "gitlab.expired"

    /// The address a gitlab.com personal access token is saved with ("Use a token instead"): a token does not run out
    /// every 2 hours, so it is the reliable fallback. The window passes this constant to `GitLabHost.saveServer`; the
    /// person never types it.
    public static let comUrl = "https://gitlab.com"

    /// The page the person opens to type the code; always this constant, never an address a server sent.
    public static let devicePage = "https://gitlab.com/oauth/device"

    static let deviceCodeUrl = "https://gitlab.com/oauth/authorize_device"
    static let tokenUrl = "https://gitlab.com/oauth/token"

    /// The API root of gitlab.com.
    static let api = "https://gitlab.com/api/v4/"

    private final class State {
        let gate = NSLock()
        var epoch = 0
    }

    // One per secret store, shared by the host, the sign-in and the renewal, so no two of them write tokens at once.
    private static let registry = NSLock()
    private static var states: [ObjectIdentifier: State] = [:]

    private static func state(_ store: SecretStore) -> State {
        registry.withLock {
            if let found = states[ObjectIdentifier(store)] { return found }
            let made = State()
            states[ObjectIdentifier(store)] = made
            return made
        }
    }

    /// The lock every write of a gitlab.com token and every write of the server address holds.
    static func lock(_ store: SecretStore) -> NSLock {
        state(store).gate
    }

    /// The sign-in generation: moved by every sign-in start, server save and sign-out, so a result that began earlier
    /// is known to be stale.
    static func epoch(_ store: SecretStore) -> Int {
        let made = state(store)
        return made.gate.withLock { made.epoch }
    }

    /// Moves the generation on; whatever began before is dropped when it finishes. The caller holds `lock(store)`.
    static func bumpLocked(_ store: SecretStore) {
        state(store).epoch += 1
    }

    /// Moves the generation on, taking the lock.
    static func bump(_ store: SecretStore) {
        lock(store).withLock { bumpLocked(store) }
    }

    /// Starts a gitlab.com sign-in: forgets every saved token and the server address (a token for one server must never
    /// sit beside another), then returns the new generation; nil when the address could not be cleared.
    static func begin(_ store: SecretStore, _ settings: IntegrationSettings) -> Int? {
        let made = state(store)
        return made.gate.withLock { () -> Int? in
            forget(store)
            guard settings.set(GitTokens.urlKey("gitlab"), "") else { return nil }
            made.epoch += 1
            return made.epoch
        }
    }

    /// Saves a gitlab.com token only if nothing has moved since it was asked for: the generation is still `epoch`, no
    /// server address is saved and, for a renewal, the saved refresh token is still `sentRefresh`. Otherwise the
    /// result is dropped, so a gitlab.com token can never land beside a self-hosted address.
    static func saveIfCurrent(
        _ store: SecretStore, _ settings: IntegrationSettings, epoch: Int, sentRefresh: String?, access: String,
        refresh: String?, expires: Date?
    ) -> Bool {
        let made = state(store)
        return made.gate.withLock { () -> Bool in
            guard made.epoch == epoch, settings.text(GitTokens.urlKey("gitlab")).isEmpty,
                sentRefresh == nil || store.get(refreshName) == sentRefresh
            else { return false }
            return save(store, clientId, access: access, refresh: refresh, expires: expires)
        }
    }

    /// Forgets everything saved and records that the sign-in expired (so the person is told, not silently signed out),
    /// but only while nothing has moved since `epoch` and the refresh token is still the one sent.
    static func forgetIfCurrent(_ store: SecretStore, epoch: Int, sentRefresh: String?) {
        let made = state(store)
        made.gate.withLock {
            if made.epoch == epoch && store.get(refreshName) == sentRefresh {
                forget(store)
                _ = store.set(expiredName, "1")
            }
        }
    }

    /// Whether the last gitlab.com sign-in was refused a renewal and no token has been saved since.
    public static func isExpired(_ store: SecretStore) -> Bool {
        store.get(expiredName) == "1" && !GitTokens.isToken(store.get(tokenName))
    }

    /// Saves a token with its record; false when it could not be saved (nothing half-saved stays). The order is
    /// refresh token, access token, expiry, then the record, so a stop at any point leaves a pair that works or one
    /// that refreshes.
    static func save(
        _ store: SecretStore, _ clientId: String, access: String, refresh: String?, expires: Date?
    ) -> Bool {
        let saved: Bool
        if let refresh, let expires {
            let until = String(Int64(expires.timeIntervalSince1970))
            saved = store.set(refreshName, refresh) && store.set(tokenName, access) && store.set(expiresName, until)
        } else {
            saved = store.set(tokenName, access)
            if saved { forgetRenewal(store) }
        }
        guard saved, store.set(clientName, clientId) else {
            forget(store)
            return false
        }
        store.delete(expiredName)
        return true
    }

    /// Deletes the refresh token, the expiry, the record and the expired mark: what a pasted token must not sit beside.
    static func forgetRenewal(_ store: SecretStore) {
        store.delete(expiredName)
        store.delete(refreshName)
        store.delete(expiresName)
        store.delete(clientName)
    }

    /// Deletes the token and everything that belongs to it.
    static func forget(_ store: SecretStore) {
        store.delete(tokenName)
        forgetRenewal(store)
    }
}

/// gitlab.com sign-in by the OAuth device authorization grant (https://docs.gitlab.com/api/oauth2/): ask for a code
/// with `GitLabApp.clientId` and the scopes `GitLabApp.scope`, show it, and poll until the person has entered it at
/// `GitLabApp.devicePage`. The tokens go straight into the `SecretStore`; they are never logged, never returned and
/// never sent to the phone. Only gitlab.com is asked, redirects are refused, every reply is capped at 16 KiB and 15 s,
/// and there is no client secret anywhere. One flow at a time: starting another cancels the first. A self-hosted server
/// does not use this: it takes a pasted token (see `GitLabHost.saveServer`). The twin of the core's GitLabSignIn.
public final class GitLabSignIn {
    private static let maxLifeSeconds = 31 * 24 * 3600
    private static let grantType = "urn:ietf:params:oauth:grant-type:device_code"
    private static let timeout: TimeInterval = 15

    private enum Poll {
        case pending
        case slowDown
        case token(String, String?, Int)
        case end(SignInState)
    }

    private let store: SecretStore
    private let settings: IntegrationSettings
    private let http: HttpSender
    private let now: () -> Date
    private let delay: (TimeInterval) async throws -> Void
    private let gate = NSLock()
    private var current: GitHubSignIn.Flow?

    public init(
        store: SecretStore,
        settings: IntegrationSettings,
        http: HttpSender = CappedHttp(),
        clock: @escaping () -> Date = Date.init,
        delay: @escaping (TimeInterval) async throws -> Void = { try await Task.sleep(nanoseconds: UInt64($0 * 1e9)) }
    ) {
        self.store = store
        self.settings = settings
        self.http = http
        self.now = clock
        self.delay = delay
    }

    /// Whether a usable token is saved (from this sign-in or pasted for a server).
    public var isSignedIn: Bool {
        GitTokens.isToken(store.get(GitLabApp.tokenName))
    }

    /// The form body of the device request: this application and the two read scopes, spaces written as `+` as
    /// GitLab's docs do.
    static func deviceForm() -> String {
        let scope = UrlText.escape(GitLabApp.scope).replacingOccurrences(of: "%20", with: "+")
        return "client_id=\(UrlText.escape(GitLabApp.clientId))&scope=\(scope)"
    }

    /// Asks gitlab.com for a code. `.failed` when it cannot be asked or answers outside the documented ranges.
    public func start() async -> DeviceCode {
        cancel()
        let reply = await post(GitLabApp.deviceCodeUrl, Self.deviceForm(), "Asking GitLab for a sign-in code")
        guard let reply, reply.isSuccess, let issued = GitHubSignIn.parseDeviceCode(reply.body) else {
            return DeviceCode(state: .failed, userCode: "", page: GitLabApp.devicePage, expiresIn: 0)
        }
        // `begin` forgets every saved token and the server address, so the tokens this flow saves can never sit beside
        // another server.
        guard let epoch = GitLabApp.begin(store, settings) else {
            return DeviceCode(state: .failed, userCode: "", page: GitLabApp.devicePage, expiresIn: 0)
        }
        let flow = GitHubSignIn.Flow(
            deviceCode: issued.deviceCode, interval: issued.interval,
            deadline: now().addingTimeInterval(TimeInterval(issued.expiresIn)))
        flow.epoch = epoch
        let old = gate.withLock { () -> GitHubSignIn.Flow? in
            let previous = current
            current = flow
            return previous
        }
        old?.stop()
        return DeviceCode(
            state: .started, userCode: issued.userCode, page: GitLabApp.devicePage,
            expiresIn: TimeInterval(issued.expiresIn))
    }

    /// Polls until the person has entered the code, said no, or the code ran out, honouring GitLab's interval and its
    /// slow_down. Cancelling the calling task or calling `cancel` ends it as `.cancelled`.
    public func wait() async -> SignInState {
        let flow = gate.withLock { current }
        guard let flow, flow.claim() else { return .failed }
        defer { gate.withLock { if current === flow { current = nil } } }
        var interval = flow.interval
        let form =
            "client_id=\(UrlText.escape(GitLabApp.clientId))&device_code=\(UrlText.escape(flow.deviceCode))"
            + "&grant_type=\(UrlText.escape(Self.grantType))"
        while true {
            let seconds = TimeInterval(interval)
            let delay = self.delay
            await flow.sleep(Task { try await delay(seconds) })
            if Task.isCancelled || flow.isStopped { return .cancelled }
            if now() >= flow.deadline { return .expired }
            let reply = await post(GitLabApp.tokenUrl, form, "Waiting for GitLab sign-in")
            if Task.isCancelled || flow.isStopped { return .cancelled }
            switch Self.readPoll(reply) {
            case .token(let token, let refresh, let life):
                return save(token, refresh, life, flow.epoch) ? .signedIn : .failed
            case .pending:
                break
            case .slowDown:
                interval += GitHubSignIn.slowDownSeconds
            case .end(let state):
                return state
            }
        }
    }

    /// Stops a sign-in in progress; its `wait` ends as `.cancelled`.
    public func cancel() {
        gate.lock()
        let flow = current
        current = nil
        gate.unlock()
        flow?.stop()
    }

    /// Saves the tokens for gitlab.com, but only if no server address was saved and no other sign-in began since this
    /// flow started; otherwise they are dropped.
    private func save(_ token: String, _ refresh: String?, _ life: Int, _ epoch: Int) -> Bool {
        let expires = refresh == nil ? nil : now().addingTimeInterval(TimeInterval(life))
        return GitLabApp.saveIfCurrent(
            store, settings, epoch: epoch, sentRefresh: nil, access: token, refresh: refresh, expires: expires)
    }

    private static func readPoll(_ reply: HttpReply?) -> Poll {
        guard let reply else { return .pending }
        guard let parsed = JsonRead.parse(reply.body, maxDepth: 64) else {
            // A server error page, not an answer: try again at the next interval, until the code runs out.
            return reply.isSuccess ? .end(.failed) : .pending
        }
        guard let root = parsed as? [String: Any] else { return .end(.failed) }
        if reply.isSuccess, let token = root["access_token"] as? String {
            return readToken(root, token)
        }
        switch root["error"] as? String {
        case "authorization_pending": return .pending
        case "slow_down": return .slowDown
        case "expired_token": return .end(.expired)
        case "access_denied": return .end(.denied)
        case nil where !reply.isSuccess: return .pending
        default: return .end(.failed)
        }
    }

    /// A token reply: a refresh token needs an expires_in (1 s..31 days) beside it. A reply without a refresh token is
    /// kept as a token that is used until GitLab refuses it. A value outside the ranges is a failure.
    private static func readToken(_ root: [String: Any], _ token: String) -> Poll {
        let refresh = root["refresh_token"] as? String
        let life = JsonRead.int32(root["expires_in"]) ?? 0
        let fine =
            GitTokens.isToken(token)
            && (refresh == nil || (GitTokens.isToken(refresh) && (1...maxLifeSeconds).contains(life)))
        return fine ? .token(token, refresh, life) : .end(.failed)
    }

    private func post(_ url: String, _ form: String, _ what: String) async -> HttpReply? {
        guard let address = URL(string: url) else { return nil }
        var request = URLRequest(url: address, timeoutInterval: Self.timeout)
        request.httpMethod = "POST"
        request.httpBody = Data(form.utf8)
        request.setValue("application/x-www-form-urlencoded; charset=utf-8", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("Edgepad", forHTTPHeaderField: "User-Agent")
        return await http.send(request, maxBytes: GitHubSignIn.maxBytes, what: what)
    }
}
