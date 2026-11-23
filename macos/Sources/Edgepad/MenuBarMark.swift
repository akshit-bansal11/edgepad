import AppKit

/// The Edgepad dial for the menu bar, drawn from the mark's own geometry (assets/edgepad-light.svg, 1024-unit
/// space, without its tile) as a template image: one colour, which macOS renders black or white for the bar and
/// its highlight.
enum MenuBarMark {
    private static let side: CGFloat = 18
    /// The mark's strokes span 181.8...925.8 in its own units, including the round caps; they fill the image less a
    /// point of margin. The numbers are scripts/logo.ps1's output; regenerate them from there if the mark changes.
    private static let origin: CGFloat = 181.8
    private static let scale: CGFloat = (side - 2) / 744
    /// About the weight of an SF Symbol at this size.
    private static let lineWidth: CGFloat = 1.6

    /// `badged` adds a dot in the top corner: something needs the owner, and the menu says what.
    static func image(badged: Bool = false) -> NSImage {
        let image = NSImage(size: NSSize(width: side, height: side), flipped: true) { _ in
            let path = NSBezierPath()
            path.lineWidth = lineWidth
            path.lineCapStyle = .round
            // The five ticks and the arc of the mark, in the order of the SVG paths.
            let ticks: [((CGFloat, CGFloat), (CGFloat, CGFloat))] = [
                ((457.8, 881.8), (225.8, 881.8)), ((490.075, 719.542), (275.735, 630.76)),
                ((719.542, 490.075), (630.76, 275.735)), ((881.8, 457.8), (881.8, 225.8)),
                ((581.987, 581.987), (372.683, 372.683)),
            ]
            for (from, to) in ticks {
                path.move(to: point(from.0, from.1))
                path.line(to: point(to.0, to.1))
            }
            // A quarter circle of radius 288 about the pivot (881.8, 881.8), from its left point up to its top point,
            // bulging away from the pivot. Control points sit 0.5523 x 288 = 159.058 along each end's tangent.
            path.move(to: point(593.8, 881.8))
            path.curve(
                to: point(881.8, 593.8), controlPoint1: point(593.8, 722.742), controlPoint2: point(722.742, 593.8))
            NSColor.black.setStroke()
            path.stroke()
            if badged {
                NSColor.black.setFill()
                NSBezierPath(ovalIn: NSRect(x: side - 7, y: 0, width: 7, height: 7)).fill()
            }
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = badged ? "Edgepad, needs permission" : "Edgepad"
        return image
    }

    private static func point(_ x: CGFloat, _ y: CGFloat) -> NSPoint {
        NSPoint(x: 1 + (x - origin) * scale, y: 1 + (y - origin) * scale)
    }
}
