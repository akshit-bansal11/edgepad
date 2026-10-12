import AppKit
import ApplicationServices
import EdgepadCore

/// Posts the phone's input into the user's desktop as `CGEvent`s. macOS silently discards them until the
/// app has the Accessibility permission; that is checked elsewhere. Remembers every key and button it is
/// holding so `releaseAll` can lift them when a session ends. Main thread only.
final class InputInjector: InputSink {
    /// A tuning value, never measured on a real Mac; raise or lower it if scrolling feels wrong.
    private static let pixelsPerNotch = 40.0
    /// Two clicks this close, in points, count as one double click.
    private static let doubleClickRadius = 4.0
    private static let mediaDownFlags: UInt = 0xa00
    private static let mediaUpFlags: UInt = 0xb00
    private static let mediaDownState = 0xa << 8
    private static let mediaUpState = 0xb << 8
    private static let mediaSubtype: Int16 = 8

    private let source = CGEventSource(stateID: .hidSystemState)
    private var heldPhoneKeys: Set<MacKey> = []
    private var heldOwnKeys: Set<MacKey> = []
    private var chordKeys: Set<MacKey> = []
    private var heldButtons: Set<UInt8> = []
    private var clicks = ClickCounter(interval: NSEvent.doubleClickInterval, radius: InputInjector.doubleClickRadius)
    private var scrolling = ScrollAccumulator(pixelsPerNotch: InputInjector.pixelsPerNotch)

    func move(dx: Int, dy: Int) {
        let type: CGEventType
        let button: CGMouseButton
        if heldButtons.contains(0) {
            (type, button) = (.leftMouseDragged, .left)
        } else if heldButtons.contains(1) {
            (type, button) = (.rightMouseDragged, .right)
        } else if heldButtons.contains(2) {
            (type, button) = (.otherMouseDragged, .center)
        } else {
            (type, button) = (.mouseMoved, .left)
        }
        let here = location()
        let target = clamped(CGPoint(x: here.x + Double(dx), y: here.y + Double(dy)))
        guard
            let event = CGEvent(
                mouseEventSource: source, mouseType: type, mouseCursorPosition: target, mouseButton: button)
        else { return }
        event.setIntegerValueField(.mouseEventDeltaX, value: Int64(dx))
        event.setIntegerValueField(.mouseEventDeltaY, value: Int64(dy))
        event.flags = currentFlags()
        event.post(tap: .cghidEventTap)
    }

    func button(_ id: UInt8, down: Bool) {
        let types: (down: CGEventType, up: CGEventType, button: CGMouseButton)
        switch id {
        case 0: types = (.leftMouseDown, .leftMouseUp, .left)
        case 1: types = (.rightMouseDown, .rightMouseUp, .right)
        default: types = (.otherMouseDown, .otherMouseUp, .center)
        }
        let point = location()
        if down {
            heldButtons.insert(id)
            _ = clicks.down(
                button: id, time: ProcessInfo.processInfo.systemUptime, x: point.x, y: point.y)
        } else {
            heldButtons.remove(id)
        }
        guard
            let event = CGEvent(
                mouseEventSource: source, mouseType: down ? types.down : types.up, mouseCursorPosition: point,
                mouseButton: types.button)
        else { return }
        event.setIntegerValueField(.mouseEventClickState, value: Int64(clicks.count))
        event.flags = currentFlags()
        event.post(tap: .cghidEventTap)
    }

    func scroll(dx: Int, dy: Int) {
        let pixels = scrolling.pixels(dx: dx, dy: dy)
        guard pixels.x != 0 || pixels.y != 0 else { return }
        // CGEvent's positive horizontal is left, the wire's is right, so the horizontal sign flips.
        // Negating from Int32.min would trap, so it is clamped by one first.
        guard
            let event = CGEvent(
                scrollWheelEvent2Source: source, units: .pixel, wheelCount: 2, wheel1: pixels.y,
                wheel2: -max(pixels.x, -Int32.max), wheel3: 0)
        else { return }
        event.flags = currentFlags()
        event.post(tap: .cghidEventTap)
    }

