import Foundation

/// What the dashboard limits for the whole process, not for one phone link: a link's `IntegrationHost` is made again
/// whenever a switch moves (the phone can flip developer mode at will), so a count or a timestamp kept in the host would
/// reset with it. The app holds one of these for its lifetime and hands it to every `DashboardParts` it makes. The twin
/// of the laptop core's DashboardThrottle.
public final class DashboardThrottle {
    public static let maxOpens = 3
    public static let openWindow: TimeInterval = 10

    private let gate = NSLock()
    private var opens: [Date] = []
    private var times: [String: Date] = [:]

    public init() {}

    /// True, and counts it, when fewer than `maxOpens` links were opened in the last `openWindow` seconds, by any phone.
    public func allowOpen(_ now: Date) -> Bool {
        gate.withLock {
            opens.removeAll { now.timeIntervalSince($0) >= Self.openWindow }
            guard opens.count < Self.maxOpens else { return false }
            opens.append(now)
            return true
        }
    }

    /// When the git host `host` was last asked for a refresh; nil when never (or after `forgetRefresh`).
    public func lastRefresh(_ host: String) -> Date? {
        gate.withLock { times[host] }
    }

    public func refreshed(_ host: String, at: Date) {
        gate.withLock { times[host] = at }
    }

    /// Lets the next refresh of `host` run at once (a sign-in just finished).
    public func forgetRefresh(_ host: String) {
        gate.withLock { times[host] = nil }
    }
}
