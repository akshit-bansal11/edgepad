import Foundation

/// Remembers whether a capture overlay (the region crosshair, the capture toolbar) may still be open on this
/// Mac, so the next capture action, or the phone leaving its capture screen, can close it first. Pure: it
/// decides and the dispatcher presses, so a test can read every decision without sending a key.
///
/// A guess and not a fact: macOS tells us nothing when the person finishes a drag. So the memory expires, and
/// the ceiling is the safety limit, since an Escape sent with no overlay open lands in the focused app. It is
/// short on purpose: long enough to aim a drag and cancel it from the phone, short enough that finishing a drag
/// and then tapping Full screen is usually past it. Inside the window that case sends one stray Escape.
public struct CaptureOverlay {
    /// How long after a capture action that opens an overlay we still believe it is open.
    public static let pendingSeconds: TimeInterval = 8

    /// The pause between the dismissing Escape and the next shortcut: about three frames at 60 Hz, for the
    /// overlay window to go. Unverified on a real Mac, which is why it is one constant.
    public static let dismissWait: TimeInterval = 0.05

    private let clock: () -> TimeInterval
    private var openedAt: TimeInterval?

    public init(clock: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime }) {
        self.clock = clock
    }

    /// Whether an overlay may be open right now.
    public var isPending: Bool {
        guard let openedAt else { return false }
        return clock() - openedAt < Self.pendingSeconds
    }

    /// A capture action is about to be pressed. True when an Escape must be pressed first.
    public mutating func begin(opensOverlay: Bool) -> Bool {
        let dismiss = isPending
        openedAt = opensOverlay ? clock() : nil
        return dismiss
    }

    /// The phone left its capture screen. True when an Escape must be pressed.
    public mutating func cancel() -> Bool {
        let dismiss = isPending
        openedAt = nil
        return dismiss
    }
}
