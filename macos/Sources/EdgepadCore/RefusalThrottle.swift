import Foundation

/// Decides which refused connections earn a log line: the first from each host in a window, so a flood of connections
/// writes a line per host per minute instead of one per connection. Remembers at most `limit` hosts; past that a new
/// host is not logged until an entry expires, so a flood from many addresses cannot grow the table either.
public struct RefusalThrottle {
    private let window: TimeInterval
    private let limit: Int
    /// When each host was last logged.
    private var logged: [String: TimeInterval] = [:]

    public init(window: TimeInterval = 60, limit: Int = 64) {
        self.window = window
        self.limit = limit
    }

    /// Hosts remembered now; never more than `limit`.
    var count: Int { logged.count }

    /// True when a refusal from `host` at `now` (seconds, any monotonic clock) should be logged.
    public mutating func shouldLog(host: String, now: TimeInterval) -> Bool {
        logged = logged.filter { now < $0.value + window }
        guard logged[host] == nil, logged.count < limit else { return false }
        logged[host] = now
        return true
    }
}
