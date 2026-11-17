import AppKit
import EdgepadCore

/// The Mac's half of a Wi-Fi or USB pairing (`PairingPrompt`), the twin of the Windows `PairingPrompt` and the Linux
/// `PairingWindow`: "Pair Pixel? Type the code shown on the phone:", a box for six digits, Pair and Cancel, and the
/// seconds left. It is a plain window, never a modal one: the link it asks for runs on this same main thread.
/// `announce` is told the phone's name as it goes up, for the status line. `origin` is the screen rectangle of the
/// menu bar item the panel grows from, when it can be found.
final class PairingPanel: PairingPrompt {
    private let origin: () -> NSRect?
    private let announce: (String) -> Void

    init(origin: @escaping () -> NSRect?, announce: @escaping (String) -> Void) {
        self.origin = origin
        self.announce = announce
    }

    func show(
        phoneName: String, onTyped: @escaping (String) -> Void, onCancelled: @escaping () -> Void
    ) -> PairingQuestion {
        show(phoneName: phoneName, source: "", onTyped: onTyped, onCancelled: onCancelled)
    }

    /// `source` is the line that says where the request came from, "from 192.168.1.23, over Wi-Fi".
    func show(
        phoneName: String, source: String, onTyped: @escaping (String) -> Void,
        onCancelled: @escaping () -> Void
    ) -> PairingQuestion {
        let name = PhoneLabel.clean(phoneName)
        let shown = name.isEmpty ? tr("Pair_ThisPhone", "this phone") : name
        let window = PairingWindow(
            phone: shown, source: source, origin: origin(), onTyped: onTyped, onCancelled: onCancelled)
        announce(name.isEmpty ? tr("Net_Phone", "A phone") : name)
        // The question returned is what keeps the window's controller alive until the pairing is settled.
        return window
    }
}

/// One pairing question. Exactly one answer leaves it: the digits on Pair, a cancel on Cancel, Escape or the close
/// box, or none at all when it is withdrawn or runs out, which the pairing's own timer then settles. After the digits
/// it stays up to say how they went, until the owner closes it, or by itself a moment after a pairing that worked.
private final class PairingWindow: NSObject, NSWindowDelegate, NSTextFieldDelegate, PairingQuestion {
    private static let tickSeconds: TimeInterval = 0.25
    /// How long "Paired with Pixel." stays before the window goes by itself.
    private static let lingerSeconds: TimeInterval = 2

    private let panel = NSPanel(
        contentRect: NSRect(x: 0, y: 0, width: 400, height: 260),
        styleMask: [.titled, .closable, .nonactivatingPanel], backing: .buffered,
        defer: false)
    private let phone: String
    private let ask = NSTextField(
        wrappingLabelWithString: tr("Pair_Ask", "Type the pairing code shown on the phone:"))
    private let code = NSTextField(frame: NSRect(x: 0, y: 0, width: 160, height: 32))
    private let countdown = NSTextField(labelWithString: "")
    private let cancel = NSButton(title: tr("Common_Cancel", "Cancel"), target: nil, action: nil)
    private let pair = NSButton(title: tr("Common_Pair", "Pair"), target: nil, action: nil)
    private let deadline = ProcessInfo.processInfo.systemUptime + LinkResponder.ownerTimeoutSeconds
    private let onTyped: (String) -> Void
    private let onCancelled: () -> Void
    /// Set by the first answer, or by withdrawing, so no second one follows.
    private var answered = false
    /// Set once the window says how the code went: it no longer asks, and is no longer withdrawn.
    private var told = false
    private var tick: Timer?
    /// The window holds itself while it shows an outcome: the pairing lets go of its question when it ends.
    private var keepAlive: PairingWindow?

    init(
        phone: String, source: String, origin: NSRect?, onTyped: @escaping (String) -> Void,
        onCancelled: @escaping () -> Void
    ) {
        self.phone = phone
        self.onTyped = onTyped
        self.onCancelled = onCancelled
        super.init()

        let title = NSTextField(labelWithString: tr("Pair_Title", "Pair %1$@?", phone))
        title.font = .boldSystemFont(ofSize: 15)
        let from = NSTextField(labelWithString: source)
        from.textColor = .secondaryLabelColor
        from.isHidden = source.isEmpty
        let warning = NSTextField(
            wrappingLabelWithString: tr(
                "Pair_Warning", "Only pair a phone that is yours. If you did not just start pairing, choose Cancel."))
        warning.preferredMaxLayoutWidth = 360
        ask.preferredMaxLayoutWidth = 360
        code.font = .monospacedDigitSystemFont(ofSize: 20, weight: .regular)
        code.placeholderString = "000000"
        code.setAccessibilityLabel(tr("Pair_CodeLabel", "Code shown on the phone"))
        code.delegate = self
        countdown.textColor = .secondaryLabelColor
        cancel.target = self
        cancel.action = #selector(cancelPressed)
        cancel.keyEquivalent = "\u{1b}"
        pair.target = self
        pair.action = #selector(pairPressed)
        // Return only does anything once six digits are in, since Pair is off until then.
        pair.keyEquivalent = "\r"
        pair.isEnabled = false
        let buttons = NSStackView(views: [cancel, pair])
        buttons.spacing = 12
        let stack = NSStackView(views: [title, from, warning, ask, code, countdown, buttons])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 10
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 20, right: 20)

