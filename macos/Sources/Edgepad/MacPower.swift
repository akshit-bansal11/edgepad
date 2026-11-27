import AppKit
import ApplicationServices
import EdgepadCore
import Foundation

/// This Mac's power. Sleep is `pmset sleepnow`, which needs no privilege. Restart and shut down ask System Events after
/// a countdown the owner can cancel at the keyboard; they need the Automation permission that
/// `NSAppleEventsUsageDescription` explains. One object for the whole app, so a countdown outlives the phone that
/// started it. HW: verify on device (no hibernate: macOS has no supported way to ask for it).
final class MacPower: PowerPlatform {
    private let gate = NSLock()
    private var counting: (kind: PowerKind, deadline: Date)?
    /// Main thread only: the alert's window while it is up, and whether a cancel waits for it to be on top.
    private var alertWindow: NSWindow?
    private var cancelRequested = false

    /// Restart and shut down are offered only when System Events would take our Apple Events without asking, so a
    /// countdown that ends in nothing is never offered. HW: verify on device.
    let caps: PowerCaps = {
        var caps: PowerCaps = [.sleep]
        let target = NSAppleEventDescriptor(bundleIdentifier: "com.apple.systemevents")
        if let desc = target.aeDesc,
            AEDeterminePermissionToAutomateTarget(desc, typeWildCard, typeWildCard, false) == noErr
        {
            caps.formUnion([.restart, .shutDown])
        }
        return caps
    }()

    var pending: (kind: PowerKind, secondsLeft: Int)? {
        gate.withLock {
            guard let counting else { return nil }
            return (counting.kind, Self.secondsLeft(counting.deadline))
        }
    }

    private static func secondsLeft(_ deadline: Date) -> Int {
        max(0, Int(deadline.timeIntervalSinceNow.rounded(.up)))
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
        DispatchQueue.main.async { [self] in
            cancelRequested = true
            endIfOnTop(NSApp.abortModal)
        }
    }

    /// Ends the alert's loop only while it is the modal on top: a trust prompt over it keeps its own, and the next tick
    /// tries again.
    private func endIfOnTop(_ end: () -> Void) {
        if let alertWindow, NSApp.modalWindow === alertWindow {
            end()
        }
    }

    func execute(_ kind: PowerKind) -> Bool {
        let verb = kind == .restart ? "restart" : "shut down"
        DispatchQueue.global(qos: .utility).async {
            _ = NowPlayingHub.osascript(["tell application \"System Events\" to \(verb)"])
        }
        return true
    }

    /// Modal, so it cannot be missed. A one second timer in the common modes (which include the modal one) ticks the
    /// text down and stops the loop at zero. The answer is the loop's own: `.stop` is the expiry, anything else
    /// (Cancel, a cancel from the phone, the window closed) is a cancel. The deadline is the one in `counting`, which a
    /// wake from sleep moves back to the full time, because the clock kept counting while the Mac slept.
    private func show(_ kind: PowerKind, seconds: Int, ended: (Bool) -> Void) {
        let alert = NSAlert()
        let what = kind == .restart ? "restart" : "shut down"
        // plain English until localisation
        alert.messageText = Self.message(what, seconds)
        alert.informativeText = "You asked your phone to \(what) this Mac. Press Cancel to stop it."
        alert.addButton(withTitle: "Cancel")
        alertWindow = alert.window
        cancelRequested = false
        let timer = Timer(timeInterval: 1, repeats: true) { [self] _ in
            let left = pending?.secondsLeft ?? 0
            alert.messageText = Self.message(what, left)
            if cancelRequested {
                endIfOnTop(NSApp.abortModal)
            } else if left == 0 {
                endIfOnTop(NSApp.stopModal)
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        let wake = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main
        ) { [self] _ in
            gate.withLock { counting?.deadline = Date().addingTimeInterval(TimeInterval(seconds)) }
            alert.messageText = Self.message(what, seconds)
            PowerControl.countdownRestarted()
        }
        NSApp.activate(ignoringOtherApps: true)
        let answer = alert.runModal()
        timer.invalidate()
        NSWorkspace.shared.notificationCenter.removeObserver(wake)
        alert.window.orderOut(nil)
        alertWindow = nil
        gate.withLock { counting = nil }
        ended(answer == .stop)
    }

    private static func message(_ what: String, _ seconds: Int) -> String {
        "This Mac will \(what) in \(seconds) second\(seconds == 1 ? "" : "s")."
    }
}
