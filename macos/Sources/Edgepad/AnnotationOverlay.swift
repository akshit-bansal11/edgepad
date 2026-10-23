import AppKit
import EdgepadCore

/// A window that can never take focus: it is only ink on glass, and the phone's keyboard must keep typing into
/// whatever the owner was using underneath.
private final class InkWindow: NSWindow {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

/// Flipped so the strokes' y-down screen pixels draw as they are.
private final class InkView: NSView {
    var strokes: [Stroke] = []

    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        guard let g = NSGraphicsContext.current?.cgContext else { return }
        // Points, not pixels: the window is in points and the strokes scale to whatever size they are given.
        let width = Int(bounds.width)
        let height = Int(bounds.height)
        for stroke in strokes {
            StrokePainter.draw(stroke, in: g, width: width, height: height)
        }
    }
}

/// The transparent, click-through layer the strokes are drawn on, over the primary display. Main thread only.
final class AnnotationOverlay {
    private let window: NSWindow
    private let view: InkView

    init(screen: NSScreen) {
        let frame = screen.frame
        view = InkView(frame: NSRect(origin: .zero, size: frame.size))
        window = InkWindow(contentRect: frame, styleMask: .borderless, backing: .buffered, defer: false)
        window.contentView = view
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        // Above full-screen apps and the menu bar, and out of every click: the phone is the pen, and the mouse
        // must still reach the apps underneath.
        window.level = .screenSaver
        window.ignoresMouseEvents = true
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        window.isReleasedWhenClosed = false
        // Not makeKeyAndOrderFront: showing the overlay must not take focus from the owner's app.
        window.orderFrontRegardless()
    }

    func present(_ strokes: [Stroke]) {
        view.strokes = strokes
        view.needsDisplay = true
    }

    func close() {
        window.orderOut(nil)
        window.close()
    }
}
