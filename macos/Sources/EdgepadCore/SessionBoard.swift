import Foundation

/// What happened to an agent session, in the board's own words.
public enum SessionEvent: Int, CaseIterable {
    case start
    case prompt
    case needsYou
    case stop
    case end
}

/// One lifecycle event of one session. The id and the folder stay on the Mac; only the folder's last name is shown.
public struct HookEvent: Equatable {
    public let agent: String
    public let sessionId: String
    public let event: SessionEvent
    public let cwd: String

    public init(agent: String, sessionId: String, event: SessionEvent, cwd: String) {
        self.agent = agent
        self.sessionId = sessionId
        self.event = event
        self.cwd = cwd
    }

    /// Claude Code hook name (and Notification type) to an event; nil for the ones the board does not track.
    public static func fromClaude(_ hookEventName: String?, _ notificationType: String?, sessionId: String, cwd: String)
        -> HookEvent?
    {
        guard let event = map(hookEventName, notificationType) else { return nil }
        return HookEvent(agent: AgentToken.claude.rawValue, sessionId: sessionId, event: event, cwd: cwd)
    }

    /// Codex hooks carry approvals and SessionStart only; the rollout watcher supplies the rest.
    public static func fromCodex(_ hookEventName: String?, sessionId: String, cwd: String) -> HookEvent? {
        guard hookEventName == "SessionStart" else { return nil }
        return HookEvent(agent: AgentToken.codex.rawValue, sessionId: sessionId, event: .start, cwd: cwd)
    }

    /// Gemini CLI hooks: SessionStart idle, BeforeAgent working, a ToolPermission Notification waiting, AfterAgent
    /// idle, SessionEnd gone. The twin of the core's HookEvent.FromGemini.
    public static func fromGemini(_ hookEventName: String?, _ notificationType: String?, sessionId: String, cwd: String)
        -> HookEvent?
    {
        let event: SessionEvent?
        switch hookEventName {
        case "SessionStart": event = .start
        case "BeforeAgent": event = .prompt
        case "Notification": event = notificationType == "ToolPermission" ? .needsYou : nil
        case "AfterAgent": event = .stop
        case "SessionEnd": event = .end
        default: event = nil
        }
        guard let event else { return nil }
        return HookEvent(agent: AgentToken.gemini.rawValue, sessionId: sessionId, event: event, cwd: cwd)
    }

    /// OpenCode plugin events as the shipped plugin names them: session.created idle, session.busy working,
    /// permission.asked waiting, session.idle idle, session.deleted gone. The twin of the core's
    /// HookEvent.FromOpenCode.
    public static func fromOpenCode(_ hookEventName: String?, sessionId: String, cwd: String) -> HookEvent? {
        let event: SessionEvent?
        switch hookEventName {
        case "session.created": event = .start
        case "session.busy": event = .prompt
        case "permission.asked": event = .needsYou
        case "session.idle": event = .stop
        case "session.deleted": event = .end
        default: event = nil
        }
        guard let event else { return nil }
        return HookEvent(agent: AgentToken.opencode.rawValue, sessionId: sessionId, event: event, cwd: cwd)
    }

    private static func map(_ name: String?, _ notificationType: String?) -> SessionEvent? {
        switch name {
        case "SessionStart": return .start
        case "UserPromptSubmit": return .prompt
        case "Notification":
            let waits = ["permission_prompt", "idle_prompt", "agent_needs_input"]
            return waits.contains(notificationType ?? "") ? .needsYou : nil
        case "Stop": return .stop
        case "SessionEnd": return .end
        default: return nil
        }
    }
}

/// A session found on disk before any hook spoke of it: its id, state token, folder and when its file was last written.
public struct SeedSession: Equatable {
    public let agent: String
    public let sessionId: String
    public let state: String
    public let cwd: String
    public let touched: Date

    public init(agent: String, sessionId: String, state: String, cwd: String, touched: Date) {
        self.agent = agent
        self.sessionId = sessionId
        self.state = state
        self.cwd = cwd
        self.touched = touched
    }
}

/// Which agent sessions exist and what each is doing, newest first, for TEXT SESSIONS. Start and Stop mean idle, Prompt
/// busy, NeedsYou waiting, End gone. A session with a permission ask pending reads as waiting until the ask is
/// answered. At most 8 are kept (the oldest idle one makes room) and one nobody touched for 6 hours is forgotten.
/// `changed` is called outside the lock and only when the list a phone would see differs. The twin of the laptop core's
/// SessionBoard.
public final class SessionBoard {
    public static let capacity = 8
    public static let maxIdLength = 200
    public static let expiry: TimeInterval = 6 * 60 * 60

    private struct Key: Hashable {
        let agent: String
        let id: String
    }

    private final class Session {
        var state = SessionStateToken.idle.rawValue
        var title = ""
        var waiting = false
        var touched = Date.distantPast
        var order: Int64 = 0
    }

    private let clock: () -> Date
    private let gate = NSLock()
    private var sessions: [Key: Session] = [:]
    private var order: Int64 = 0
    private var onChanged: (() -> Void)?

