import AppKit
import EdgepadCore

/// One connection's annotate and sign sessions: the strokes, the overlay that shows them, and the signature
/// export. Frames can arrive on any thread and change the board under a lock, so each one is counted as taken or
/// dropped at once; the window lives on the main queue and only ever reads a copy.
final class Annotator: AnnotationSink {
    private let lock = NSLock()
    private let board = StrokeBoard()

    /// All of these are guarded by `lock`.
    private var redrawQueued = false
    private var ended = false
    private var observer: NSObjectProtocol?

    /// Only touched on the main queue.
    private var overlay: AnnotationOverlay?

    var signStatusReply: ((String) -> Void)?
    var screenSizeChanged: (() -> Void)?

    init() {
        // A display plugged in or out moves the primary one, so the overlay is rebuilt on the next draw and the
        // phone is told the new aspect ratio.
        let name = NSApplication.didChangeScreenParametersNotification
        observer = NotificationCenter.default.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
            self?.overlay?.close()
            self?.overlay = nil
            self?.redraw()
            self?.screenSizeChanged?()
        }
    }

    /// The primary display, the only one drawn on: a phone's 0...65535 span maps to one screen, and which one is
    /// not something it could choose. `NSScreen.main` is the focused window's screen, so it is not used.
    private static var primary: NSScreen? { NSScreen.screens.first }

    var screenSize: (width: Int, height: Int)? {
        guard let screen = Annotator.primary else { return nil }
        let scale = screen.backingScaleFactor
        return (Int(screen.frame.width * scale), Int(screen.frame.height * scale))
    }

    func pen(x: UInt16, y: UInt16, state: UInt8) -> Bool {
        let (taken, shown) = locked { () -> (Bool, Bool) in
            (board.pen(x: x, y: y, state: state), board.mode == .annotate)
        }
        // Signing draws nothing here, so its points cost no repaint.
        if taken && shown {
            redraw()
        }
        return taken
    }

    func setColour(_ index: Int) -> Bool {
        locked { board.setColour(index) }
    }

    func run(_ action: ActionId) -> Bool {
        if action == .signSave {
            return saveSignature()
        }
        let taken = locked { board.run(action) }
        if taken {
            redraw()
        }
        return taken
    }

    /// Ends whatever is open and takes the overlay down. Called from the session's own cleanup, so a phone that
    /// vanishes mid-drawing, or the app quitting, leaves no layer over the owner's screen.
    func end() {
        let first = locked { () -> Bool in
            let wasEnded = ended
            ended = true
            board.end()
            return !wasEnded
        }
        guard first else { return }

        signStatusReply = nil
        screenSizeChanged = nil
        if let observer {
            NotificationCenter.default.removeObserver(observer)
        }
        observer = nil
        // Synchronous on the main thread, because a quit would not run a queued block before the process goes.
        if Thread.isMainThread {
            removeOverlay()
        } else {
            DispatchQueue.main.async { [weak self] in self?.removeOverlay() }
        }
    }

    private func saveSignature() -> Bool {
        let strokes: [Stroke]? = locked { board.mode == .sign ? board.strokes : nil }
        guard let strokes else { return false }

        guard !strokes.isEmpty else {
            // Answered at once and before anything is rendered or written: there is no ink, so there is no file
            // and no pasteboard to touch.
            signStatusReply?(SignStatus.empty.rawValue)
            return true
        }

        // Finished strokes never change again, so the copy shares them. Rendered on the main queue like the
        // overlay is; the reply goes out from there too, since the transport's writes are queued, not blocking.
        DispatchQueue.main.async { [weak self] in self?.export(strokes) }
        return true
    }

    /// Every outcome is answered, so the phone never waits on nothing.
    private func export(_ strokes: [Stroke]) {
        // The session ended while this was queued: its strokes are discarded, and there is no one to answer.
        guard !locked({ ended }) else { return }

        let status: SignStatus
        do {
            status = try save(strokes)
        } catch {
            Log.write("A signature could not be saved: \(error.localizedDescription)")
            status = .failed
        }
        signStatusReply?(status.rawValue)
    }

    private func save(_ strokes: [Stroke]) throws -> SignStatus {
        guard let size = screenSize else { return .empty }
        guard let png = SignatureExport.render(strokes, width: size.width, height: size.height) else { return .empty }

        let pictures = FileManager.default.urls(for: .picturesDirectory, in: .userDomainMask).first
        let home = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Pictures")
        let folder = (pictures ?? home).appendingPathComponent("Edgepad")
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyyMMdd-HHmmss"
        _ = try SignatureFile.writeNew(folder: folder, stem: "signature-\(formatter.string(from: Date()))", png: png)

        // PNG keeps the transparency for the apps that read it; TIFF is for the many that only take that.
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        let tiff = NSImage(data: png)?.tiffRepresentation
        let copiedPng = pasteboard.setData(png, forType: .png)
        let copiedTiff = tiff.map { pasteboard.setData($0, forType: .tiff) } ?? false
        return copiedPng || copiedTiff ? .saved : .failed
    }

    /// Asks the main queue for a repaint. One in flight at a time: a finger sends a point every few milliseconds,
    /// a full-screen repaint is slower than that, and only the newest board is worth drawing anyway.
    private func redraw() {
        let queue = locked { () -> Bool in
            guard !redrawQueued else { return false }
            redrawQueued = true
            return true
        }
        if queue {
            DispatchQueue.main.async { [weak self] in self?.drain() }
        }
    }

    /// Draws the newest board, or takes the overlay down when there is nothing to show. The flag is cleared first,
    /// so a point that lands while this draws wins a redraw of its own.
    private func drain() {
        let strokes = locked { () -> [Stroke]? in
            redrawQueued = false
            return snapshot()
        }
        guard let strokes else {
            removeOverlay()
            return
        }
        if overlay == nil, let screen = Annotator.primary {
            overlay = AnnotationOverlay(screen: screen)
        }
        overlay?.present(strokes)
    }

    /// What the overlay should show, or nil when it should not be up. Called with the lock held.
    private func snapshot() -> [Stroke]? {
        guard !ended, board.mode == .annotate else { return nil }
        // The stroke under the finger is still growing, so it alone is copied.
        guard let active = board.active else { return board.strokes }
        return board.strokes + [Stroke(tool: active.tool, colour: active.colour, points: active.points)]
    }

    private func removeOverlay() {
        overlay?.close()
        overlay = nil
    }

    private func locked<T>(_ body: () -> T) -> T {
        lock.lock()
        defer { lock.unlock() }
        return body()
    }
}
