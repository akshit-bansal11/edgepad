import Foundation

/// The tray app's end of the local hook channel. Per connection: one request line of at most 64 KiB, which must arrive
/// within 2 s; the token compared in constant time; then one reply and close. Anything malformed, from an unknown agent
/// or kind, or with a wrong token is closed with no reply. At most 8 connections at once; the 9th is closed unread. A
/// request for an agent whose switch is off is answered (perm: defer) and otherwise ignored. Each accepted request
/// stamps the agent's last-heard time; the first accepted Codex request records that the person trusted the plugin's
/// hooks. Only the agent, the kind and the outcome are ever logged, never a command, path or token. The twin of the
/// laptop core's HookServer.
public final class HookServer {
    public static let maxConnections = 8
    public static let readDeadline: TimeInterval = 2

    private let listener: HookListening
    private let token: () -> String
    private let broker: PermissionBroker
    private let sessions: SessionBoard
    private let limits: LimitsBoard
    private let spend: SpendBoard
    private let usage: UsageStore
    private let settings: IntegrationSettings
    private let clock: () -> Date
    private let readWait: TimeInterval
    private let workers = DispatchQueue(label: "edgepad.hooks", qos: .userInitiated, attributes: .concurrent)
    private let gate = NSLock()
    private var active = 0
    private var started = false
    private var onAgentReady: ((String) -> Void)?

    /// - Parameters:
    ///   - token: The current token (`HookEndpoint.token`), read per request so a rotation applies at once.
    ///   - settings: Whose switches say which agent is on (keys `claude` and `codex`) and where codexTrusted is kept.
    public init(
        listener: HookListening,
        token: @escaping () -> String,
        broker: PermissionBroker,
        sessions: SessionBoard,
        limits: LimitsBoard,
        spend: SpendBoard,
        usage: UsageStore,
        settings: IntegrationSettings,
        clock: @escaping () -> Date = Date.init,
        readDeadline: TimeInterval = HookServer.readDeadline
    ) {
        self.listener = listener
        self.token = token
        self.broker = broker
        self.sessions = sessions
        self.limits = limits
        self.spend = spend
        self.usage = usage
        self.settings = settings
        self.clock = clock
        self.readWait = readDeadline
    }

    /// An agent now works with Edgepad: its first accepted request while it had no last-heard stamp, or for Codex the
    /// first after Connect (the person trusted the plugin's hooks). Called on a hook thread with the agent's token.
    public var agentReady: ((String) -> Void)? {
        get {
            gate.lock()
            defer { gate.unlock() }
            return onAgentReady
        }
        set {
            gate.lock()
            defer { gate.unlock() }
            onAgentReady = newValue
        }
    }

    /// Connections being served now.
    public var activeCount: Int {
        gate.lock()
        defer { gate.unlock() }
        return active
    }

    /// Starts the accept loop on a thread of its own. Idempotent.
    public func start() {
        gate.lock()
        let first = !started
        started = true
        gate.unlock()
        guard first else { return }
        let thread = Thread { [self] in acceptLoop() }
        thread.name = "edgepad.hooks.accept"
        thread.start()
    }

    /// Stops accepting and removes the socket. Connections being served finish on their own.
    public func dispose() {
        listener.close()
    }

    private func acceptLoop() {
        while let stream = listener.accept() {
            guard take() else {
                stream.close()
                continue
            }
            workers.async { [self] in
                serve(stream)
                stream.close()
                release()
            }
        }
    }

    private func take() -> Bool {
        gate.lock()
        defer { gate.unlock() }
        guard active < Self.maxConnections else { return false }
        active += 1
        return true
    }

    private func release() {
        gate.lock()
        defer { gate.unlock() }
        active -= 1
    }

