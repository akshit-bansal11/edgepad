import AppKit
import EdgepadCore

/// The owner's way into `PinStore`: choose the security level, set the PIN, or give up on a forgotten PIN. Only ever
/// opened by the menu, under `RfcommServer.holdOff()`, because a connected phone is a keyboard and could otherwise
/// answer it, the twin of the Windows `SecurityWindow`.
///
/// Level and PIN are a working copy until Save, so closing the panel any other way changes nothing. Reset is the
/// exception on purpose: it is its own confirmed action, not a setting waiting for Save. No digit is ever logged.
final class SecurityPanel: NSObject, NSTextFieldDelegate {
    private static let width: CGFloat = 380
    private static let levelNames = ["Trust only", "PIN once per phone", "PIN on every connection"]

    private let pin: PinStore
    private let trust: TrustStore
    private let hangUp: () -> Void
    private let forgetPairings: () -> Void

    /// The level the radios show.
    private var level: Int
    /// A PIN chosen here and not yet saved. It lives only as long as this panel.
    private var staged: Pin?
    private var radios: [NSButton] = []
    private let pinState = NSTextField(labelWithString: "")
    private let setPinButton = NSButton(title: "", target: nil, action: nil)

    /// `forgetPairings` forgets the Wi-Fi and USB pairings and tells the owner if it could not, as Forget does.
    init(pin: PinStore, trust: TrustStore, forgetPairings: @escaping () -> Void, hangUp: @escaping () -> Void) {
        self.pin = pin
        self.trust = trust
        self.hangUp = hangUp
        self.forgetPairings = forgetPairings
        level = pin.level
        super.init()
    }

    private var havePin: Bool { staged != nil || pin.hasPin }

    func run() {
        let alert = NSAlert()
        alert.messageText = "Security"
        alert.informativeText = "Every phone is asked for your yes first. Levels 2 and 3 also ask for a six-digit PIN."
        alert.accessoryView = makeBody()
        // Cancel is first, so it is the default: a stray Return changes nothing.
        alert.addButton(withTitle: "Cancel")
        alert.addButton(withTitle: "Save")
        NSApp.activate(ignoringOtherApps: true)
        // A failed save runs the same alert again, so what was chosen is not lost; Reset ends it with `.abort`.
        while true {
            let answer = alert.runModal()
            alert.window.orderOut(nil)
            if answer != .alertSecondButtonReturn || save() {
                return
            }
        }
    }