    public init(clock: @escaping () -> Date = Date.init) {
        self.clock = clock
    }

    /// Called outside the lock when the list a phone would see changed.
    public var changed: (() -> Void)? {
        get {
            gate.lock()
            defer { gate.unlock() }
            return onChanged
        }
        set {
            gate.lock()
            defer { gate.unlock() }
            onChanged = newValue
        }
    }

    /// `at` is when the thing was measured (a rollout file's modification time); the clock when nil.
    public func apply(_ hook: HookEvent, at: Date? = nil) {
        guard DashWire.isAgent(hook.agent), (1...Self.maxIdLength).contains(hook.sessionId.utf16.count) else { return }
        run {
            let key = Key(agent: hook.agent, id: hook.sessionId)
            if hook.event == .end {
                sessions[key] = nil
                return
            }
            let session: Session
            if let known = sessions[key] {
                session = known
            } else {
                makeRoom()
                session = Session()
                sessions[key] = session
            }
            switch hook.event {
            case .prompt: session.state = SessionStateToken.busy.rawValue
            case .needsYou: session.state = SessionStateToken.wait.rawValue
            default: session.state = SessionStateToken.idle.rawValue
            }
            if !hook.cwd.isEmpty {
                session.title = Self.titleOf(hook.cwd)
            }
            touch(session, at: at)
        }
    }

    /// Adds sessions found on disk (a transcript written before Edgepad ran or the phone linked). A session the board
    /// already knows is left alone (hook-fed state wins), one past `expiry` is ignored, and a full board takes no more:
    /// seeds never push out a live session. The next hook event for the same id replaces the seeded state via `apply`.
    public func seed(_ found: [SeedSession]) {
        run {
            let cutoff = clock().addingTimeInterval(-Self.expiry)
            var best: [Key: SeedSession] = [:]
            for item in found where item.touched > cutoff && DashWire.isAgent(item.agent) {
                guard (1...Self.maxIdLength).contains(item.sessionId.utf16.count) else { continue }
                let key = Key(agent: item.agent, id: item.sessionId)
                if sessions[key] != nil { continue }
                if let have = best[key], have.touched >= item.touched { continue }
                best[key] = item
            }
            let room = max(0, Self.capacity - sessions.count)
            for item in best.values.sorted(by: { $0.touched > $1.touched }).prefix(room).reversed() {
                let session = Session()
                session.state = item.state
                session.title = item.cwd.isEmpty ? "" : Self.titleOf(item.cwd)
                touch(session, at: item.touched)
                sessions[Key(agent: item.agent, id: item.sessionId)] = session
            }
        }
    }

    /// The permission broker's callback: a session with an ask pending reads as waiting. An unknown session is ignored.
    public func setWaiting(_ sessionId: String, _ waiting: Bool) {
        run {
            for (key, session) in sessions where key.id == sessionId {
                session.waiting = waiting
                touch(session)
            }
        }
    }

    /// Forgets what expired. `apply` and `snapshot` call it already; a timer may call it too.
    public func sweep() {
        run {}
    }

    /// Newest first.
    public func snapshot() -> [SessionEntry] {
        sweep()
        gate.lock()
        defer { gate.unlock() }
        return project()
    }

    static func titleOf(_ cwd: String) -> String {
        var scalars = Array(cwd.unicodeScalars)
        while let last = scalars.last, last == "/" || last == "\\" {
            scalars.removeLast()
        }
        let start = (scalars.lastIndex { $0 == "/" || $0 == "\\" }).map { $0 + 1 } ?? 0
        return DashWire.clean(String(String.UnicodeScalarView(scalars[start...])), 24)
    }

    private func run(_ change: () -> Void) {
        gate.lock()
        let before = project()
        let cutoff = clock().addingTimeInterval(-Self.expiry)
        for (key, session) in sessions where session.touched <= cutoff {
            sessions[key] = nil
        }
        change()
        let changed = before != project()
        let notify = onChanged
        gate.unlock()
        if changed {
            notify?()
        }
    }

    private func touch(_ session: Session, at: Date? = nil) {
        session.touched = at ?? clock()
        order += 1
        session.order = order
    }

    // Under the gate. Makes room for one more: the oldest idle session, else the oldest of all.
    private func makeRoom() {
        guard sessions.count >= Self.capacity else { return }
        let idle = SessionStateToken.idle.rawValue
        let victim = sessions.min { a, b in
            let rankA = a.value.state == idle && !a.value.waiting ? 0 : 1
            let rankB = b.value.state == idle && !b.value.waiting ? 0 : 1
            return rankA != rankB ? rankA < rankB : a.value.order < b.value.order
        }
        if let victim {
            sessions[victim.key] = nil
        }
    }

    private func project() -> [SessionEntry] {
        sessions.sorted { $0.value.order > $1.value.order }.map { key, session in
            SessionEntry(
                agent: key.agent, state: session.waiting ? SessionStateToken.wait.rawValue : session.state,
                title: session.title)
        }
    }
}
