import AppKit
import EdgepadCore

/// The Mac's card for a file moving to or from the phone: bottom-right of the main screen, never takes focus, with
/// the app mark, "Sending to Pixel", the file, its place in the batch and the speed, a 4 pt bar, and Pause or Resume,
/// Stop and Close. Close only hides it, the transfer goes on, and the menu's Transfers row brings it back. It
/// replaces the menu's status line for transfers; the result of a file stays on it for a few seconds, then it goes.
/// Reduce Motion keeps the fade (nothing here ever slides) and shortens it.
final class TransferPanel: NSObject {
    private static let width: CGFloat = 380
    private static let height: CGFloat = 78
    private static let margin: CGFloat = 16
    /// A file that ended is waited on this long before the card says so, so the next file of a batch replaces it
    /// without a flash.
    private static let settleSeconds: TimeInterval = 0.3
    private static let lingerSeconds: TimeInterval = 3
    private static let lingerFailedSeconds: TimeInterval = 6
    private static let renderEvery: TimeInterval = 0.25

    /// Pause, resume and stop, told whether this Mac is the sender. Set by the app once it has a host.
    var onPause: (Bool) -> Bool = { _ in false }
    var onResume: (Bool) -> Bool = { _ in false }
    var onStop: (Bool) -> Void = { _ in }

    /// Whether a file is in flight, for the menu's Transfers row.
    var running: Bool { model.running }

    private var model = TransferPanelModel()
    private var phone = ""
    private var result: String?
    private var resultFailed = false
    /// The owner pressed Close; the card stays away until the batch is over or the menu asks for it.
    private var dismissed = false
    /// Moves on with every tick and every result, so a timer armed for an earlier state cannot act on a later one.
    private var token = 0
    private var lastRender: TimeInterval = 0

    private let panel: NSPanel
    private let title = NSTextField(labelWithString: "")
    private let detail = NSTextField(labelWithString: "")
    private let bar = TransferBar()
    private let pauseButton = NSButton()
    private let stopButton = NSButton()
    private let closeButton = NSButton()

