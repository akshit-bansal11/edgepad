import Foundation

/// The GitHub OAuth App Edgepad signs in through, and the fixed names and addresses that go with it. The twin of the
/// laptop core's GitHubApp.
public enum GitHubApp {
    /// PLACEHOLDER: the OAuth App's public Client ID, empty until the owner registers the app (plan step O1), and the
    /// same value as GitHubApp.cs. While it is empty, sign-in reports `SignInState.notConfigured` and asks nobody. A
    /// Client ID is public by design; the device flow needs no client secret, and none may ever be added.
    public static let clientId = ""

    /// The `SecretStore` name the token is kept under.
    public static let tokenName = "github.token"

    /// Public repositories and notifications: the default.
    public static let scopePublic = "public_repo notifications"

    /// Private repositories too, when the user ticks "Include private repositories".
    public static let scopePrivate = "repo notifications"

    private static let maxClientId = 64
    private static let maxToken = 255

    /// The page the user opens to type the code; always this constant, never an address a server sent.
    public static let devicePage = "https://github.com/login/device"

    static let deviceCodeUrl = "https://github.com/login/device/code"
    static let tokenUrl = "https://github.com/login/oauth/access_token"

    /// Whether `clientId` looks like a real Client ID; the empty placeholder does not.
    public static func isConfigured(_ clientId: String) -> Bool {
        (1...maxClientId).contains(clientId.utf8.count)
            && clientId.unicodeScalars.allSatisfy { isAsciiLetterOrDigit($0) || $0 == "." || $0 == "_" }
    }

    /// A token GitHub could have issued: 1..255 ASCII letters, digits and underscores. Anything else is never stored or
    /// sent.
    public static func isToken(_ token: String?) -> Bool {
        guard let token, (1...maxToken).contains(token.utf8.count) else { return false }
        return token.unicodeScalars.allSatisfy { isAsciiLetterOrDigit($0) || $0 == "_" }
    }

    static func isAsciiLetterOrDigit(_ scalar: Unicode.Scalar) -> Bool {
        ("a"..."z").contains(scalar) || ("A"..."Z").contains(scalar) || ("0"..."9").contains(scalar)
    }
}

/// Where a sign-in stands.
public enum SignInState: Equatable {
    /// A code was issued; show it and wait.
    case started
    /// The token is saved.
    case signedIn
    /// No Client ID is built in (`GitHubApp.clientId` is the placeholder); nobody was asked.
    case notConfigured
    /// The user said no on GitHub.
    case denied
    /// The code ran out before the user entered it.
    case expired
    /// Stopped here, or replaced by a newer sign-in.
    case cancelled
    /// GitHub could not be asked, answered something unusable, or the token could not be saved.
    case failed
}

/// What `GitHubSignIn.start` gives the window: the code to show and the page to open, only when `state` is `.started`.
public struct DeviceCode: Equatable {
    public let state: SignInState
    public let userCode: String
    public let page: String
    public let expiresIn: TimeInterval

    static func not(_ state: SignInState) -> DeviceCode {
        DeviceCode(state: state, userCode: "", page: GitHubApp.devicePage, expiresIn: 0)
    }
}

/// GitHub sign-in by the OAuth device flow (GitHub docs, "Authorizing OAuth apps", device flow): ask github.com for a
/// code, show it, and poll until the user has entered it at https://github.com/login/device. The token goes straight
/// into the `SecretStore` under `GitHubApp.tokenName`; it is never logged, never returned, and never sent to the phone.
/// github.com is the only host asked, redirects are refused, every reply is capped at 16 KiB and 15 s, and there is no
/// client secret anywhere. One flow at a time: starting another cancels the first. The twin of the core's GitHubSignIn.
public final class GitHubSignIn {
    public static let maxBytes = 16 * 1024
    public static let slowDownSeconds = 5

