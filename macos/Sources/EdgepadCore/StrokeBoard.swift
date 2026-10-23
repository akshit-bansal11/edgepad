import Foundation

/// Whether strokes are being taken at all, and from where they may come.
public enum BoardMode: Equatable {
    case off
    /// The overlay is up and every tool draws on it.
    case annotate
    /// Nothing is shown on this Mac and only the pen tool is accepted: a signature is a pen line.
    case sign
}

public enum StrokeTool: Int, CaseIterable {
    case pen
    case highlighter
    case line
    case arrow
    case rectangle
    case ellipse
}

/// A point as the phone sent it: 0 is the left/top edge of the Mac's screen, 65535 the right/bottom.
public struct NormPoint: Equatable {
    public let x: UInt16
    public let y: UInt16

    public init(_ x: UInt16, _ y: UInt16) {
        self.x = x
        self.y = y
    }
}

/// The colours `ControlId.penColor` indexes, by position, as 0xRRGGBB. A test holds them to the PALETTE rows of
/// `protocol/actions.txt`. Only the index crosses the wire, so an RGB the phone invented cannot reach the overlay.
public enum Palette {
    public static let count = 8

    private static let rgb: [UInt32] = [
        0x00_0000,  // BLACK
        0xFF_FFFF,  // WHITE
        0xFF_3B30,  // RED
        0xFF_9500,  // ORANGE
        0xFF_CC00,  // YELLOW
        0x34_C759,  // GREEN
        0x00_68D6,  // BLUE
        0xAF_52DE,  // PURPLE
    ]

    /// The 0xRRGGBB colour at `index`, or nil when the index is not in the palette.
    public static func colour(_ index: Int) -> UInt32? {
        rgb.indices.contains(index) ? rgb[index] : nil
    }
}

/// One finger-down to finger-up. Freehand tools keep every point; the shape tools keep the down point and the
/// latest one, because a shape is only ever its two corners.
public final class Stroke {
    /// Pen and highlighter widths, as a share of the screen's height so a stroke looks alike on any display.
    public static let penShare: Float = 0.004
    public static let highlighterShare: Float = 0.02

    /// The highlighter is translucent so the text under it stays readable. It is stroked once per path, so an
    /// overlapping stretch of one stroke is not darker than the rest.
    public static let highlighterAlpha: Float = 0.35

    /// The arrow head's wings are this many pen widths long.
    private static let headWidths: Float = 4

    /// Radians between the shaft and a wing, a little under 30 degrees.
    private static let headAngle: Float = 0.5

    public let tool: StrokeTool

    /// An index into `Palette`.
    public let colour: Int

    public internal(set) var points: [NormPoint]

    public init(tool: StrokeTool, colour: Int, points: [NormPoint] = []) {
        self.tool = tool
        self.colour = colour
        self.points = points
    }

    private var isShape: Bool {
        tool == .line || tool == .arrow || tool == .rectangle || tool == .ellipse
    }

    public func widthPx(height: Int) -> Float {
        max(1, (tool == .highlighter ? Stroke.highlighterShare : Stroke.penShare) * Float(height))
    }

    /// The points in pixels of a `width` by `height` screen, y down.
    public func path(width: Int, height: Int) -> [SIMD2<Float>] {
        points.map { SIMD2(Float($0.x) / 65535 * Float(width - 1), Float($0.y) / 65535 * Float(height - 1)) }
    }

    /// The outer ends of an arrow head, which meet the tip at the shaft's end. Empty for anything but an arrow,
    /// and for an arrow with no length yet, which has no direction to point in.
    public func arrowWings(width: Int, height: Int) -> [SIMD2<Float>] {
        let line = path(width: width, height: height)
        guard tool == .arrow, line.count >= 2, line[1] != line[0] else { return [] }

        let along = line[0] - line[1]
        let length = (along * along).sum().squareRoot()
        let back = along / length * (Stroke.headWidths * widthPx(height: height))
        return [line[1] + Stroke.rotate(back, Stroke.headAngle), line[1] + Stroke.rotate(back, -Stroke.headAngle)]
    }

    /// The box the ink fills, half the stroke's width beyond every point; nil before any point.
    public func ink(width: Int, height: Int) -> (min: SIMD2<Float>, max: SIMD2<Float>)? {
        let all = path(width: width, height: height) + arrowWings(width: width, height: height)
        guard let first = all.first else { return nil }

        let half = SIMD2<Float>(repeating: widthPx(height: height) / 2)
        let low = all.reduce(first) { pointwiseMin($0, $1) }
        let high = all.reduce(first) { pointwiseMax($0, $1) }
        return (low - half, high + half)
    }