        panel.title = tr("Pair_Window", "Pair a phone")
        panel.contentView = stack
        panel.isReleasedWhenClosed = false
        // A panel hides when its app is not in front by default; this question must stay until it is answered.
        panel.hidesOnDeactivate = false
        // Raised by a network event while another app is in front, which macOS may not let take the foreground.
        panel.level = .floating
        panel.delegate = self
        panel.center()
        count()
        tick = Self.timer(every: Self.tickSeconds, repeats: true) { [weak self] in self?.count() }
        // Not activated and not key: typing meant for another app must not land in the code box. The owner clicks
        // the box, and the Dock icon bounces to say a question waits.
        reveal(from: origin)
        NSApp.requestUserAttention(.criticalRequest)
        Announce.say(
            tr("Pair_Announce", "Pair %1$@? Closes in %2$@ s.", phone, "\(Int(LinkResponder.ownerTimeoutSeconds))"))
    }

    /// Puts the panel up: it fades in while growing from the menu bar item, or only fades, shorter, when the owner
    /// asked for reduced motion. The panel takes input from its first frame; the animation never waits on it.
    private func reveal(from origin: NSRect?) {
        let final = panel.frame
        let grows = !Motion.reduce && origin != nil
        panel.alphaValue = 0
        if grows, let origin {
            panel.setFrame(Self.seed(at: origin), display: false)
        }
        panel.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { context in
            context.duration = Motion.reduce ? Motion.press : Motion.reveal
            context.timingFunction = CAMediaTimingFunction(name: .easeOut)
            panel.animator().alphaValue = 1
            if grows {
                panel.animator().setFrame(final, display: true)
            }
        }
    }

    /// A small rectangle at the centre of the menu bar item, where the grow starts.
    private static func seed(at item: NSRect) -> NSRect {
        let size = NSSize(width: 40, height: 24)
        let corner = NSPoint(x: item.midX - size.width / 2, y: item.midY - size.height / 2)
        return NSRect(origin: corner, size: size)
    }

    private static func timer(every seconds: TimeInterval, repeats: Bool, _ run: @escaping () -> Void) -> Timer {
        let timer = Timer(timeInterval: seconds, repeats: repeats) { _ in run() }
        RunLoop.main.add(timer, forMode: .common)
        return timer
    }

    /// Takes the window down unanswered. Harmless after an answer, and twice; does nothing once it has said how the
    /// code went.
    func withdraw() {
        guard !told else { return }
        answered = true
        close()
    }

    func matched() {
        tell(
            tr("Pair_Matched", "Code matched. Tap “Codes match” on %1$@ to finish.", phone), closeSoon: false)
    }

    func mismatched() {
        tell(tr("Pair_Mismatched", "That code did not match. Nothing was paired."), closeSoon: false)
    }

    func finished(paired: Bool) {
        tell(
            paired
                ? tr("Pair_Paired", "Paired with %1$@.", phone) : tr("Pair_NotFinished", "Pairing did not finish."),
            closeSoon: paired)
    }

    /// Says how the code went in place of the question, with Close in place of Cancel and Pair.
    private func tell(_ message: String, closeSoon: Bool) {
        guard panel.isVisible else { return }
        answered = true
        told = true
        keepAlive = self
        stopTicking()
        ask.stringValue = message
        Announce.say(message)
        countdown.stringValue = ""
        code.isEnabled = false
        pair.isHidden = true
        cancel.title = tr("Common_Close", "Close")
        if closeSoon {
            tick = Self.timer(every: Self.lingerSeconds, repeats: false) { [weak self] in self?.close() }
        }
    }

    /// The countdown, rounded up; at zero the window goes, and the pairing's own timer refuses the phone.
    private func count() {
        let left = max(0, Int((deadline - ProcessInfo.processInfo.systemUptime).rounded(.up)))
        countdown.stringValue = tr("Pair_ClosesIn", "Closes in %1$@ s", "\(left)")
        if left == 0 {
            withdraw()
        }
    }

    /// Keeps the box to digits and at most six of them, a pasted "482 913" included, and Pair off until all six are in.
    func controlTextDidChange(_ notification: Notification) {
        let digits = String(code.stringValue.filter { $0 >= "0" && $0 <= "9" }.prefix(LinkCrypto.codeDigits))
        if digits != code.stringValue {
            code.stringValue = digits
        }
        pair.isEnabled = digits.count == LinkCrypto.codeDigits
    }

    @objc private func pairPressed() {
        let digits = code.stringValue
        guard !answered, digits.count == LinkCrypto.codeDigits else { return }
        // The window stays: the pairing tells it at once whether the code matched.
        answered = true
        stopTicking()
        code.isEnabled = false
        pair.isEnabled = false
        countdown.stringValue = tr("Pair_Checking", "Checking the code")
        Announce.say(countdown.stringValue)
        onTyped(digits)
    }

    @objc private func cancelPressed() {
        close()
    }

    /// Cancel, the close box and Escape are all the owner saying no, unless an answer was already given.
    func windowWillClose(_ notification: Notification) {
        stopTicking()
        if !answered {
            answered = true
            onCancelled()
        }
        // Let go once the close has finished with it.
        DispatchQueue.main.async { self.keepAlive = nil }
    }

    private func stopTicking() {
        tick?.invalidate()
        tick = nil
    }

    private func close() {
        stopTicking()
        if panel.isVisible {
            panel.close()
        }
    }
}
