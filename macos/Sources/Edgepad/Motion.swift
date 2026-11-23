import AppKit

/// The three durations everything on the Mac that moves uses, in seconds (UX-27, DES-D07): a press, a window or
/// panel appearing, a whole screen changing. Nothing here animates a menu or an alert, which are the system's, or
/// the pointer and keys, which stay instant. `scene` has no consumer on the Mac, which has no screen-level change;
/// it stays so the three tiers are the same on every platform.
enum Motion {
    static let press: TimeInterval = 0.160
    static let reveal: TimeInterval = 0.300
    static let scene: TimeInterval = 0.400

    /// "Reduce motion" in System Settings › Accessibility › Display: a short cross-fade instead of any movement.
    static var reduce: Bool { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }
}