    private func makeBody() -> NSView {
        radios = Self.levelNames.enumerated().map { index, name in
            let radio = NSButton(
                radioButtonWithTitle: "\(index + 1). \(name)", target: self, action: #selector(choose(_:)))
            radio.tag = index + 1
            return radio
        }
        setPinButton.target = self
        setPinButton.action = #selector(setPinPressed)
        let reset = NSButton(title: "Reset PIN and forget phones…", target: self, action: #selector(confirmReset))
        let warning = NSTextField(
            wrappingLabelWithString: "Saving a new PIN or level makes phones that entered the PIN once enter it again.")
        warning.preferredMaxLayoutWidth = Self.width
        let pinRow = NSStackView(views: [setPinButton, pinState])
        pinRow.spacing = 12
        let stack = NSStackView(views: radios + [pinRow, warning, reset])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 10
        refresh()
        stack.setFrameSize(NSSize(width: Self.width, height: stack.fittingSize.height))
        return stack
    }

    private func refresh() {
        setPinButton.title = havePin ? "Change PIN…" : "Set PIN…"
        pinState.stringValue =
            staged != nil ? "A new PIN will be saved." : pin.hasPin ? "A PIN is set." : "No PIN is set."
        for radio in radios {
            radio.state = radio.tag == level ? .on : .off
        }
    }

    /// A radio was ticked. Levels 2 and 3 with no PIN anywhere would lock every phone out, so the PIN is asked for
    /// right now, and the choice goes back to what it was if the owner does not give one.
    @objc private func choose(_ sender: NSButton) {
        if sender.tag > 1, !havePin, !setPinFlow() {
            refresh()
            return
        }
        level = sender.tag
        refresh()
    }

    @objc private func setPinPressed() {
        _ = setPinFlow()
    }

    /// Asks for a new PIN twice. True when one is staged; nothing is saved until Save.
    private func setPinFlow() -> Bool {
        guard let chosen = askNewPin() else { return false }
        staged = chosen
        refresh()
        return true
    }

    private func askNewPin() -> Pin? {
        let first = secureField("Six digits", label: "New PIN")
        let again = secureField("Again", label: "New PIN again")
        let problem = NSTextField(labelWithString: "")
        problem.textColor = .systemRed
        let stack = NSStackView(views: [first, again, problem])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 8
        stack.setFrameSize(NSSize(width: 220, height: stack.fittingSize.height))

        let alert = NSAlert()
        alert.messageText = "New PIN"
        alert.informativeText = "Six digits, typed twice."
        alert.accessoryView = stack
        alert.addButton(withTitle: "Cancel")
        alert.addButton(withTitle: "Use this PIN")
        alert.window.initialFirstResponder = first
        NSApp.activate(ignoringOtherApps: true)
        while true {
            let answer = alert.runModal()
            alert.window.orderOut(nil)
            guard answer == .alertSecondButtonReturn else { return nil }
            let typed = first.stringValue
            if typed != again.stringValue {
                problem.stringValue = "The two PINs do not match."
            } else if let chosen = Self.pin(from: typed) {
                return chosen
            } else {
                problem.stringValue = "Use exactly six digits."
            }
            first.stringValue = ""
            again.stringValue = ""
        }
    }

    private func secureField(_ placeholder: String, label: String) -> NSSecureTextField {
        let field = NSSecureTextField(frame: NSRect(x: 0, y: 0, width: 220, height: 24))
        field.placeholderString = placeholder
        field.setAccessibilityLabel(label)
        field.delegate = self
        return field
    }

    /// Keeps a field to digits and at most six of them as they are typed.
    func controlTextDidChange(_ notification: Notification) {
        guard let field = notification.object as? NSTextField else { return }
        let digits = String(field.stringValue.filter { $0 >= "0" && $0 <= "9" }.prefix(Pin.length))
        if digits != field.stringValue {
            field.stringValue = digits
        }
    }

    /// Nil unless `text` is exactly six ASCII digits.
    private static func pin(from text: String) -> Pin? {
        let bytes = Array(text.utf8)
        guard bytes.allSatisfy({ (48...57).contains($0) }) else { return nil }
        return Pin(digits: bytes.map { $0 - 48 })
    }

    /// True when everything chosen is stored, or nothing was changed.
    private func save() -> Bool {
        if staged == nil && level == pin.level {
            return true
        }
        // The PIN first: setLevel refuses level 2 or 3 without one. setPin also empties the verified list, so the
        // level needs its own write only when it changed.
        if let staged {
            guard pin.setPin(staged) else {
                fail("The PIN could not be saved, so nothing was changed. The log has the reason.")
                return false
            }
            self.staged = nil
        }
        if level != pin.level && !pin.setLevel(level) {
            refresh()
            fail("The level could not be saved, so the previous level still applies. The log has the reason.")
            return false
        }
        return true
    }

    @objc private func confirmReset() {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "Edgepad"
        alert.informativeText =
            "Delete the PIN, go back to level 1 and forget every trusted phone? Each phone will have to be trusted "
            + "here again. A forgotten PIN cannot be recovered, only reset."
        // Cancel is first, so it is the default: a stray Return resets nothing.
        alert.addButton(withTitle: "Cancel")
        alert.addButton(withTitle: "Reset")
        let answer = alert.runModal()
        alert.window.orderOut(nil)
        guard answer == .alertSecondButtonReturn else { return }
        // Trust first, and the PIN only once the phones are gone: a PIN deleted while they stay trusted would start
        // the next run at level 1, where a stolen trusted phone gets in with nothing to answer. The phone is hung up
        // on either way, so a half-done reset leaves none connected.
        let forgotten = trust.forget()
        // Every way of forgetting phones forgets the network pairings too, whatever became of the trust file: a paired
        // phone would otherwise resume its link and meet only the trust question.
        forgetPairings()
        let pinGone = forgotten && pin.reset()
        hangUp()
        if !forgotten {
            fail(
                "Nothing was reset: the trusted phones could not be forgotten, so the PIN and the level were left as "
                    + "they were. No phone can connect while Edgepad stays running, but those phones will be trusted "
                    + "again, behind the same PIN, the next time it starts. Close whatever is holding the file and "
                    + "choose Reset again, or delete it yourself:\n\n\(trust.filePath)")
        } else if !pinGone {
            fail(
                "The trusted phones were forgotten, but the PIN could not be reset: a settings file could not be "
                    + "removed, so the old PIN and level may still apply. Each phone has to be trusted here again. "
                    + "Close whatever is holding the file and choose Reset again, or delete security.txt and "
                    + "pin-verified.txt yourself from:\n\n\(pin.folder)")
        }
        NSApp.stopModal(withCode: .abort)
    }

    private func fail(_ message: String) {
        let alert = NSAlert()
        alert.alertStyle = .critical
        alert.messageText = "Edgepad"
        alert.informativeText = message
        alert.addButton(withTitle: "OK")
        _ = alert.runModal()
        alert.window.orderOut(nil)
    }
}
