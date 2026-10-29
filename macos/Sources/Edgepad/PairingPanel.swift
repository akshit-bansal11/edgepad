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
    ) -> () -> Void {
        let name = PhoneLabel.clean(phoneName)
        let shown = name.isEmpty ? "this phone" : name
        let window = PairingWindow(phone: shown, onTyped: onTyped, onCancelled: onCancelled)
        announce(name.isEmpty ? "A phone" : name)
        // The returned closure is what keeps the window's controller alive until the pairing is settled.
        return { window.withdraw() }
    }
}

/// One pairing question. Exactly one answer leaves it: the digits on Pair, a cancel on Cancel, Escape or the close
/// box, or none at all when it is withdrawn or runs out, which the pairing's own timer then settles.
private final class PairingWindow: NSObject, NSWindowDelegate, NSTextFieldDelegate {
    private static let tickSeconds: TimeInterval = 0.25

    private let panel = NSPanel(
        contentRect: NSRect(x: 0, y: 0, width: 400, height: 190), styleMask: [.titled, .closable], backing: .buffered,
        defer: false)
    private let code = NSTextField(frame: NSRect(x: 0, y: 0, width: 160, height: 32))
    private let countdown = NSTextField(labelWithString: "")
    private let pair = NSButton(title: "Pair", target: nil, action: nil)
    private let deadline = ProcessInfo.processInfo.systemUptime + LinkResponder.ownerTimeoutSeconds
    private let onTyped: (String) -> Void
    private let onCancelled: () -> Void
    /// Set by the first answer, or by withdrawing, so no second one follows.
    private var answered = false
    private var tick: Timer?

    init(phone: String, onTyped: @escaping (String) -> Void, onCancelled: @escaping () -> Void) {
        self.onTyped = onTyped
        self.onCancelled = onCancelled
        super.init()

        let title = NSTextField(labelWithString: "Pair \(phone)?")
        title.font = .boldSystemFont(ofSize: 15)
        let ask = NSTextField(labelWithString: "Type the code shown on the phone:")
        code.font = .monospacedDigitSystemFont(ofSize: 20, weight: .regular)
        code.placeholderString = "000000"
        code.setAccessibilityLabel("Code shown on the phone")
        code.delegate = self
        countdown.textColor = .secondaryLabelColor
        let cancel = NSButton(title: "Cancel", target: self, action: #selector(cancelPressed))
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
        let timer = Timer(timeInterval: Self.tickSeconds, repeats: true) { [weak self] _ in self?.count() }
        RunLoop.main.add(timer, forMode: .common)
        tick = timer
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        panel.makeFirstResponder(code)
    }

    /// Takes the window down unanswered. Harmless after an answer, and twice.
    func withdraw() {
        answered = true
        close()
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
        answered = true
        close()
        onTyped(digits)
    }

    @objc private func cancelPressed() {
        close()
    }

    /// Cancel, the close box and Escape are all the owner saying no, unless an answer was already given.
    func windowWillClose(_ notification: Notification) {
        tick?.invalidate()
        tick = nil
        if !answered {
            answered = true
            onCancelled()
        }
    }

    private func close() {
        tick?.invalidate()
        tick = nil
        if panel.isVisible {
            panel.close()
        }
    }
}
