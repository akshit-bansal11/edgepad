import Foundation

/// Set once, never cleared: a session's end, read by the owner's trust question so that a hold or a hang-up
/// withdraws it. Locked because the question reads it from a timer while the session sets it from wherever it ends.
public final class Cancellation {
    private let gate = NSLock()
    private var cancelled = false

    public init() {}

    public var isCancelled: Bool {
        gate.lock()
        defer { gate.unlock() }
        return cancelled
    }

    public func cancel() {
        gate.lock()
        defer { gate.unlock() }
        cancelled = true
    }

    /// Throws `CancellationError` once cancelled, so a caller can tell an ended session from an answer of no.
    public func check() throws {
        if isCancelled {
            throw CancellationError()
        }
    }
}