    override init() {
        panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: Self.width, height: Self.height),
            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        super.init()
        panel.isFloatingPanel = true
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.contentView = buildContent()
    }

    // MARK: Fed by the session

    /// `count` more files are on their way to the phone.
    func expect(files count: Int) {
        model.expect(files: count)
    }

    /// One tick from the sender or receiver; `active` false is the file ending.
    func progress(sending: Bool, phone: String, name: String, done: UInt64, size: UInt64, active: Bool) {
        let now = ProcessInfo.processInfo.systemUptime
        let fresh = !model.running || model.name != name || model.sending != sending
        model.update(sending: sending, name: name, done: done, size: size, active: active, now: now)
        self.phone = phone
        token += 1
        guard active else {
            settle()
            return
        }
        result = nil
        render(force: fresh)
    }

    /// Either end started or stopped holding the file.
    func held(_ value: Bool) {
        guard model.running else { return }
        model.setHeld(value, now: ProcessInfo.processInfo.systemUptime)
        render(force: true)
    }

    /// How a file ended, in words, for the few seconds the card stays.
    func finished(_ line: String, failed: Bool) {
        result = line
        resultFailed = failed
        // A closed card still tells the owner what went wrong.
        if failed { dismissed = false }
        if !model.running {
            token += 1
            settle()
        }
    }

    /// The menu's Transfers row.
    func showAgain() {
        dismissed = false
        render(force: true)
    }

    // MARK: Ending

    private func settle() {
        let mine = token
        DispatchQueue.main.asyncAfter(deadline: .now() + Self.settleSeconds) { [weak self] in
            guard let self, self.token == mine, !self.model.running else { return }
            guard self.result != nil else {
                self.finishBatch()
                return
            }
            self.render(force: true)
            let linger = self.resultFailed ? Self.lingerFailedSeconds : Self.lingerSeconds
            DispatchQueue.main.asyncAfter(deadline: .now() + linger) { [weak self] in
                guard let self, self.token == mine, !self.model.running else { return }
                self.finishBatch()
            }
        }
    }

    private func finishBatch() {
        result = nil
        dismissed = false
        hide()
    }

    // MARK: Drawing

    private func render(force: Bool) {
        let now = ProcessInfo.processInfo.systemUptime
        guard !dismissed, force || now - lastRender >= Self.renderEvery else { return }
        lastRender = now
        if let result {
            title.stringValue = result
            detail.stringValue = ""
            detail.isHidden = true
            bar.fraction = resultFailed ? model.fraction : 1
            bar.failed = resultFailed
            pauseButton.isHidden = true
            stopButton.isHidden = true
        } else {
            title.stringValue =
                model.sending
                ? tr("Xfer_PanelSending", "Sending to %1$@", phone)
                : tr("Xfer_PanelReceiving", "Receiving from %1$@", phone)
            detail.stringValue = detailLine()
            detail.isHidden = false
            bar.fraction = model.fraction
            bar.failed = false
            pauseButton.isHidden = false
            stopButton.isHidden = false
            let label = model.held ? tr("Xfer_Resume", "Resume") : tr("Xfer_Pause", "Pause")
            pauseButton.image = Self.symbol(model.held ? "play.fill" : "pause.fill")
            pauseButton.toolTip = label
            pauseButton.setAccessibilityLabel(label)
        }
        show()
    }

    private func detailLine() -> String {
        let file = StatusText.middleTruncated(model.name)
        if model.held {
            return tr("Xfer_PanelHeld", "%1$@ · Paused", file)
        }
        let count = model.total > 1
        let speed = model.bytesPerSecond > 0 ? Self.bytes(Int64(model.bytesPerSecond)) : nil
        switch (count, speed) {
        case (true, let speed?):
            return tr(
                "Xfer_PanelDetail", "%1$@ · %2$@ of %3$@ · %4$@/s", file, "\(model.number)", "\(model.total)", speed)
        case (true, nil):
            return tr("Xfer_PanelCount", "%1$@ · %2$@ of %3$@", file, "\(model.number)", "\(model.total)")
        case (false, let speed?):
            return tr("Xfer_PanelSpeed", "%1$@ · %2$@/s", file, speed)
        case (false, nil):
            return file
        }
    }

    private static func bytes(_ count: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: count, countStyle: .file)
    }

    private func show() {
        if !panel.isVisible {
            place()
            panel.alphaValue = 0
        }
        panel.orderFrontRegardless()
        fade(to: 1, completion: nil)
    }

    private func hide() {
        guard panel.isVisible else { return }
        fade(to: 0) { [weak self] in
            guard let self, self.panel.alphaValue == 0 else { return }
            self.panel.orderOut(nil)
        }
    }

    private func fade(to alpha: CGFloat, completion: (() -> Void)?) {
        guard panel.alphaValue != alpha else {
            completion?()
            return
        }
        NSAnimationContext.runAnimationGroup(
            { context in
                context.duration = Motion.reduce ? Motion.press / 2 : (alpha == 0 ? Motion.press : Motion.reveal)
                panel.animator().alphaValue = alpha
            }, completionHandler: completion)
    }

    private func place() {
        guard let area = (NSScreen.main ?? NSScreen.screens.first)?.visibleFrame else { return }
        panel.setFrameOrigin(NSPoint(x: area.maxX - Self.width - Self.margin, y: area.minY + Self.margin))
    }

    // MARK: Buttons

    @objc private func pauseTapped() {
        let sending = model.sending
        _ = model.held ? onResume(sending) : onPause(sending)
    }

    @objc private func stopTapped() {
        onStop(model.sending)
    }

    @objc private func closeTapped() {
        dismissed = true
        hide()
    }

    // MARK: Layout

    private static func symbol(_ name: String) -> NSImage {
        let image = NSImage(systemSymbolName: name, accessibilityDescription: nil) ?? NSImage()
        return image.withSymbolConfiguration(NSImage.SymbolConfiguration(pointSize: 13, weight: .semibold)) ?? image
    }

    private func style(_ button: NSButton, symbol: String, label: String, tint: NSColor, action: Selector) {
        button.image = Self.symbol(symbol)
        button.imagePosition = .imageOnly
        button.isBordered = false
        button.contentTintColor = tint
        button.target = self
        button.action = action
        button.toolTip = label
        button.setAccessibilityLabel(label)
        button.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            button.widthAnchor.constraint(equalToConstant: 28), button.heightAnchor.constraint(equalToConstant: 28),
        ])
    }

    private func buildContent() -> NSView {
        let card = TransferCard(frame: NSRect(x: 0, y: 0, width: Self.width, height: Self.height))

        let mark = NSImageView(image: MenuBarMark.image())
        mark.contentTintColor = .labelColor
        mark.setAccessibilityElement(false)

        title.font = .systemFont(ofSize: 13, weight: .semibold)
        title.textColor = .labelColor
        title.lineBreakMode = .byTruncatingTail
        detail.font = .systemFont(ofSize: 11)
        detail.textColor = .secondaryLabelColor
        detail.lineBreakMode = .byTruncatingMiddle

        style(
            pauseButton, symbol: "pause.fill", label: tr("Xfer_Pause", "Pause"), tint: .labelColor,
            action: #selector(pauseTapped))
        style(
            stopButton, symbol: "stop.fill", label: tr("Xfer_Stop", "Stop"), tint: .systemRed,
            action: #selector(stopTapped))
        style(
            closeButton, symbol: "xmark", label: tr("Common_Close", "Close"), tint: .secondaryLabelColor,
            action: #selector(closeTapped))

        let buttons = NSStackView(views: [pauseButton, stopButton, closeButton])
        buttons.orientation = .horizontal
        buttons.spacing = 2

        for view in [mark, title, detail, buttons, bar] {
            view.translatesAutoresizingMaskIntoConstraints = false
            card.addSubview(view)
        }
        // A title long enough to meet the buttons gives way, not the buttons.
        title.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        detail.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        NSLayoutConstraint.activate([
            mark.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 14),
            mark.topAnchor.constraint(equalTo: card.topAnchor, constant: 14),
            mark.widthAnchor.constraint(equalToConstant: 18),
            mark.heightAnchor.constraint(equalToConstant: 18),
            title.leadingAnchor.constraint(equalTo: mark.trailingAnchor, constant: 10),
            title.topAnchor.constraint(equalTo: card.topAnchor, constant: 12),
            title.trailingAnchor.constraint(lessThanOrEqualTo: buttons.leadingAnchor, constant: -8),
            detail.leadingAnchor.constraint(equalTo: title.leadingAnchor),
            detail.topAnchor.constraint(equalTo: title.bottomAnchor, constant: 2),
            detail.trailingAnchor.constraint(lessThanOrEqualTo: buttons.leadingAnchor, constant: -8),
            buttons.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -8),
            buttons.topAnchor.constraint(equalTo: card.topAnchor, constant: 8),
            bar.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 14),
            bar.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -14),
            bar.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -14),
            bar.heightAnchor.constraint(equalToConstant: 4),
        ])
        return card
    }
}

