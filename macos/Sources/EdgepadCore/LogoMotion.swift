import Foundation

/// One part of the logo: the arc (`isArc`, radius `outer`) or a tick from `inner` to `outer` at `angle` degrees.
public struct LogoStroke: Sendable {
    public let isArc: Bool
    public let angle: Double
    public let inner: Double
    public let outer: Double
    public let accent: Bool
}

/// How far a part has sprung out: its radii are multiplied by `scale`, and it is drawn at `opacity`.
public struct LogoPose: Sendable {
    public let scale: Double
    public let opacity: Double
}

/// The logo's 0.75 s pop, as numbers: `scripts/logo-motion.ps1` is the source, and the Windows and Linux apps carry the
/// same maths in `shared/Edgepad.Core/LogoMotion.cs`. Geometry is on the 1024-unit logo canvas; angles run clockwise
/// from the right (0 = right, 90 = down). Pure, so the maths is a test and every frame is a function of the time.
public enum LogoMotion {
    public static let canvas = 1024.0
    public static let pivot = 881.8
    public static let strokeWidth = 88.0
    public static let arcRadius = 288.0
    public static let totalMs = 760.0

    private static let stagger = 60.0
    private static let partMs = 420.0
    private static let fromScale = 0.55
    private static let fadeIn = 0.25

    /// The arc, then each tick in angle order (the needle is the middle one), as the pop plays them.
    public static let strokes: [LogoStroke] = [
        LogoStroke(isArc: true, angle: 180, inner: 0, outer: arcRadius, accent: true),
        LogoStroke(isArc: false, angle: 180, inner: 424, outer: 656, accent: false),
        LogoStroke(isArc: false, angle: 202.5, inner: 424, outer: 656, accent: false),
        LogoStroke(isArc: false, angle: 225, inner: 424, outer: 720, accent: true),
        LogoStroke(isArc: false, angle: 247.5, inner: 424, outer: 656, accent: false),
        LogoStroke(isArc: false, angle: 270, inner: 424, outer: 656, accent: false),
    ]

    /// Each stroke's radius scale (toward the pivot) and opacity at `ms` into the pop.
    public static func frame(ms: Double) -> [LogoPose] {
        strokes.indices.map { index in
            let x = min(max((ms - Double(index) * stagger) / partMs, 0), 1)
            return LogoPose(
                scale: fromScale + (1 - fromScale) * spring(x),
                opacity: min(max(x / fadeIn, 0), 1))
        }
    }

    private static func spring(_ x: Double) -> Double {
        x >= 1 ? 1 : 1 - exp(-6 * x) * cos(9 * x)
    }
}
