import AppKit
import EdgepadCore

/// The Mac's half of a Wi-Fi or USB pairing (`PairingPrompt`), the twin of the Windows `PairingPrompt` and the Linux
/// `PairingWindow`: "Pair Pixel? Type the code shown on the phone:", a box for six digits, Pair and Cancel, and the
/// seconds left. It is a plain window, never a modal one: the link it asks for runs on this same main thread.
/// `announce` is told the phone's name as it goes up, for the status line.
final class PairingPanel: PairingPrompt {
    private let announce: (String) -> Void

    init(announce: @escaping (String) -> Void) {
        self.announce = announce
    }

    func show(
        phoneName: String, onTyped: @escaping (String) -> Void, onCancelled: @escaping () -> Void
    ) -> PairingQuestion {
        let name = PhoneLabel.clean(phoneName)
        let shown = name.isEmpty ? "this phone" : name
        let window = PairingWindow(phone: shown, onTyped: onTyped, onCancelled: onCancelled)
        announce(name.isEmpty ? "A phone" : name)
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
        contentRect: NSRect(x: 0, y: 0, width: 400, height: 190), styleMask: [.titled, .closable], backing: .buffered,
        defer: false)
    private let phone: String
    private let ask = NSTextField(wrappingLabelWithString: "Type the code shown on the phone:")
    private let code = NSTextField(frame: NSRect(x: 0, y: 0, width: 160, height: 32))
    private let countdown = NSTextField(labelWithString: "")
    private let cancel = NSButton(title: "Cancel", target: nil, action: nil)
    private let pair = NSButton(title: "Pair", target: nil, action: nil)
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

    init(phone: String, onTyped: @escaping (String) -> Void, onCancelled: @escaping () -> Void) {
        self.phone = phone
        self.onTyped = onTyped
        self.onCancelled = onCancelled
        super.init()

        let title = NSTextField(labelWithString: "Pair \(phone)?")
        title.font = .boldSystemFont(ofSize: 15)
        ask.preferredMaxLayoutWidth = 360
        code.font = .monospacedDigitSystemFont(ofSize: 20, weight: .regular)
        code.placeholderString = "000000"
        code.setAccessibilityLabel("Code shown on the phone")
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
        let stack = NSStackView(views: [title, ask, code, countdown, buttons])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 10
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 20, right: 20)

        panel.title = "Pair a phone"
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
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        panel.makeFirstResponder(code)
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
        tell("Code matched. Tap Yes on \(phone) to finish.", closeSoon: false)
    }

    func mismatched() {
        tell("That code did not match. Nothing was paired.", closeSoon: false)
    }

    func finished(paired: Bool) {
        tell(paired ? "Paired with \(phone)." : "Pairing did not finish.", closeSoon: paired)
    }

    /// Says how the code went in place of the question, with Close in place of Cancel and Pair.
    private func tell(_ message: String, closeSoon: Bool) {
        guard panel.isVisible else { return }
        answered = true
        told = true
        keepAlive = self
        stopTicking()
        ask.stringValue = message
        countdown.stringValue = ""
        code.isEnabled = false
        pair.isHidden = true
        cancel.title = "Close"
        if closeSoon {
            tick = Self.timer(every: Self.lingerSeconds, repeats: false) { [weak self] in self?.close() }
        }
    }

    /// The countdown, rounded up; at zero the window goes, and the pairing's own timer refuses the phone.
    private func count() {
        let left = max(0, Int((deadline - ProcessInfo.processInfo.systemUptime).rounded(.up)))
        countdown.stringValue = "Closes in \(left) s"
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
        countdown.stringValue = "Checking the code"
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
