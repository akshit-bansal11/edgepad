import AppKit
import EdgepadCore

/// Draws strokes, and turns finished ones into the PNG a signature is pasted from: transparent, at screen
/// resolution, cropped to the ink so it drops into a document at a sensible size.
enum SignatureExport {
    /// Breathing room around the ink, in pixels, so the stroke's anti-aliased edge is never clipped.
    static let margin = 8

    /// The part of a `width` by `height` screen that the ink and its margin cover; nil when there is no ink.
    static func cropRect(_ strokes: [Stroke], width: Int, height: Int) -> CGRect? {
        guard width > 0, height > 0 else { return nil }

        let boxes = strokes.compactMap { $0.ink(width: width, height: height) }
        guard let first = boxes.first else { return nil }
        let low = boxes.reduce(first.min) { pointwiseMin($0, $1.min) }
        let high = boxes.reduce(first.max) { pointwiseMax($0, $1.max) }

        let left = Int(low.x.rounded(.down)) - margin
        let top = Int(low.y.rounded(.down)) - margin
        let right = Int(high.x.rounded(.up)) + margin
        let bottom = Int(high.y.rounded(.up)) + margin
        let wanted = CGRect(x: left, y: top, width: right - left, height: bottom - top)
        let crop = wanted.intersection(CGRect(x: 0, y: 0, width: width, height: height))
        return crop.isEmpty ? nil : crop
    }

    /// The PNG, or nil when there is nothing to export.
    static func render(_ strokes: [Stroke], width: Int, height: Int) -> Data? {
        guard let crop = cropRect(strokes, width: width, height: height) else { return nil }

        // Only the crop is allocated: a 5K screen is 59 MB of pixels and almost all of them would be empty.
        guard let g = makeContext(width: Int(crop.width), height: Int(crop.height)) else { return nil }

        // The strokes are y-down screen pixels and a bitmap context is y-up: flip it, then slide the crop to 0,0.
        g.translateBy(x: 0, y: crop.height)
        g.scaleBy(x: 1, y: -1)
        g.translateBy(x: -crop.minX, y: -crop.minY)
        for stroke in strokes {
            StrokePainter.draw(stroke, in: g, width: width, height: height)
        }
        guard let image = g.makeImage() else { return nil }
        return NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:])
    }

    /// A transparent RGBA bitmap; a new one is cleared, which is what makes the PNG transparent.
    private static func makeContext(width w: Int, height h: Int) -> CGContext? {
        guard let s = CGColorSpace(name: CGColorSpace.sRGB) else { return nil }
        let i = CGImageAlphaInfo.premultipliedLast.rawValue
        let g = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0, space: s, bitmapInfo: i)
        g?.clear(CGRect(x: 0, y: 0, width: w, height: h))
        return g
    }
}

/// One stroke as it looks on a `width` by `height` screen, y down. The overlay draws with it too, so what the
/// owner sees and what a signature saves cannot drift apart.
enum StrokePainter {
    static func draw(_ stroke: Stroke, in g: CGContext, width: Int, height: Int) {
        let points = stroke.path(width: width, height: height).map { CGPoint(x: CGFloat($0.x), y: CGFloat($0.y)) }
        guard let first = points.first else { return }
        if stroke.isLaser {
            drawLaser(stroke, points: points, in: g, height: height)
            return
        }

        // The stroke's own colour, width and opacity, so the signature saved is the one the phone's sliders showed.
        let alpha = CGFloat(stroke.alpha)
        let rgb = stroke.rgb
        let red = CGFloat((rgb >> 16) & 0xFF) / 255
        let green = CGFloat((rgb >> 8) & 0xFF) / 255
        let blue = CGFloat(rgb & 0xFF) / 255
        let colour = CGColor(srgbRed: red, green: green, blue: blue, alpha: alpha)
        let size = CGFloat(stroke.widthPx(height: height))

        // A tap is a dot, not nothing: a line of no length draws no pixels. Down and up on one spot is two
        // identical points, so it is the points being all alike that marks a tap.
        if points.allSatisfy({ $0 == first }) {
            g.setFillColor(colour)
            g.fillEllipse(in: CGRect(x: first.x - size / 2, y: first.y - size / 2, width: size, height: size))
            return
        }

        // One path, one stroke: overlapping stretches of a translucent highlighter must not darken twice.
        let path = CGMutablePath()
        if stroke.tool == .rectangle || stroke.tool == .ellipse {
            let corner = points[1]
            let left = min(first.x, corner.x)
            let top = min(first.y, corner.y)
            let box = CGRect(x: left, y: top, width: abs(corner.x - first.x), height: abs(corner.y - first.y))
            if stroke.tool == .rectangle {
                path.addRect(box)
            } else {
                path.addEllipse(in: box)
            }
        } else {
            path.addLines(between: points)
            let wings = stroke.arrowWings(width: width, height: height)
            if wings.count == 2 {
                path.move(to: CGPoint(x: CGFloat(wings[0].x), y: CGFloat(wings[0].y)))
                path.addLine(to: points[1])
                path.addLine(to: CGPoint(x: CGFloat(wings[1].x), y: CGFloat(wings[1].y)))
            }
        }

        g.setStrokeColor(colour)
        g.setLineWidth(size)
        g.setLineCap(.round)
        g.setLineJoin(.round)
        g.addPath(path)
        g.strokePath()
    }

