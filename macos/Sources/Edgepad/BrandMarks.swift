import AppKit

/// Brand marks drawn from SVG path data, as template images so AppKit tints them for light and dark.
enum BrandMarks {
    /// GitHub's Octicons "mark-github", 16 x 16 (MIT licence, Copyright (c) 2021 GitHub Inc.).
    static let gitHub =
        "M8 0c4.42 0 8 3.58 8 8a8.013 8.013 0 0 1-5.45 7.59c-.4.08-.55-.17-.55-.38 0-.27.01-1.13.01-2.2 "
        + "0-.75-.25-1.23-.54-1.48 1.78-.2 3.65-.88 3.65-3.95 0-.88-.31-1.59-.82-2.15.08-.2.36-1.02-.08-2.12 0 "
        + "0-.67-.22-2.2.82-.64-.18-1.32-.27-2-.27-.68 0-1.36.09-2 .27-1.53-1.03-2.2-.82-2.2-.82-.44 1.1-.16 "
        + "1.92-.08 2.12-.51.56-.82 1.28-.82 2.15 0 3.06 1.86 3.75 3.64 3.95-.23.2-.44.55-.51 "
        + "1.07-.46.21-1.61.55-2.33-.66-.15-.24-.6-.83-1.23-.82-.67.01-.27.38.01.53.34.19.73.9.82 1.13.16.45.68 "
        + "1.31 2.69.94 0 .67.01 1.3.01 1.49 0 .21-.15.45-.55.38A7.995 7.995 0 0 1 0 8c0-4.42 3.58-8 8-8Z"

    /// A filled `data` path on a `grid` x `grid` square, `size` points wide, as a template image.
    static func image(path data: String, grid: CGFloat, size: CGFloat) -> NSImage {
        let shape = path(data)
        let image = NSImage(size: NSSize(width: size, height: size), flipped: true) { rect in
            guard let g = NSGraphicsContext.current?.cgContext else { return false }
            let scale = rect.width / grid
            g.scaleBy(x: scale, y: scale)
            g.addPath(shape)
            g.setFillColor(NSColor.black.cgColor)
            g.fillPath()
            return true
        }
        image.isTemplate = true
        return image
    }

    /// SVG path data as a CGPath: the move, line, cubic, circular arc and close commands, absolute and relative,
    /// where "1-14" is two numbers and ".7.7" is too. That is every command the marks here use.
    static func path(_ data: String) -> CGPath {
        let chars = Array(data)
        var at = 0
        func skip() {
            while at < chars.count, chars[at] == " " || chars[at] == "," { at += 1 }
        }
        func number() -> CGFloat {
            skip()
            let begin = at
            if at < chars.count, chars[at] == "-" || chars[at] == "+" { at += 1 }
            var dot = false
            while at < chars.count, (chars[at] >= "0" && chars[at] <= "9") || (chars[at] == "." && !dot) {
                if chars[at] == "." { dot = true }
                at += 1
            }
            return CGFloat(Double(String(chars[begin..<at])) ?? 0)
        }

        let path = CGMutablePath()
        var current = CGPoint.zero
        var start = CGPoint.zero
        var command: Character = "M"
        while true {
            skip()
            if at >= chars.count { break }
            if chars[at].isLetter {
                command = chars[at]
                at += 1
            }
            let relative = command.isLowercase
            let origin = relative ? current : .zero
            func point() -> CGPoint {
                let x = number()
                let y = number()
                return CGPoint(x: origin.x + x, y: origin.y + y)
            }
            switch command {
            case "M", "m":
                current = point()
                start = current
                path.move(to: current)
                // Pairs after a move are lines, in the same absolute or relative sense.
                command = relative ? "l" : "L"
            case "L", "l":
                current = point()
                path.addLine(to: current)
            case "C", "c":
                let first = point()
                let second = point()
                let end = point()
                path.addCurve(to: end, control1: first, control2: second)
                current = end
            case "A", "a":
                let radius = number()
                _ = number()
                _ = number()
                let large = number() != 0
                let sweep = number() != 0
                let end = point()
                arc(path, from: current, to: end, radius: radius, large: large, sweep: sweep)
                current = end
            case "Z", "z":
                path.closeSubpath()
                current = start
            default:
                return path
            }
        }
        return path
    }

    /// The SVG endpoint-to-centre conversion for a circle (SVG 1.1, appendix F.6.5).
    private static func arc(
        _ path: CGMutablePath, from: CGPoint, to: CGPoint, radius: CGFloat, large: Bool, sweep: Bool
    ) {
        if from == to { return }
        let halfX = (from.x - to.x) / 2
        let halfY = (from.y - to.y) / 2
        let squared = halfX * halfX + halfY * halfY
        let r = max(abs(radius), squared.squareRoot())
        let reach = max(0, (r * r - squared) / squared).squareRoot() * (large == sweep ? -1 : 1)
        let centre = CGPoint(x: reach * halfY + (from.x + to.x) / 2, y: -reach * halfX + (from.y + to.y) / 2)
        // A path takes raw angles, which grow with the SVG sweep flag, so clockwise (falling) is its opposite.
        path.addArc(
            center: centre, radius: r,
            startAngle: atan2(from.y - centre.y, from.x - centre.x),
            endAngle: atan2(to.y - centre.y, to.x - centre.x), clockwise: !sweep)
    }
}
