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
/// `protocol/actions.txt`. A colour of the phone's own choosing arrives as `TextKind.penRgb` and only through
/// `PenStyle.parseRgb`.
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

/// What the next stroke will look like, fixed when its finger goes down so a later change never repaints ink already
/// on the screen. Width and opacity are the phone's own numbers (SET PEN_WIDTH and PEN_OPACITY), kept as sent and
/// turned into pixels and alpha only where a stroke is drawn.
public struct PenStyle: Equatable {
    /// 20 steps are 0.4% of the screen's height, the pen's width before the phone could choose, and a round number
    /// to send as the phone's default.
    public static let defaultWidth = 20
    /// Below this the width is the 0.1% floor: 1...4 are accepted and all mean 5, so a slider may start at 5.
    public static let minWidth = 5
    public static let maxWidth = 100
    public static let defaultOpacity = 100
    public static let minOpacity = 10
    public static let maxOpacity = 100

    /// The highlighter is this many times as wide as the pen is set to be, which is today's 2% against 0.4%.
    public static let highlighterWidths: Float = 5

    /// One width step is 0.02% of the screen's height, written as a divisor so 20 steps are exactly 0.004.
    private static let widthStep: Float = 5000

    /// Black, the first palette colour, at the default width and fully opaque.
    public static let `default` = PenStyle(rgb: 0x00_0000, width: defaultWidth, opacity: defaultOpacity)

    /// 0xRRGGBB, from the palette or from `parseRgb`; nothing else can reach it.
    public var rgb: UInt32
    /// 1...100, in steps of 0.02% of the screen's height.
    public var width: Int
    /// 10...100 percent.
    public var opacity: Int

    public init(rgb: UInt32, width: Int, opacity: Int) {
        self.rgb = rgb
        self.width = width
        self.opacity = opacity
    }

    public static func isWidth(_ value: Int) -> Bool {
        (1...maxWidth).contains(value)
    }

    public static func isOpacity(_ value: Int) -> Bool {
        (minOpacity...maxOpacity).contains(value)
    }

    /// The pen's width as a share of the screen's height, 0.001 to 0.02.
    public var penShare: Float {
        Float(max(PenStyle.minWidth, width)) / PenStyle.widthStep
    }

    /// Exactly six hex digits, either case, and nothing around them: no "#", no sign, no space, no "0x". Read byte by
    /// byte because a string's own count is in characters, where "\r\n" is one, and this one string is the phone's to
    /// choose.
    public static func parseRgb(_ text: String) -> UInt32? {
        let bytes = Array(text.utf8)
        guard bytes.count == 6 else { return nil }

        var value: UInt32 = 0
        for byte in bytes {
            let digit: UInt32
            switch byte {
            case UInt8(ascii: "0")...UInt8(ascii: "9"):
                digit = UInt32(byte - UInt8(ascii: "0"))
            case UInt8(ascii: "A")...UInt8(ascii: "F"):
                digit = UInt32(byte - UInt8(ascii: "A")) + 10
            case UInt8(ascii: "a")...UInt8(ascii: "f"):
                digit = UInt32(byte - UInt8(ascii: "a")) + 10
            default:
                return nil
            }
            value = value << 4 | digit
        }
        return value
    }
}

/// One finger-down to finger-up. Freehand tools keep every point; the shape tools keep the down point and the
/// latest one, because a shape is only ever its two corners.
public final class Stroke {
    /// The highlighter is translucent so the text under it stays readable: 35% of its colour, times the opacity the
    /// phone set. It is stroked once per path, so an overlapping stretch of one stroke is not darker than the rest.
    public static let highlighterAlpha: Float = 0.35

    /// The arrow head's wings are this many pen widths long.
    private static let headWidths: Float = 4

    /// Radians between the shaft and a wing, a little under 30 degrees.
    private static let headAngle: Float = 0.5

    public let tool: StrokeTool

    public let style: PenStyle

    public internal(set) var points: [NormPoint]

    public init(tool: StrokeTool, style: PenStyle, points: [NormPoint] = []) {
        self.tool = tool
        self.style = style
        self.points = points
    }

    /// The colour as 0xRRGGBB, without the alpha `alpha` gives it.
    public var rgb: UInt32 {
        style.rgb
    }

    /// The ink's alpha, 0...1: the opacity the phone set, and for the highlighter that of its 35%.
    public var alpha: Float {
        (tool == .highlighter ? Stroke.highlighterAlpha : 1) * Float(style.opacity) / 100
    }

    private var isShape: Bool {
        tool == .line || tool == .arrow || tool == .rectangle || tool == .ellipse
    }

    /// The width as a share of the screen's height, so a stroke looks alike on any display; at least one pixel.
    public func widthPx(height: Int) -> Float {
        max(1, style.penShare * (tool == .highlighter ? PenStyle.highlighterWidths : 1) * Float(height))
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

    /// The whole board's ceiling. The per-stroke cap alone bounds nothing: every PEN down starts a new stroke,
    /// so a stream of them would grow the board, and every redraw, without end. Past it new ink is dropped
    /// until undo or clear makes room, rather than old ink being thrown away unasked.
    static let maxPointsPerBoard = 200_000

    /// The points held in `strokes`, so the board ceiling is checked without walking every stroke per frame.
    private var committedPoints = 0

    public private(set) var mode = BoardMode.off
    public private(set) var tool = StrokeTool.pen
    /// The colour, width and opacity the next stroke takes. They outlive `begin`, as the colour always did, and end
    /// with the board.
    public private(set) var style = PenStyle.default

    /// The strokes whose finger has lifted, oldest first.
    public private(set) var strokes: [Stroke] = []

    /// The stroke under the finger now: drawn by the overlay, but not yet undoable or exportable.
    public private(set) var active: Stroke?

    public init() {}

    /// Starts a session from nothing, so the last session's strokes never leak into this one.
    public func begin(_ newMode: BoardMode) {
        strokes.removeAll()
        committedPoints = 0
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
        guard let rgb = Palette.colour(index) else { return false }
        style.rgb = rgb
        return true
    }

    /// Any 0xRRGGBB: the caller has already checked it came from `PenStyle.parseRgb`.
    public func setRgb(_ rgb: UInt32) -> Bool {
        guard rgb <= 0xFF_FFFF else { return false }
        style.rgb = rgb
        return true
    }

    /// False, and nothing changes, unless `width` is 1...100.
    public func setWidth(_ width: Int) -> Bool {
        guard PenStyle.isWidth(width) else { return false }
        style.width = width
        return true
    }

    /// False, and nothing changes, unless `opacity` is 10...100.
    public func setOpacity(_ opacity: Int) -> Bool {
        guard PenStyle.isOpacity(opacity) else { return false }
        style.opacity = opacity
        return true
    }

    /// Takes back the last finished stroke. False when there is none or no session.
    public func undo() -> Bool {
        guard mode != .off, let last = strokes.popLast() else { return false }
        committedPoints -= last.points.count
        return true
    }

    /// Empties the board, a stroke in progress included. False outside a session.
    public func clear() -> Bool {
        guard mode != .off else { return false }
        strokes.removeAll()
        committedPoints = 0
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
            guard committedPoints < StrokeBoard.maxPointsPerBoard else { return false }
            active = Stroke(tool: tool, style: style, points: [point])
            return true
        case 2:
            guard let open = active, open.points.count < StrokeBoard.maxPointsPerStroke else { return false }
            guard committedPoints + open.points.count < StrokeBoard.maxPointsPerBoard else { return false }
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
            committedPoints += open.points.count
            active = nil
        }
    }
}
