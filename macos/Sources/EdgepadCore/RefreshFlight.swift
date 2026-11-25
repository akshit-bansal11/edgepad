import Foundation

/// One token refresh at a time per secret store. A refresh returns a new refresh token and kills the old one, so two
/// that overlap would leave one of them holding a dead token: a second caller joins the refresh already running and
/// gets its answer instead. GitHubTokenRefresh and GitLabRenewal each keep one, so a GitHub refresh never waits on a
/// GitLab one. The twin of the laptop core's RefreshFlight.
final class RefreshFlight {
    /// A token this close to its end is refreshed before it is used.
    static let margin: TimeInterval = 5 * 60

    private let gate = NSLock()
    private var running: [ObjectIdentifier: Task<RefreshOutcome, Never>] = [:]

    /// Whether `expires`, a saved expiry in Unix seconds written as digits, is within `margin` of its end at `now`, or
    /// past it. False for anything else.
    static func isDue(_ expires: String?, now: Date) -> Bool {
        guard let text = expires, !text.isEmpty, text.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }),
            let seconds = Int64(text)
        else { return false }
        return now.addingTimeInterval(margin) >= Date(timeIntervalSince1970: TimeInterval(seconds))
    }

    /// Runs `exchange` in a task of its own for `store`, or joins the one already under way.
    func join(_ store: SecretStore, _ exchange: @escaping () async -> RefreshOutcome) async -> RefreshOutcome {
        let key = ObjectIdentifier(store)
        let task = gate.withLock { () -> Task<RefreshOutcome, Never> in
            if let joined = running[key] { return joined }
            let made = Task { await exchange() }
            running[key] = made
            return made
        }
        let outcome = await task.value
        gate.withLock {
            if running[key] == task { running[key] = nil }
        }
        return outcome
    }
}
