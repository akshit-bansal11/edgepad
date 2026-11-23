/// Why this Mac should not serve a phone right now. A hold begins with the first reason and ends with the last, so a
/// lock that lands while the user is switched out is still one hold and one resume. Pure: no notification, no timer.
public struct SessionHold {
    public enum Reason: Hashable, CaseIterable {
        /// Another user's session is in front (fast user switching).
        case switchedOut
        /// The screen is locked.
        case locked
    }

    private var reasons: Set<Reason> = []

    public init() {}

    /// True when this is the first reason, so the caller holds off. Repeating a reason changes nothing.
    public mutating func begin(_ reason: Reason) -> Bool {
        let wasEmpty = reasons.isEmpty
        return reasons.insert(reason).inserted && wasEmpty
    }

    /// True when this removed the last reason, so the caller resumes. Ending a reason that was not held changes nothing.
    public mutating func end(_ reason: Reason) -> Bool {
        reasons.remove(reason) != nil && reasons.isEmpty
    }

    /// Every reason dropped at once; true when a hold was open, so the caller resumes.
    public mutating func clear() -> Bool {
        let wasHeld = !reasons.isEmpty
        reasons.removeAll()
        return wasHeld
    }
}
