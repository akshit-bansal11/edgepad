import Foundation

/// How a pairing attempt ended, for `PairingLimiter`.
public enum PairingOutcome {
    case paired
    /// Counts toward the lock.
    case failed
    /// The owner cancelled, the connection ended or the listener stopped: does not count.
    case abandoned
}

/// Keeps network pairing to one at a time, and stops anyone on the network from wearing the owner down with requests,
/// the twin of the C# `PairingLimiter`. Five pairings in a row that fail (a wrong code typed, a commitment that does
/// not open, a phone that does not confirm) lock pairing for 5 minutes, doubling with every further five, never more
/// than an hour. The owner cancelling does not count: that is the owner deciding, not anyone guessing. A pairing that
/// succeeds clears the count. Counted in memory only, like `PinStore`'s wrong answers: quitting the app needs the
/// desktop. Main thread only.
public final class PairingLimiter {
    private static let failuresPerLock = 5
    private static let firstLockSeconds: TimeInterval = 300
    private static let maxLockSeconds: TimeInterval = 3600

    /// Uptime, not the wall clock, so a clock stepped backwards cannot stretch or shorten a lock. A test hands in its
    /// own.
    private let now: () -> TimeInterval
    private var busy = false
    private var failures = 0
    private var locks = 0
    private var lockedUntil: TimeInterval = 0

    public init(now: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime }) {
        self.now = now
    }

    /// Takes the one pairing slot; false while another pairing holds it or pairing is locked. Every true is followed
    /// by exactly one `end`.
    func tryBegin() -> Bool {
        guard !busy, now() >= lockedUntil else { return false }
        busy = true
        return true
    }

    /// Frees the slot, counting the attempt as `outcome` says.
    func end(_ outcome: PairingOutcome) {
        busy = false
        switch outcome {
        case .paired:
            failures = 0
            locks = 0
        case .failed:
            failures += 1
            if failures >= Self.failuresPerLock {
                failures = 0
                locks += 1
                // The shift is capped: 300 s doubled four times is already past the hour, and a large one overflows.
                let doubled = Self.firstLockSeconds * Double(1 << min(locks - 1, 4))
                lockedUntil = now() + min(doubled, Self.maxLockSeconds)
            }
        case .abandoned:
            break
        }
    }
}