    private static let defaultInterval = 5
    private static let maxDeviceCode = 255
    private static let grantType = "urn:ietf:params:oauth:grant-type:device_code"
    private static let timeout: TimeInterval = 15

    /// A device code reply that passed every check.
    struct IssuedCode: Equatable {
        let deviceCode: String
        let userCode: String
        let interval: Int
        let expiresIn: Int
    }

    private enum Poll {
        case pending
        case slowDown
        case token(String)
        case end(SignInState)
    }

    /// One sign-in: its device code, GitHub's interval, the moment the code runs out, and the wait it may be in.
    private final class Flow {
        let deviceCode: String
        let interval: Int
        let deadline: Date
        private let gate = NSLock()
        private var stopped = false
        private var waiting = false
        private var sleeper: Task<Void, Error>?

        init(deviceCode: String, interval: Int, deadline: Date) {
            self.deviceCode = deviceCode
            self.interval = interval
            self.deadline = deadline
        }

        var isStopped: Bool {
            gate.lock()
            defer { gate.unlock() }
            return stopped
        }

        /// True the first time only: one wait per flow.
        func claim() -> Bool {
            gate.lock()
            defer { gate.unlock() }
            if waiting { return false }
            waiting = true
            return true
        }

        func stop() {
            gate.lock()
            stopped = true
            let sleeping = sleeper
            gate.unlock()
            sleeping?.cancel()
        }

        /// Sleeps through `sleep`, cut short by `stop` or by cancelling the waiting task.
        func sleep(_ sleep: Task<Void, Error>) async {
            let already = gate.withLock { () -> Bool in
                sleeper = sleep
                return stopped
            }
            if already { sleep.cancel() }
            await withTaskCancellationHandler {
                _ = try? await sleep.value
            } onCancel: {
                sleep.cancel()
            }
        }
    }

    private let store: SecretStore
    private let clientId: String
    private let http: HttpSender
    private let now: () -> Date
    private let delay: (TimeInterval) async throws -> Void
    private let gate = NSLock()
    private var current: Flow?

    public init(
        store: SecretStore,
        clientId: String = GitHubApp.clientId,
        http: HttpSender = CappedHttp(),
        clock: @escaping () -> Date = Date.init,
        delay: @escaping (TimeInterval) async throws -> Void = { try await Task.sleep(nanoseconds: UInt64($0 * 1e9)) }
    ) {
        self.store = store
        self.clientId = clientId
        self.http = http
        self.now = clock
        self.delay = delay
    }

    /// Whether a usable token is saved.
    public var isSignedIn: Bool {
        GitHubApp.isToken(store.get(GitHubApp.tokenName))
    }

    /// Asks GitHub for a code. `.notConfigured` without a request while the Client ID is the placeholder; `.failed`
    /// when GitHub cannot be asked or answers outside the documented ranges.
    public func start(includePrivate: Bool) async -> DeviceCode {
        guard GitHubApp.isConfigured(clientId) else { return .not(.notConfigured) }
        cancel()
        let scope = includePrivate ? GitHubApp.scopePrivate : GitHubApp.scopePublic
        let form = "client_id=\(UrlText.escape(clientId))&scope=\(UrlText.escape(scope))"
        let reply = await post(GitHubApp.deviceCodeUrl, form, "Asking GitHub for a sign-in code")
        guard let reply, reply.isSuccess, let issued = Self.parseDeviceCode(reply.body) else { return .not(.failed) }
        let flow = Flow(
            deviceCode: issued.deviceCode, interval: issued.interval,
            deadline: now().addingTimeInterval(TimeInterval(issued.expiresIn)))
        let old = gate.withLock { () -> Flow? in
            let previous = current
            current = flow
            return previous
        }
        old?.stop()
        return DeviceCode(
            state: .started, userCode: issued.userCode, page: GitHubApp.devicePage,
            expiresIn: TimeInterval(issued.expiresIn))
    }

