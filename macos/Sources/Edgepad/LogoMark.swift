import AppKit
import EdgepadCore

/// The Edgepad logo, drawn here rather than loaded, that plays its 0.75 s pop when `play()` is called
/// (`LogoMotion`). Decorative: hidden from accessibility. The timer runs only while the pop plays, and with "Reduce
/// motion" on the finished logo is drawn at once.
final class LogoMark: NSView {
    private var ms = LogoMotion.totalMs
    private var started = 0.0
    private var timer: Timer?

    init(size: CGFloat) {
        super.init(frame: NSRect(x: 0, y: 0, width: size, height: size))
        translatesAutoresizingMaskIntoConstraints = false
        widthAnchor.constraint(equalToConstant: size).isActive = true
        heightAnchor.constraint(equalToConstant: size).isActive = true
        setAccessibilityElement(false)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("LogoMark is built in code") }

    /// Y runs down, as on the logo canvas.
    override var isFlipped: Bool { true }

    func play() {
        stop()
        guard !Motion.reduce else { return }
        ms = 0
        started = ProcessInfo.processInfo.systemUptime
        let frames = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in self?.step() }
        RunLoop.main.add(frames, forMode: .common)
        timer = frames
        needsDisplay = true
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    private func step() {
        ms = min(LogoMotion.totalMs, (ProcessInfo.processInfo.systemUptime - started) * 1000)
        if ms >= LogoMotion.totalMs { stop() }
        needsDisplay = true
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window == nil { stop() }
    }

    override func draw(_ dirtyRect: NSRect) {
        let dark = effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
        let tile = dark ? Self.rgb(0x1B1C20) : Self.rgb(0xFFFFFF)
        let ink = dark ? Self.rgb(0xF2F3F5) : Self.rgb(0x16171A)
        let accent = dark ? Self.rgb(0x4DA3FF) : Self.rgb(0x0068D6)
        let side = min(bounds.width, bounds.height)
        let unit = side / LogoMotion.canvas

        let body = NSBezierPath()
        func tilePoint(_ x: Double, _ y: Double) -> NSPoint { NSPoint(x: x * unit, y: y * unit) }
        body.move(to: tilePoint(LogoMotion.tileStart.x, LogoMotion.tileStart.y))
        for segment in LogoMotion.tileSegments {
            if segment.count == 2 {
                body.line(to: tilePoint(segment[0], segment[1]))
            } else {
                body.curve(
                    to: tilePoint(segment[4], segment[5]), controlPoint1: tilePoint(segment[0], segment[1]),
                    controlPoint2: tilePoint(segment[2], segment[3]))
            }
        }
        body.close()
        tile.setFill()
        body.fill()
        ink.withAlphaComponent(0.12).setStroke()
        body.lineWidth = 1
        body.stroke()

        let poses = LogoMotion.frame(ms: ms)
        for (stroke, pose) in zip(LogoMotion.strokes, poses) where pose.opacity > 0 {
            let path = NSBezierPath()
            path.lineWidth = LogoMotion.strokeWidth * unit
            path.lineCapStyle = .round
            path.lineJoinStyle = .round
            func point(_ reach: Double, _ degrees: Double) -> NSPoint {
                let radians = degrees * .pi / 180
                return NSPoint(
                    x: (LogoMotion.pivot + reach * pose.scale * cos(radians)) * unit,
                    y: (LogoMotion.pivot + reach * pose.scale * sin(radians)) * unit)
            }
            if stroke.isArc {
                // Raw angles run 180 to 270, y down: the quarter that bulges away from the pivot, up and left.
                path.appendArc(
                    withCenter: NSPoint(x: LogoMotion.pivot * unit, y: LogoMotion.pivot * unit),
                    radius: stroke.outer * pose.scale * unit, startAngle: 180, endAngle: 270, clockwise: false)
            } else {
                path.move(to: point(stroke.inner, stroke.angle))
                path.line(to: point(stroke.outer, stroke.angle))
            }
            (stroke.accent ? accent : ink).withAlphaComponent(pose.opacity).setStroke()
            path.stroke()
        }
    }

    private static func rgb(_ value: UInt32) -> NSColor {
        NSColor(
            srgbRed: CGFloat((value >> 16) & 0xFF) / 255, green: CGFloat((value >> 8) & 0xFF) / 255,
            blue: CGFloat(value & 0xFF) / 255, alpha: 1)
    }
}
