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

    /// The tile's outline, exactly as the logo SVG's `bg` path (continuous corners): the one copy of its numbers.
    public static let tilePath =
        "M354.65 0H669.35C771.47 0 822.53 0 868.58 15.2C937.64 37.99 986.01 86.36 1008.8 155.42C1024 201.47 1024 252.53 "
        + "1024 354.65V669.35C1024 771.47 1024 822.53 1008.8 868.58C986.01 937.64 937.64 986.01 868.58 1008.8C822.53 1024 "
        + "771.47 1024 669.35 1024H354.65C252.53 1024 201.47 1024 155.42 1008.8C86.36 986.01 37.99 937.64 15.2 868.58C0 "
        + "822.53 0 771.47 0 669.35V354.65C0 252.53 0 201.47 15.2 155.42C37.99 86.36 86.36 37.99 155.42 15.2C201.47 0 "
        + "252.53 0 354.65 0Z"

    /// Where the tile's outline starts.
    public static let tileStart = (x: 354.65, y: 0.0)

    /// The tile as segments: two numbers is a line to there, six a cubic (two controls, then the end). Closed.
    public static let tileSegments: [[Double]] = parseTile()

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

    private static func parseTile() -> [[Double]] {
        var tokens: [String] = []
        var number = ""
        for char in tilePath {
            if char.isLetter {
                if !number.isEmpty { tokens.append(number) }
                number = ""
                tokens.append(String(char))
            } else if char == " " {
                if !number.isEmpty { tokens.append(number) }
                number = ""
            } else {
                number.append(char)
            }
        }
        if !number.isEmpty { tokens.append(number) }

        var segments: [[Double]] = []
        var x = 0.0
        var y = 0.0
        var index = 0
        var command = " "
        func next() -> Double {
            defer { index += 1 }
            return Double(tokens[index]) ?? 0
        }
        while index < tokens.count {
            if tokens[index].first?.isLetter == true {
                command = tokens[index]
                index += 1
            }
            switch command {
            case "M":
                x = next()
                y = next()
            case "H":
                x = next()
                segments.append([x, y])
            case "V":
                y = next()
                segments.append([x, y])
            case "C":
                let curve = (0..<6).map { _ in next() }
                x = curve[4]
                y = curve[5]
                segments.append(curve)
            default:
                return segments
            }
        }
        return segments
    }

    private static func spring(_ x: Double) -> Double {
        x >= 1 ? 1 : 1 - exp(-6 * x) * cos(9 * x)
    }
}