    /// Polls until the user has entered the code, said no, or the code ran out, honouring GitHub's interval and its
    /// slow_down. Cancelling the calling task or calling `cancel` ends it as `.cancelled`. A failed poll is tried again
    /// at the next interval until the code runs out.
    public func wait() async -> SignInState {
        let flow = gate.withLock { current }
        guard let flow, flow.claim() else { return .failed }
        defer { gate.withLock { if current === flow { current = nil } } }
        var interval = flow.interval
        let form =
            "client_id=\(UrlText.escape(clientId))&device_code=\(UrlText.escape(flow.deviceCode))"
            + "&grant_type=\(UrlText.escape(Self.grantType))"
        while true {
            let seconds = TimeInterval(interval)
            let delay = self.delay
            await flow.sleep(Task { try await delay(seconds) })
            if Task.isCancelled || flow.isStopped { return .cancelled }
            if now() >= flow.deadline { return .expired }
            let reply = await post(GitHubApp.tokenUrl, form, "Waiting for GitHub sign-in")
            if Task.isCancelled || flow.isStopped { return .cancelled }
            switch Self.readPoll(reply) {
            case .token(let token):
                return store.set(GitHubApp.tokenName, token) ? .signedIn : .failed
            case .pending:
                break
            case .slowDown:
                interval += Self.slowDownSeconds
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

    /// Stops any sign-in and forgets the token.
    public func signOut() {
        cancel()
        store.delete(GitHubApp.tokenName)
    }

    /// The device code reply: device_code (1..255 printable ASCII), user_code matching `^[A-Z0-9-]{4,20}$`, interval
    /// 1..60 (5 when absent) and expires_in 60..1800 seconds. verification_uri is never read: the page shown is
    /// `GitHubApp.devicePage`.
    static func parseDeviceCode(_ body: Data) -> IssuedCode? {
        guard let root = JsonRead.object(body, maxDepth: 64) else { return nil }
        guard let deviceCode = root["device_code"] as? String, let userCode = root["user_code"] as? String,
            let expires = JsonRead.int32(root["expires_in"])
        else { return nil }
        let interval = root.keys.contains("interval") ? JsonRead.int32(root["interval"]) : Optional(defaultInterval)
        guard let every = interval, (1...60).contains(every), (60...1800).contains(expires) else { return nil }
        guard (1...maxDeviceCode).contains(deviceCode.utf16.count),
            deviceCode.unicodeScalars.allSatisfy({ $0.value > 0x20 && $0.value <= 0x7E })
        else { return nil }
        guard (4...20).contains(userCode.utf16.count),
            userCode.unicodeScalars.allSatisfy({ ("A"..."Z").contains($0) || ("0"..."9").contains($0) || $0 == "-" })
        else { return nil }
        return IssuedCode(deviceCode: deviceCode, userCode: userCode, interval: every, expiresIn: expires)
    }

    private static func readPoll(_ reply: HttpReply?) -> Poll {
        guard let reply else { return .pending }
        guard let parsed = JsonRead.parse(reply.body, maxDepth: 64) else {
            // A server error page, not an answer: try again at the next interval, until the code runs out.
            return reply.isSuccess ? .end(.failed) : .pending
        }
        guard let root = parsed as? [String: Any] else { return .end(.failed) }
        if reply.isSuccess, let token = root["access_token"] as? String {
            return GitHubApp.isToken(token) ? .token(token) : .end(.failed)
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

    private func post(_ url: String, _ form: String, _ what: String) async -> HttpReply? {
        guard let address = URL(string: url) else { return nil }
        var request = URLRequest(url: address, timeoutInterval: Self.timeout)
        request.httpMethod = "POST"
        request.httpBody = Data(form.utf8)
        request.setValue("application/x-www-form-urlencoded; charset=utf-8", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("Edgepad", forHTTPHeaderField: "User-Agent")
        return await http.send(request, maxBytes: Self.maxBytes, what: what)
    }
}