/// The three palettes of the card: the laptop's dark (card `#1B1C20`, line `#2B2D33`, accent `#4DA3FF`) and, in a
/// light appearance, the system's own colours with the accent `#0068D6`.
private enum TransferColors {
    static func rgb(_ value: UInt32) -> NSColor {
        NSColor(
            srgbRed: CGFloat((value >> 16) & 0xFF) / 255, green: CGFloat((value >> 8) & 0xFF) / 255,
            blue: CGFloat(value & 0xFF) / 255, alpha: 1)
    }

    static func pick(dark: NSColor, light: NSColor) -> NSColor {
        NSColor(name: nil) { appearance in
            appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? dark : light
        }
    }

    static let card = pick(dark: rgb(0x1B1C20), light: .windowBackgroundColor)
    static let line = pick(dark: rgb(0x2B2D33), light: .separatorColor)
    static let track = pick(dark: rgb(0x2B2D33), light: NSColor.black.withAlphaComponent(0.1))
    static let accent = pick(dark: rgb(0x4DA3FF), light: rgb(0x0068D6))
}

/// The card's face: fill, one-point line and a 12 pt radius.
private final class TransferCard: NSView {
    override var wantsUpdateLayer: Bool { true }

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.cornerRadius = 12
        layer?.borderWidth = 1
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("not built from a nib")
    }

    override func updateLayer() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            layer?.backgroundColor = TransferColors.card.cgColor
            layer?.borderColor = TransferColors.line.cgColor
        }
    }
}

/// A 4 pt bar, full width, filled to `fraction`; red when `failed`.
private final class TransferBar: NSView {
    var fraction: Double = 0 {
        didSet { if fraction != oldValue { needsDisplay = true } }
    }
    var failed = false {
        didSet { if failed != oldValue { needsDisplay = true } }
    }

    override func draw(_ dirtyRect: NSRect) {
        let radius = bounds.height / 2
        TransferColors.track.setFill()
        NSBezierPath(roundedRect: bounds, xRadius: radius, yRadius: radius).fill()
        var filled = bounds
        filled.size.width = bounds.width * CGFloat(min(1, max(0, fraction)))
        guard filled.width > 0 else { return }
        (failed ? NSColor.systemRed : TransferColors.accent).setFill()
        NSBezierPath(roundedRect: filled, xRadius: radius, yRadius: radius).fill()
    }
}
