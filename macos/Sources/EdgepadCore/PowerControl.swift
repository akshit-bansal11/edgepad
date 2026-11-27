import Foundation

/// A power action that waits out a countdown first.
public enum PowerKind {
    case restart
    case shutDown

    /// The first field of `TextKind.powerState`.
    var token: PowerStateToken {
        self == .restart ? .restart : .shutDown
    }
}

/// What this computer can do, from `PowerPlatform.caps`.
public struct PowerCaps: OptionSet {
    public let rawValue: Int

    public init(rawValue: Int) {
        self.rawValue = rawValue
    }

    public static let sleep = PowerCaps(rawValue: 1)
    public static let hibernate = PowerCaps(rawValue: 2)
    public static let restart = PowerCaps(rawValue: 4)
    public static let shutDown = PowerCaps(rawValue: 8)
}

/// The operating system's side of power. One object for the whole app, so a countdown already on screen outlives the
/// phone that started it.
public protocol PowerPlatform: AnyObject {
    var caps: PowerCaps { get }
    func sleep() -> Bool
    func hibernate() -> Bool
    /// Shows the countdown. `ended` is called exactly once: true at zero, false when cancelled.
    func startCountdown(_ kind: PowerKind, seconds: Int, ended: @escaping (Bool) -> Void) -> Bool
    func cancelCountdown()
    /// Restarts or shuts down now.
    func execute(_ kind: PowerKind) -> Bool
    /// A countdown already up, for a phone that reconnects.
    var pending: (kind: PowerKind, secondsLeft: Int)? { get }
}

/// Sleep, hibernate, restart and shut down on the phone's say. The twin of the shared core's `PowerControl`.
/// Restart and shut down wait out a 60 s countdown that the phone or the owner can cancel; `stateChanged` gets
/// "restart/60", "shutdown/60" or "" on every change, and nothing else. The countdown outlives the session: only the
/// callback is dropped when it ends.
public final class PowerControl {
    public static let countdownSeconds = 60

    /// One listener for the whole process, like Windows' `WindowsPower.Listen/Unlisten`.
    private final class Listener: @unchecked Sendable {
        private let lock = NSLock()
        private weak var control: PowerControl?

        func set(_ new: PowerControl) {
            lock.withLock { control = new }
        }

        func clear(_ old: PowerControl) {
            lock.withLock {
                if control === old { control = nil }
            }
        }

        func get() -> PowerControl? {
            lock.withLock { control }
        }
    }

    private static let current = Listener()

    private let platform: PowerPlatform
    private let gate = NSLock()
    private var reported: String
    private var callback: ((String) -> Void)?

    public init(platform: PowerPlatform) {
        self.platform = platform
        reported = PowerControl.state(platform.pending)
    }

    public var caps: PowerCaps { platform.caps }

    /// Where a state change goes. Set by the session once it has a link to write to; nil when it ends.
    public var stateChanged: ((String) -> Void)? {
        get { gate.withLock { callback } }
        set {
            gate.withLock { callback = newValue }
            // The newest session is the one a countdown's end is told to; a session going away only lets go of its own.
            if newValue != nil {
                Self.current.set(self)
            } else {
                Self.current.clear(self)
            }
        }
    }

    /// The restart seconds changed under a running countdown (the Mac woke): tells the current session, bypassing the
    /// "unchanged" filter because the same text now means a fresh 60 s.
    public static func countdownRestarted() {
        current.get()?.report(force: true)
    }

    /// False, so the frame is dropped and counted, when this computer lacks the capability, or for a sleep while a
    /// countdown is pending (its clock would fire the moment the Mac woke). A restart or shut down while one is
    /// counting down is accepted and changes nothing.
    public func run(_ action: ActionId) -> Bool {
        switch action {
        case .sleep:
            return platform.pending == nil && platform.caps.contains(.sleep) && platform.sleep()
        case .hibernate:
            return platform.pending == nil && platform.caps.contains(.hibernate) && platform.hibernate()
        case .restart:
            return countdown(.restart, needs: .restart)
        case .shutDown:
            return countdown(.shutDown, needs: .shutDown)
        case .powerCancel:
            platform.cancelCountdown()
            // The platform reports a cancel to whoever started the countdown, who may be a phone that has gone.
            report("")
            return true
        default:
            return false
        }
    }

    /// "restart/52", "shutdown/7", or "" for none: what a reconnecting phone is sent.
    public func currentState() -> String {
        PowerControl.state(platform.pending)
    }

    /// The `TextKind.features` tokens for these capabilities, in a fixed order.
    public static func features(_ caps: PowerCaps) -> [String] {
        var tokens: [String] = []
        if caps.contains(.sleep) { tokens.append(Feature.sleep.rawValue) }
        if caps.contains(.hibernate) { tokens.append(Feature.hibernate.rawValue) }
        if caps.contains(.restart) { tokens.append(Feature.restart.rawValue) }
        if caps.contains(.shutDown) { tokens.append(Feature.shutDown.rawValue) }
        return tokens
    }

    private func countdown(_ kind: PowerKind, needs cap: PowerCaps) -> Bool {
        guard platform.caps.contains(cap) else { return false }
        if platform.pending != nil { return true }
        let seconds = PowerControl.countdownSeconds
        // The platform and the kind are captured, never self: the countdown must still finish if this session is gone.
        // The end goes to whichever session is listening now, which may be a phone that reconnected since.
        let started = platform.startCountdown(kind, seconds: seconds) { [platform] fired in
            Self.current.get()?.report("")
            if fired { _ = platform.execute(kind) }
        }
        guard started else { return false }
        report("\(kind.token.rawValue)/\(seconds)")
        return true
    }

    private func report(_ state: String) {
        report(state, force: false)
    }

    /// With `force`, sends the platform's current state even when it equals the last one sent.
    private func report(_ state: String? = nil, force: Bool) {
        let text = state ?? Self.state(platform.pending)
        let send: ((String) -> Void)? = gate.withLock {
            guard force || reported != text else { return nil }
            reported = text
            return callback
        }
        send?(text)
    }

    private static func state(_ pending: (kind: PowerKind, secondsLeft: Int)?) -> String {
        guard let pending else { return "" }
        return "\(pending.kind.token.rawValue)/\(pending.secondsLeft)"
    }
}
