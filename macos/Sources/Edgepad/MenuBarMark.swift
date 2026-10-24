import AppKit

/// The Edgepad dial for the menu bar, drawn from the mark's own geometry (assets/edgepad-light.svg, 1024-unit
/// space, without its tile) as a template image: one colour, which macOS renders black or white for the bar and
/// its highlight.
enum MenuBarMark {
    private static let side: CGFloat = 18
    /// The mark's strokes span 188...780 in its own units, including the round caps; they fill the image less a
    /// point of margin.
    private static let origin: CGFloat = 188
    private static let scale: CGFloat = (side - 2) / 592
    /// About the weight of an SF Symbol at this size.
    private static let lineWidth: CGFloat = 1.6

    static func image() -> NSImage {
        let image = NSImage(size: NSSize(width: side, height: side), flipped: true) { _ in
            let path = NSBezierPath()
            path.lineWidth = lineWidth
            path.lineCapStyle = .round
            // The five ticks and the arc of the mark, in the order of the SVG paths.
            let ticks: [((CGFloat, CGFloat), (CGFloat, CGFloat))] = [
                ((394, 744), (224, 744)), ((420.6, 610.1), (263.6, 545)), ((610.1, 420.6), (545, 263.6)),
                ((744, 394), (744, 224)), ((496.5, 496.5), (348, 348)),
            ]
            for (from, to) in ticks {
                path.move(to: point(from.0, from.1))
                path.line(to: point(to.0, to.1))
            }
            // A quarter circle of radius 240 about (504, 504), from the bottom to the right.
            path.move(to: point(504, 744))
            path.curve(to: point(744, 504), controlPoint1: point(636.55, 744), controlPoint2: point(744, 636.55))
            NSColor.black.setStroke()
            path.stroke()
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "Edgepad"
        return image
    }

    private static func point(_ x: CGFloat, _ y: CGFloat) -> NSPoint {
        NSPoint(x: 1 + (x - origin) * scale, y: 1 + (y - origin) * scale)
    }
}