    func type(_ text: String) {
        for character in text {
            if character == "\u{8}" {
                chord([.delete])
            } else if character == "\n" {
                chord([.returnKey])
            } else {
                let units = Array(String(character).utf16)
                for down in [true, false] {
                    guard let event = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: down) else {
                        continue
                    }
                    event.keyboardSetUnicodeString(stringLength: units.count, unicodeString: units)
                    // Empty flags: typed text must not become a shortcut because Command happens to be held.
                    event.flags = []
                    event.post(tap: .cghidEventTap)
                }
            }
        }
    }

    func key(_ key: MacKey, down: Bool) {
        if down {
            heldPhoneKeys.insert(key)
        } else {
            heldPhoneKeys.remove(key)
        }
        post(key, down: down)
    }

    func chord(_ keys: [MacKey]) {
        // A key already held (by `hold` or the phone) is left alone: pressing it again is harmless, but its
        // key-up would end the hold on the Mac while this code still believes it is down.
        let pressed = keys.filter { !heldOwnKeys.contains($0) && !heldPhoneKeys.contains($0) }
        for key in pressed {
            chordKeys.insert(key)
            post(key, down: true)
        }
        for key in pressed.reversed() {
            chordKeys.remove(key)
            post(key, down: false)
        }
    }

    func hold(_ key: MacKey) {
        if heldOwnKeys.insert(key).inserted {
            post(key, down: true)
        }
    }

    func release(_ key: MacKey) {
        if heldOwnKeys.remove(key) != nil {
            post(key, down: false)
        }
    }

    func isHeld(_ key: MacKey) -> Bool {
        heldOwnKeys.contains(key)
    }

    func media(_ key: MediaKey) {
        postMedia(key, flags: Self.mediaDownFlags, state: Self.mediaDownState)
        postMedia(key, flags: Self.mediaUpFlags, state: Self.mediaUpState)
    }

    func releaseAll() {
        let keys = heldPhoneKeys.union(heldOwnKeys)
        heldPhoneKeys.removeAll()
        heldOwnKeys.removeAll()
        for key in keys {
            post(key, down: false)
        }
        let buttons = heldButtons
        heldButtons.removeAll()
        for id in buttons {
            button(id, down: false)
        }
    }

    private func postMedia(_ key: MediaKey, flags: UInt, state: Int) {
        NSEvent.otherEvent(
            with: .systemDefined, location: .zero, modifierFlags: NSEvent.ModifierFlags(rawValue: flags),
            timestamp: 0, windowNumber: 0, context: nil, subtype: Self.mediaSubtype,
            data1: (Int(key.rawValue) << 16) | state, data2: -1)?.cgEvent?.post(tap: .cghidEventTap)
    }

    private func post(_ key: MacKey, down: Bool) {
        guard let event = CGEvent(keyboardEventSource: source, virtualKey: key.code, keyDown: down) else { return }
        var flags = currentFlags()
        if key.isArrow {
            flags.formUnion([.maskNumericPad, .maskSecondaryFn])
        } else if key.isFunctionOrNavigation {
            flags.insert(.maskSecondaryFn)
        }
        event.flags = flags
        event.post(tap: .cghidEventTap)
    }

    /// macOS applies a synthetic modifier reliably only when every event carries it, so the flags come
    /// from everything held plus the keys currently down inside a chord.
    private func currentFlags() -> CGEventFlags {
        var flags: CGEventFlags = []
        for key in heldPhoneKeys.union(heldOwnKeys).union(chordKeys) {
            switch key.modifier {
            case .command: flags.insert(.maskCommand)
            case .shift: flags.insert(.maskShift)
            case .control: flags.insert(.maskControl)
            case .option: flags.insert(.maskAlternate)
            case nil: break
            }
        }
        return flags
    }

    private func location() -> CGPoint {
        CGEvent(source: nil)?.location ?? .zero
    }

    /// Keeps the pointer inside the union of the active displays, one point short of the far edges.
    private func clamped(_ point: CGPoint) -> CGPoint {
        var ids = [CGDirectDisplayID](repeating: 0, count: 16)
        var count: UInt32 = 0
        guard CGGetActiveDisplayList(UInt32(ids.count), &ids, &count) == .success, count > 0 else { return point }
        let bounds = ids.prefix(Int(count)).map { CGDisplayBounds($0) }.reduce(CGRect.null) { $0.union($1) }
        return CGPoint(
            x: min(max(point.x, bounds.minX), bounds.maxX - 1),
            y: min(max(point.y, bounds.minY), bounds.maxY - 1))
    }
}
