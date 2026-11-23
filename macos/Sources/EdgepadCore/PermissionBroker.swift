import Foundation

/// What became of a permission request. `deferred` means the agent shows its own prompt, as if Edgepad were not there.
public enum PermDecision: Equatable {
    case deferred
    case allow
    case deny
}

/// An approval on its way to the phones: BLOB PERM_ASK with label `id/complete` (`DashWire.permLabel`) and `body` as
/// UTF-8. When `complete` is false the body has no text and the phone offers Deny only.
public struct PermPrompt: Equatable {
    public let id: String
    public let complete: Bool
    public let body: String
}

/// Holds the approvals that wait for a phone. Each gets a fresh random 12-hex id; the first valid answer wins and every
/// later one, a replay or a guessed id is refused. No phone, 8 asks already waiting, or 20 asks in the last minute:
/// deferred at once, so the agent's own prompt shows and nothing piles up. An ask nobody answers in 30 s, or whose hook
/// went away first, is deferred. Every ask that ends, answered or not, is announced `gone`, so every phone drops its
/// sheet. The text shown is built here from a whitelist of tool fields, never from anything else the hook sent. While
/// an ask waits its session reads as waiting on the board. The twin of the laptop core's PermissionBroker.
public final class PermissionBroker {
    public static let maxPending = 8
    public static let maxPerMinute = 20
    public static let answerWait: TimeInterval = 30

    private struct Pending {
        let session: String
        let done: (PermDecision) -> Void
    }

    private let board: SessionBoard
    private let phoneLinked: () -> Bool
    private let clock: () -> Date
    private let wait: TimeInterval
    private let after: (TimeInterval, @escaping () -> Void) -> Void
    private let gate = NSLock()
    private var pending: [String: Pending] = [:]
    private var recent: [Date] = []
    private var onAsked: ((PermPrompt) -> Void)?
    private var onGone: ((String) -> Void)?

    /// - Parameters:
    ///   - after: Runs the action once, after the delay, on another queue. Tests hold it and fire it by hand.
    public init(
        board: SessionBoard,
        phoneLinked: @escaping () -> Bool,
        clock: @escaping () -> Date = Date.init,
        wait: TimeInterval = PermissionBroker.answerWait,
        after: @escaping (TimeInterval, @escaping () -> Void) -> Void = { delay, action in
            DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + delay, execute: action)
        }
    ) {
        self.board = board
        self.phoneLinked = phoneLinked
        self.clock = clock
        self.wait = wait
        self.after = after
    }

    /// A new ask for every linked phone. Called outside the lock, on the hook's thread.
    public var asked: ((PermPrompt) -> Void)? {
        get { locked { onAsked } }
        set { locked { onAsked = newValue } }
    }

    /// The ask with this id is over (answered, timed out, or its hook left): every phone dismisses it.
    public var gone: ((String) -> Void)? {
        get { locked { onGone } }
        set { locked { onGone = newValue } }
    }

    /// How many asks wait right now.
    public var pendingCount: Int {
        locked { pending.count }
    }

    /// The text an approval shows for this tool: Bash's command, the file of an edit or read, WebFetch's URL; for Codex,
    /// whose tool names are not documented, the first of command, file and URL it sent. Nil when there is nothing to
    /// show (for Claude Code any other tool: MCP tools, Task, ExitPlanMode, WebSearch, an unknown one; or the field
    /// missing or empty): a person must never approve what they cannot read, so the agent asks itself.
    public static func textFor(_ request: PermRequest) -> String? {
        let input = request.input
        let text: String?
        switch request.tool {
        case "Bash": text = input.command
        case "Edit", "Write", "MultiEdit", "Read", "NotebookEdit": text = input.filePath
        case "WebFetch": text = input.url
        default:
            text = request.agent == AgentToken.codex.rawValue ? input.command ?? input.filePath ?? input.url : nil
        }
        guard let text, !text.isEmpty else { return nil }
        return text
    }

    /// Asks the phones; `done` is told the outcome exactly once, at once when the ask is deferred before it starts, else
    /// when a phone answers or 30 s pass. The closure returned is the hook going away (answered in the terminal, hook
    /// cancelled): it ends a waiting ask as deferred and announces it gone; afterwards it does nothing.
    @discardableResult
    public func request(_ request: PermRequest, done: @escaping (PermDecision) -> Void) -> () -> Void {
        guard phoneLinked(), let text = Self.textFor(request) else {
            done(.deferred)
            return {}
        }

        // Complete only when nothing was cut on the way and the whole body fits; else the text stays off the phone
        // and it offers Deny only.
        var (body, complete) = PermAskBody.encode(
            agent: request.agent, tool: request.tool, text: text, truncated: request.input.truncated)
        if complete && !request.input.commandSafe && request.input.command == text {
            body = PermAskBody.encode(agent: request.agent, tool: request.tool, text: "").body
            complete = false
        }

        gate.lock()
        let now = clock()
        let cutoff = now.addingTimeInterval(-60)
        recent.removeAll { $0 <= cutoff }
        guard pending.count < Self.maxPending, recent.count < Self.maxPerMinute else {
            gate.unlock()
            done(.deferred)
            return {}
        }
        recent.append(now)
        var id = Self.newId()
        while pending[id] != nil {
            id = Self.newId()
        }
        pending[id] = Pending(session: request.session, done: done)
        let ask = onAsked
        gate.unlock()

        board.setWaiting(request.session, true)
        ask?(PermPrompt(id: id, complete: complete, body: body))
        after(wait) { [weak self] in self?.finish(id)?(.deferred) }
        return { [weak self] in self?.finish(id)?(.deferred) }
    }

    /// A phone's PERM_ANSWER. True when the id waits and this is its first answer; false for an unknown, used or guessed
    /// id.
    @discardableResult
    public func answer(id: String, allow: Bool) -> Bool {
        guard let done = finish(id) else { return false }
        done(allow ? .allow : .deny)
        return true
    }

    /// The `TextKind.permAnswer` wire, for the Dispatcher: `id/a` or `id/d`. False when it is not one or the id does
    /// not wait.
    @discardableResult
    public func answer(_ wire: String) -> Bool {
        guard let parsed = PermAnswer.parse(wire) else { return false }
        return answer(id: parsed.id, allow: parsed.allow)
    }

    /// A phone went away. When none is left, every waiting ask is deferred so the agent shows its own prompt.
    public func phonesChanged() {
        guard !phoneLinked() else { return }
        let ids = locked { Array(pending.keys) }
        for id in ids {
            finish(id)?(.deferred)
        }
    }

    /// 6 random bytes as 12 lowercase hex digits, from the system's cryptographic generator.
    static func newId() -> String {
        var generator = SystemRandomNumberGenerator()
        return (0..<6).map { _ in
            let byte = UInt8.random(in: 0...255, using: &generator)
            return (byte < 16 ? "0" : "") + String(byte, radix: 16)
        }.joined()
    }

    // Removes the ask once, clears its session's waiting mark when no other ask of that session waits, and announces it
    // gone. Nil when it was not waiting.
    private func finish(_ id: String) -> ((PermDecision) -> Void)? {
        gate.lock()
        guard let ask = pending.removeValue(forKey: id) else {
            gate.unlock()
            return nil
        }
        let stillWaits = pending.values.contains { $0.session == ask.session }
        let announce = onGone
        gate.unlock()

        if !stillWaits {
            board.setWaiting(ask.session, false)
        }
        announce?(id)
        return ask.done
    }

    private func locked<T>(_ body: () -> T) -> T {
        gate.lock()
        defer { gate.unlock() }
        return body()
    }
}