    /// A shape keeps two points: the down point, then wherever the finger is now.
    func extend(_ point: NormPoint) {
        if isShape && points.count > 1 {
            points[1] = point
        } else {
            points.append(point)
        }
    }

    private static func rotate(_ v: SIMD2<Float>, _ radians: Float) -> SIMD2<Float> {
        let sine = Float(sin(Double(radians)))
        let cosine = Float(cos(Double(radians)))
        return SIMD2(v.x * cosine - v.y * sine, v.x * sine + v.y * cosine)
    }
}

/// The strokes of one annotate or sign session, and the rules for which PEN frame may change them. Pure: no
/// window and no drawing, so the overlay and the signature export both read it and a test can drive it.
public final class StrokeBoard {
    /// A held finger sends a point every few milliseconds and nothing else bounds a stroke, so one is capped
    /// rather than left to grow until the process does.
    static let maxPointsPerStroke = 20_000

    public private(set) var mode = BoardMode.off
    public private(set) var tool = StrokeTool.pen
    public private(set) var colour = 0

    /// The strokes whose finger has lifted, oldest first.
    public private(set) var strokes: [Stroke] = []

    /// The stroke under the finger now: drawn by the overlay, but not yet undoable or exportable.
    public private(set) var active: Stroke?

    public init() {}

    /// Starts a session from nothing, so the last session's strokes never leak into this one.
    public func begin(_ newMode: BoardMode) {
        strokes.removeAll()
        active = nil
        mode = newMode
        tool = .pen
    }

    /// Ends the session and throws its strokes away.
    public func end() {
        begin(.off)
    }

    /// False when the tool is not allowed in this mode, so the caller counts the frame as dropped.
    public func setTool(_ newTool: StrokeTool) -> Bool {
        if mode == .off || (mode == .sign && newTool != .pen) {
            return false
        }
        tool = newTool
        return true
    }

    /// False when `index` is not in the palette.
    public func setColour(_ index: Int) -> Bool {
        guard Palette.colour(index) != nil else { return false }
        colour = index
        return true
    }

    /// Takes back the last finished stroke. False when there is none or no session.
    public func undo() -> Bool {
        guard mode != .off, !strokes.isEmpty else { return false }
        strokes.removeLast()
        return true
    }

    /// Empties the board, a stroke in progress included. False outside a session.
    public func clear() -> Bool {
        guard mode != .off else { return false }
        strokes.removeAll()
        active = nil
        return true
    }

    /// One PEN frame. False means dropped: no session, a state that is not up/down/move (a newer phone may add
    /// states), or a move or up with no finger down. An up commits the stroke, and a down while one is still
    /// open commits that one first, because the up that would have ended it was lost.
    public func pen(x: UInt16, y: UInt16, state: UInt8) -> Bool {
        guard mode != .off else { return false }

        let point = NormPoint(x, y)
        switch state {
        case 1:
            commit()
            active = Stroke(tool: tool, colour: colour, points: [point])
            return true
        case 2:
            guard let open = active, open.points.count < StrokeBoard.maxPointsPerStroke else { return false }
            open.extend(point)
            return true
        case 0:
            guard let open = active else { return false }
            open.extend(point)
            commit()
            return true
        default:
            return false
        }
    }

    /// One of the annotate and sign actions, 40..51 except `signSave`, which needs the export and is the app's.
    /// False means dropped.
    public func run(_ action: ActionId) -> Bool {
        switch action {
        case .annotateBegin:
            begin(.annotate)
            return true
        case .signBegin:
            begin(.sign)
            return true
        case .annotateEnd:
            // Ends a signature too: the phone has one way out of either surface.
            end()
            return true
        case .annotateUndo:
            return undo()
        case .annotateClear:
            return clear()
        case .toolPen:
            return setTool(.pen)
        case .toolHighlighter:
            return setTool(.highlighter)
        case .toolLine:
            return setTool(.line)
        case .toolArrow:
            return setTool(.arrow)
        case .toolRectangle:
            return setTool(.rectangle)
        case .toolEllipse:
            return setTool(.ellipse)
        default:
            return false
        }
    }

    private func commit() {
        if let open = active {
            strokes.append(open)
            active = nil
        }
    }
}