    /// One connection, start to end (the caller closes it). Internal to the accept loop and to tests.
    func serve(_ stream: HookStream) {
        guard let line = readLine(stream), let request = HookProtocol.parse(line), tokenMatches(request.token) else {
            return
        }
        guard settings.flag(request.agent) else {
            if case .perm = request {
                _ = stream.write(HookProtocol.decisionReply(HookProtocol.later))
            } else {
                _ = stream.write(HookProtocol.okReply())
            }
            return
        }

        heard(request.agent)
        switch request {
        case .perm(let perm):
            servePerm(stream, perm)
            return
        case .session(let s):
            let hook: HookEvent?
            switch s.agent {
            case AgentToken.claude.rawValue:
                hook = HookEvent.fromClaude(s.event, s.notificationType, sessionId: s.session, cwd: s.cwd)
            case AgentToken.gemini.rawValue:
                hook = HookEvent.fromGemini(s.event, s.notificationType, sessionId: s.session, cwd: s.cwd)
            case AgentToken.opencode.rawValue:
                hook = HookEvent.fromOpenCode(s.event, sessionId: s.session, cwd: s.cwd)
            default:
                hook = HookEvent.fromCodex(s.event, sessionId: s.session, cwd: s.cwd)
            }
            if let hook {
                sessions.apply(hook)
            }
        case .usage(let u):
            // The context fill belongs to one session, not to the agent: it goes to that session's row, not the limits.
            limits.update(u.agent, u.windows, contextPct: nil, now: clock())
            if let fill = u.contextPct {
                sessions.setContext(agent: u.agent, cwd: u.cwd, pct: fill)
            }
        case .spend(let s):
            spend.update(s.agent, s.totals, now: clock())
        }
        // A client that already left makes the write fail; that is its choice, not an error.
        _ = stream.write(HookProtocol.okReply())
    }

    private func servePerm(_ stream: HookStream, _ perm: PermRequest) {
        let finished = DispatchSemaphore(value: 0)
        let outcome = Latch(PermDecision.deferred)
        let left = Latch(false)
        let cancel = broker.request(perm) { decision in
            outcome.set(decision)
            finished.signal()
        }
        // The hook leaving (answered in the terminal, cancelled) ends the ask: a read that returns, at the end of the
        // stream or with more bytes, which are not allowed either. Closing the stream below ends this read too.
        workers.async {
            _ = stream.read(limit: 1, until: nil)
            left.set(true)
            cancel()
        }
        finished.wait()
        let word: String
        switch outcome.value {
        case .allow: word = HookProtocol.allow
        case .deny: word = HookProtocol.deny
        case .deferred: word = HookProtocol.later
        }
        Log.write("Hook perm from \(perm.agent): \(word)")
        if !left.value {
            _ = stream.write(HookProtocol.decisionReply(word))
        }
        stream.close()
    }

    // One line of at most 64 KiB without its line feed, or nil: too long, closed first, or not complete in time.
    private func readLine(_ stream: HookStream) -> [UInt8]? {
        let deadline = Date().addingTimeInterval(readWait)
        let cap = HookProtocol.maxLineBytes + 1
        var buffer: [UInt8] = []
        while buffer.count < cap {
            guard let chunk = stream.read(limit: cap - buffer.count, until: deadline), !chunk.isEmpty else {
                return nil
            }
            let start = buffer.count
            buffer.append(contentsOf: chunk)
            if let end = buffer[start...].firstIndex(of: UInt8(ascii: "\n")) {
                return Array(buffer[..<end])
            }
        }
        return nil
    }

    private func tokenMatches(_ presented: String) -> Bool {
        Self.sameToken(presented, token())
    }

    /// Constant time over the bytes of equal-length tokens; a length mismatch or an empty token is false at once.
    static func sameToken(_ presented: String, _ expected: String) -> Bool {
        let given = Array(presented.utf8)
        let wanted = Array(expected.utf8)
        guard given.count == wanted.count, !wanted.isEmpty else { return false }
        var difference: UInt8 = 0
        for index in 0..<wanted.count {
            difference |= given[index] ^ wanted[index]
        }
        return difference == 0
    }

    private func heard(_ agent: String) {
        var ready = usage.touch(agent, now: clock())
        if agent == AgentToken.codex.rawValue && !settings.flag(IntegrationSettings.codexTrusted) {
            settings.setFlag(IntegrationSettings.codexTrusted, true)
            ready = true
        }
        if ready {
            agentReady?(agent)
        }
    }
}

/// A value set on one thread and read on another.
final class Latch<T> {
    private let gate = NSLock()
    private var stored: T

    init(_ value: T) {
        stored = value
    }

    var value: T {
        gate.lock()
        defer { gate.unlock() }
        return stored
    }

    func set(_ value: T) {
        gate.lock()
        defer { gate.unlock() }
        stored = value
    }
}
