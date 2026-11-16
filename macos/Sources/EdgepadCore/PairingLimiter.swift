import Foundation

/// How a pairing attempt ended, for `PairingLimiter`.
public enum PairingOutcome {
    case paired
    /// Counts toward the lock: a wrong code, a commitment that does not open, a bad confirmation or an invalid key.
    case failed
    /// The connection ended before the question was shown, the trusted phones were forgotten, or this Mac stopped
    /// it: does not count.
    case abandoned
    /// The owner pressed Cancel: refuses that host for a while and spends one of the hour's prompts.
    case cancelled
    /// The owner did not type the code in time, or the phone went silent or hung up under the question: as
    /// `cancelled`.
    case timedOut
}

/// Keeps network pairing to one at a time, and stops anyone on the network from wearing the owner down with requests,
/// the twin of the C# `PairingLimiter`. Five pairings in a row that fail (a wrong code typed, a commitment that does
/// not open, a confirmation that does not match) lock pairing for 5 minutes, doubling with every further five, never
/// more than an hour. A prompt the owner cancels or nobody answers in time is not a guess and does not count toward
/// that lock, but it must not be free either, or a host could raise the prompt forever: the host that raised it is
/// refused for a minute, doubling with each further one in a row, never more than an hour, and ten of them in any
/// hour refuse everyone until the oldest is an hour old. A pairing that succeeds clears the count and that host's
/// streak. Counted in memory only, like `PinStore`'s wrong answers: quitting the app needs the desktop. Main thread
/// only.
public final class PairingLimiter {
    private static let failuresPerLock = 5
    private static let firstLockSeconds: TimeInterval = 300
    private static let maxLockSeconds: TimeInterval = 3600
    private static let hostLockSeconds: TimeInterval = 60
    private static let maxHostLockSeconds: TimeInterval = 3600
    private static let maxPromptsPerHour = 10
    private static let maxTrackedHosts = 64
    private static let hour: TimeInterval = 3600

    private struct HostStreak {
        var count: Int
        var refusedUntil: TimeInterval
    }

    /// Uptime, not the wall clock, so a clock stepped backwards cannot stretch or shorten a lock. A test hands in its
    /// own.
    private let now: () -> TimeInterval
    private var busy = false
    private var failures = 0
    private var locks = 0
    private var lockedUntil: TimeInterval = 0
    /// When each cancelled or unanswered prompt of the last hour ended, oldest first.
    private var prompts: [TimeInterval] = []
    /// Hosts with a streak of cancelled or unanswered prompts. A host is forgotten an hour after its refusal ends.
    private var hosts: [String: HostStreak] = [:]

    public init(now: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime }) {
        self.now = now
    }

    /// How many hosts the limiter is remembering, for tests.
    var trackedHosts: Int { hosts.count }

    /// Takes the one pairing slot; false while another pairing holds it, pairing is locked, the hour's prompts are
    /// spent, or `host` is refused. Every true is followed by exactly one `end`. A nil host skips the per-host rule.
    func tryBegin(host: String? = nil) -> Bool {
        let t = now()
        expire(t)
        guard !busy, t >= lockedUntil, prompts.count < Self.maxPromptsPerHour else { return false }
        if let host, let streak = hosts[host], t < streak.refusedUntil { return false }
        busy = true
        return true
    }

    /// Frees the slot, counting the attempt from `host` as `outcome` says.
    func end(_ outcome: PairingOutcome, host: String? = nil) {
        busy = false
        let t = now()
        expire(t)
        switch outcome {
        case .paired:
            failures = 0
            locks = 0
            if let host { hosts[host] = nil }
        case .failed:
            failures += 1
            if failures >= Self.failuresPerLock {
                failures = 0
                locks += 1
                // The shift is capped: 300 s doubled four times is already past the hour, and a large one overflows.
                let doubled = Self.firstLockSeconds * Double(1 << min(locks - 1, 4))
                lockedUntil = t + min(doubled, Self.maxLockSeconds)
            }
        case .cancelled, .timedOut:
            prompts.append(t)
            if prompts.count > Self.maxPromptsPerHour {
                prompts.removeFirst(prompts.count - Self.maxPromptsPerHour)
            }
            if let host { refuse(host, t) }
        case .abandoned:
            break
        }
    }

    private func refuse(_ host: String, _ t: TimeInterval) {
        if hosts[host] == nil, hosts.count >= Self.maxTrackedHosts,
            let oldest = hosts.min(by: { $0.value.refusedUntil < $1.value.refusedUntil })?.key
        {
            hosts[oldest] = nil
        }
        let count = (hosts[host]?.count ?? 0) + 1
        // Capped before shifting, as above: 60 s doubled six times is already past the hour.
        let doubled = Self.hostLockSeconds * Double(1 << min(count - 1, 6))
        hosts[host] = HostStreak(count: count, refusedUntil: t + min(doubled, Self.maxHostLockSeconds))
    }

    private func expire(_ t: TimeInterval) {
        prompts.removeAll { t >= $0 + Self.hour }
        hosts = hosts.filter { t < $0.value.refusedUntil + Self.maxHostLockSeconds }
    }
}