    /// A laser: a hot core inside a soft red glow, and for the trailing one a red tail fading and thinning away
    /// behind it. Always red, whatever colour the pen has; the size is the pen width's.
    private static func drawLaser(_ stroke: Stroke, points: [CGPoint], in g: CGContext, height: Int) {
        guard let dot = points.last else { return }

        let radius = CGFloat(stroke.laserRadiusPx(height: height))
        let space = CGColorSpace(name: CGColorSpace.sRGB)
        func red(_ alpha: CGFloat) -> [CGFloat] { [1, 0.1, 0.1, alpha] }

        if stroke.tool == .laserTrail, points.count > 2, let space {
            drawTail(points, radius: radius, space: space, in: g)
        }

        let glow = radius * 2.2
        if let space,
            let gradient = CGGradient(
                colorSpace: space,
                colorComponents: red(0.55) + red(0.25) + red(0),
                locations: [0, 1 / 2.2, 1],
                count: 3)
        {
            g.drawRadialGradient(
                gradient, startCenter: dot, startRadius: 0, endCenter: dot, endRadius: glow, options: [])
        }
        g.setFillColor(CGColor(srgbRed: 1, green: 0.1, blue: 0.1, alpha: 1))
        g.fillEllipse(in: CGRect(x: dot.x - radius, y: dot.y - radius, width: radius * 2, height: radius * 2))
        let hot = radius * 0.35
        g.setFillColor(CGColor(srgbRed: 1, green: 0.816, blue: 0.816, alpha: 1))
        g.fillEllipse(in: CGRect(x: dot.x - hot, y: dot.y - hot, width: hot * 2, height: hot * 2))
    }

    /// The trail as one smooth ribbon: as wide as the dot at the head, tapering to nothing at the tail, and fading
    /// with a single gradient, so no joints or dots show.
    private static func drawTail(_ points: [CGPoint], radius: CGFloat, space: CGColorSpace, in g: CGContext) {
        let n = points.count
        guard let first = points.first, let last = points.last,
            hypot(last.x - first.x, last.y - first.y) >= 1
        else { return }

        var edge = [CGPoint](repeating: .zero, count: n * 2)
        var (nx, ny) = (CGFloat(0), CGFloat(1))
        for i in 0..<n {
            let a = points[max(i - 1, 0)]
            let b = points[min(i + 1, n - 1)]
            let length = hypot(b.x - a.x, b.y - a.y)
            if length > 0.001 { (nx, ny) = (-(b.y - a.y) / length, (b.x - a.x) / length) }
            let half = radius * CGFloat(i) / CGFloat(n - 1)
            edge[i] = CGPoint(x: points[i].x + nx * half, y: points[i].y + ny * half)
            edge[n * 2 - 1 - i] = CGPoint(x: points[i].x - nx * half, y: points[i].y - ny * half)
        }

        func mid(_ a: CGPoint, _ b: CGPoint) -> CGPoint { CGPoint(x: (a.x + b.x) / 2, y: (a.y + b.y) / 2) }
        // A closed loop of quadratic curves through the edge points' midpoints: smooth, no corners but the tail.
        let ribbon = CGMutablePath()
        ribbon.move(to: mid(edge[n * 2 - 1], edge[0]))
        for i in 0..<(n * 2) {
            ribbon.addQuadCurve(to: mid(edge[i], edge[(i + 1) % (n * 2)]), control: edge[i])
        }
        ribbon.closeSubpath()

        let components: [CGFloat] = [1, 0.1, 0.1, 0, 1, 0.1, 0.1, 0.86]
        guard let gradient = CGGradient(colorSpace: space, colorComponents: components, locations: [0, 1], count: 2)
        else { return }
        g.saveGState()
        g.addPath(ribbon)
        g.clip()
        g.drawLinearGradient(gradient, start: first, end: last, options: [])
        g.restoreGState()
    }
}
