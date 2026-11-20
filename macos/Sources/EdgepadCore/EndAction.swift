import Foundation

/// What an armed `EndAction` does when the media ends.
public enum EndKind {
    case sleep
    case shutdown
}

/// One look at what is playing. `identity` is empty when nothing is playing or paused; a duration of 0 or less means
/// the player has no timeline.
public struct MediaSnapshot: Equatable {
    public let identity: String
    public let playing: Bool
    public let positionSeconds: Int
    public let durationSeconds: Int

    public init(identity: String, playing: Bool, positionSeconds: Int, durationSeconds: Int) {
        self.identity = identity
        self.playing = playing
        self.positionSeconds = positionSeconds
        self.durationSeconds = durationSeconds
    }
}

/// Sleep or shut this Mac down once the current media has ended. The twin of the shared core's `EndAction`: the phone
/// arms it (END_SLEEP, END_SHUTDOWN) and disarms it (END_CANCEL); the session calls `observe` from the media report
/// it already makes, so this holds no timer. It fires `perform` once and clears when armed and playback stopped in the
/// last `endWindowSeconds` of the timeline, or the track changed or vanished after them. With no timeline it refuses
/// to arm, because "the end" could not be told. `stateChanged` gets one `EndActionToken` raw value on every change.
public final class EndAction {
    public static let endWindowSeconds = 3

    private let perform: (EndKind) -> Bool
    private let stateChanged: (String) -> Void
    private let gate = NSLock()
    private var armed: EndKind?
    private var last = MediaSnapshot(identity: "", playing: false, positionSeconds: 0, durationSeconds: 0)

    public init(perform: @escaping (EndKind) -> Bool, stateChanged: @escaping (String) -> Void) {
        self.perform = perform
        self.stateChanged = stateChanged
    }

    /// False, and nothing armed, when the last observed media has no timeline. Arming again replaces the kind.
    public func arm(_ kind: EndKind) -> Bool {
        gate.lock()
        guard last.durationSeconds > 0 else {
            gate.unlock()
            return false
        }
        let token: String? = armed == kind ? nil : EndAction.token(kind)
        armed = kind
        gate.unlock()
        if let token { stateChanged(token) }
        return true
    }

    public func cancel() {
        gate.lock()
        let token: String? = armed == nil ? nil : EndActionToken.none.rawValue
        armed = nil
        gate.unlock()
        if let token { stateChanged(token) }
    }

    public func observe(_ now: MediaSnapshot) {
        gate.lock()
        var fire: EndKind?
        if let kind = armed, EndAction.ended(last, now) {
            fire = kind
            armed = nil
        }
        last = now
        gate.unlock()

        if let due = fire {
            stateChanged(EndActionToken.none.rawValue)
            _ = perform(due)
        }
    }

    private static func ended(_ before: MediaSnapshot, _ now: MediaSnapshot) -> Bool {
        let wasNearEnd =
            before.durationSeconds > 0 && before.positionSeconds >= before.durationSeconds - endWindowSeconds
        let nearEnd = now.durationSeconds > 0 && now.positionSeconds >= now.durationSeconds - endWindowSeconds

        // (a) stopped at the end; (b) another track after the end; (c) the session gone after the end.
        return (!now.playing && nearEnd && !now.identity.isEmpty) || (wasNearEnd && now.identity != before.identity)
    }

    private static func token(_ kind: EndKind) -> String {
        kind == .sleep ? EndActionToken.sleep.rawValue : EndActionToken.shutdown.rawValue
    }
}
