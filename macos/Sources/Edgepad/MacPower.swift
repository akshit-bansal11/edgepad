import AppKit
import EdgepadCore
import Foundation

/// This Mac's power. Sleep is `pmset sleepnow`, which needs no privilege. Restart and shut down ask System Events after
/// a countdown the owner can cancel at the keyboard; they need the Automation permission that
/// `NSAppleEventsUsageDescription` explains. One object for the whole app, so a countdown outlives the phone that
/// started it. HW: verify on device (no hibernate: macOS has no supported way to ask for it).
final class MacPower: PowerPlatform {
    private let gate = NSLock()
    private var counting: (kind: PowerKind, deadline: Date)?

    var caps: PowerCaps { [.sleep, .restart, .shutDown] }

    var pending: (kind: PowerKind, secondsLeft: Int)? {
        gate.withLock {
            guard let counting else { return nil }
            return (counting.kind, max(0, Int(counting.deadline.timeIntervalSinceNow.rounded(.up))))
        }
    }

    func sleep() -> Bool {
        DispatchQueue.global(qos: .utility).async {
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/usr/bin/pmset")
            process.arguments = ["sleepnow"]
            try? process.run()
        }
        return true
    }

    func hibernate() -> Bool {
        false
    }

    func startCountdown(_ kind: PowerKind, seconds: Int, ended: @escaping (Bool) -> Void) -> Bool {
        let started = gate.withLock {
            guard counting == nil else { return false }
            counting = (kind, Date().addingTimeInterval(TimeInterval(seconds)))
            return true
        }
        guard started else { return false }
        DispatchQueue.main.async { [self] in
            show(kind, seconds: seconds, ended: ended)
        }
        return true
    }

    func cancelCountdown() {
        DispatchQueue.main.async {
            NSApp.abortModal()
        }
    }

    func execute(_ kind: PowerKind) -> Bool {
        let verb = kind == .restart ? "restart" : "shut down"
        DispatchQueue.global(qos: .utility).async {
            _ = NowPlayingHub.osascript(["tell application \"System Events\" to \(verb)"])
        }
        return true
    }

    /// Modal, so it cannot be missed. The timer is in the common modes, which includes the modal one. Closing the
    /// window or pressing Cancel ends it with `false`.
    private func show(_ kind: PowerKind, seconds: Int, ended: (Bool) -> Void) {
        let alert = NSAlert()
        let what = kind == .restart ? "restart" : "shut down"
        // plain English until localisation
        alert.messageText = "This Mac will \(what) in \(seconds) seconds."
        alert.informativeText = "You asked your phone to \(what) this Mac. Press Cancel to stop it."
        alert.addButton(withTitle: "Cancel")
        var expired = false
        let timer = Timer(timeInterval: TimeInterval(seconds), repeats: false) { _ in
            expired = true
            NSApp.stopModal()
        }
        RunLoop.main.add(timer, forMode: .common)
        NSApp.activate(ignoringOtherApps: true)
        _ = alert.runModal()
        timer.invalidate()
        alert.window.orderOut(nil)
        gate.withLock { counting = nil }
        ended(expired)
    }
}
