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
        set { gate.withLock { callback = newValue } }
    }

    /// False, so the frame is dropped and counted, when this computer lacks the capability. A restart or shut down
    /// while one is counting down is accepted and changes nothing.
    public func run(_ action: ActionId) -> Bool {
        switch action {
        case .sleep:
            return platform.caps.contains(.sleep) && platform.sleep()
        case .hibernate:
            return platform.caps.contains(.hibernate) && platform.hibernate()
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
        let started = platform.startCountdown(kind, seconds: seconds) { [platform, weak self] fired in
            self?.report("")
            if fired { _ = platform.execute(kind) }
        }
        guard started else { return false }
        report("\(kind.token.rawValue)/\(seconds)")
        return true
    }

    private func report(_ state: String) {
        let send: ((String) -> Void)? = gate.withLock {
            guard reported != state else { return nil }
            reported = state
            return callback
        }
        send?(state)
    }

    private static func state(_ pending: (kind: PowerKind, secondsLeft: Int)?) -> String {
        guard let pending else { return "" }
        return "\(pending.kind.token.rawValue)/\(pending.secondsLeft)"
    }
}
