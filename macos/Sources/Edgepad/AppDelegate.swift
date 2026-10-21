import AppKit
import ApplicationServices
import EdgepadCore
import Foundation
import ServiceManagement

/// The whole UI: a menu-bar item whose menu is rebuilt each time it opens, so it never shows a stale state.
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private static let documentationUrl = "https://edgepad.akshitbansal.dev/docs"
    private static let accessibilityPaneUrl =
        // Still resolves on macOS 27, where the pane is Device Control and Data Access.
        "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility"
    /// No answer to the trust question within this is a no: a Mac left alone must not end up trusting whichever
    /// paired phone happened to try it.
    private static let trustPromptSeconds: TimeInterval = 60
    /// A click already in flight when the question appears must not land on "Trust".
    private static let trustButtonDelaySeconds: TimeInterval = 2
    /// How long before Bluetooth is tried again after it failed to start.
    private static let bluetoothRetrySeconds: TimeInterval = 30

    private let runningVersion = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0.0.0"
    private let trust = TrustStore.forCurrentUser()
    private var statusItem: NSStatusItem?
    private var server: RfcommServer?
    private var status = "Starting"
    private var newerVersion: Version?
    /// The menu-bar item may stay clickable under the app-modal alert, and a second Forget would lift the hold while
    /// the first was still open.
    private var forgetting = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Newest wins, as on Windows where a newer exe asks the running one to quit.
        if let bundleId = Bundle.main.bundleIdentifier {
            for other in NSRunningApplication.runningApplications(withBundleIdentifier: bundleId)
            where other != NSRunningApplication.current {
                _ = other.terminate()
            }
        }

        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let image = NSImage(systemSymbolName: "macbook.and.iphone", accessibilityDescription: "Edgepad") {
            image.isTemplate = true
            item.button?.image = image
        } else {
            item.button?.title = "Edgepad"
        }
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        statusItem = item

        // Without them macOS silently discards every event this app posts.
        Log.write(
            "Input access: accessibility=\(AXIsProcessTrusted()), eventPosting=\(CGPreflightPostEventAccess())"
        )
        _ = requestAccessibility()

        let server = RfcommServer(
            trust: trust,
            askTrust: { [weak self] in self?.askTrust($0) ?? false },
            onStatus: { [weak self] in self?.setStatus($0) })
        self.server = server
        startServer()

        checkForUpdate()
        Timer.scheduledTimer(withTimeInterval: UpdateCheck.every, repeats: true) { [weak self] _ in
            self?.checkForUpdate()
        }
    }

    /// With Start at login the app can launch before Bluetooth is ready or its permission prompt is answered, so a
    /// failed start is tried again rather than left for good.
    private func startServer() {
        if server?.start() == false {
            setStatus("Bluetooth is unavailable; see the log")
            Timer.scheduledTimer(withTimeInterval: Self.bluetoothRetrySeconds, repeats: false) { [weak self] _ in
                self?.startServer()
            }
        }
    }

    /// Releases everything the phone was holding down before the app goes.
    func applicationWillTerminate(_ notification: Notification) {
        server?.stop()
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        menu.addItem(disabled(status))
        if let version = newerVersion {
            menu.addItem(item("Update available: \(version)", #selector(openRelease)))
        }
        // Newer macOS tracks event posting separately from the Accessibility trust, under the
        // same pane. Either one missing means input goes nowhere.
        if !AXIsProcessTrusted() || !CGPreflightPostEventAccess() {
            menu.addItem(item("Allow Edgepad to control this Mac…", #selector(allowAccessibility)))
        }
        menu.addItem(.separator())
        let login = item("Start at login", #selector(toggleLogin))
        login.state = SMAppService.mainApp.status == .enabled ? .on : .off
        menu.addItem(login)
        menu.addItem(item("Open log", #selector(openLog)))
        menu.addItem(item("Documentation", #selector(openDocumentation)))
        let forget = item("Forget trusted phones", #selector(confirmForget))
        forget.isEnabled = trust.canForget
        menu.addItem(forget)
        menu.addItem(.separator())
        menu.addItem(item("Quit Edgepad", #selector(quit)))
        menu.addItem(disabled("Edgepad \(runningVersion)"))
    }

    private func setStatus(_ text: String) {
        status = text
    }

    private func item(_ title: String, _ action: Selector) -> NSMenuItem {
        let row = NSMenuItem(title: title, action: action, keyEquivalent: "")
        row.target = self
        return row
    }

    private func disabled(_ title: String) -> NSMenuItem {
        let row = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        row.isEnabled = false
        return row
    }

    private func requestAccessibility() -> Bool {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        return AXIsProcessTrustedWithOptions([key: true] as CFDictionary)
    }

    private func open(_ text: String) {
        guard let url = URL(string: text) else { return }
        NSWorkspace.shared.open(url)
    }

    private func checkForUpdate() {
        UpdateCheck.latest { [weak self] version in
            DispatchQueue.main.async {
                guard let self, let version, UpdateCheck.isNewer(version, than: self.runningVersion) else { return }
                if self.newerVersion != version {
                    Log.write("Edgepad \(version) is available")
                }
                self.newerVersion = version
            }
        }
    }

    @objc private func openRelease() {
        open(UpdateCheck.latestUrl)
    }

    @objc private func allowAccessibility() {
        // Newer macOS tracks event posting separately from the Accessibility trust. Asking for
        // both at once can stack two identical dialogs, so the second is only requested once the
        // first is granted; the menu item stays until both are. On macOS 27 neither call prompts
        // and the app is never listed on its own, so the pane is opened for a manual add: the +
        // button, Edgepad switched on including its Events switch, if shown.
        if !AXIsProcessTrusted() {
            _ = requestAccessibility()
        } else {
            _ = CGRequestPostEventAccess()
        }
        open(Self.accessibilityPaneUrl)
    }

    @objc private func toggleLogin() {
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            Log.write("Could not change start at login: \(error.localizedDescription)")
        }
    }

    @objc private func openLog() {
        Log.write("Log opened")
        NSWorkspace.shared.open(URL(fileURLWithPath: Log.filePath))
    }

    @objc private func openDocumentation() {
        open(Self.documentationUrl)
    }

    /// Asked first because the menu cannot undo it: every phone after it has to be trusted again. The connected
    /// phone, and any still being asked about, is hung up on, and connections are refused, while the question is
    /// open. If the trust file cannot be removed the owner is told, and the store refuses every phone until a later
    /// Forget succeeds.
    @objc private func confirmForget() {
        guard !forgetting else { return }
        forgetting = true
        // A connected phone is a keyboard and could press Return or Escape on this dialog, so the owner could
        // never forget it. Hanging up is not enough, since the phone is still trusted and reconnects within
        // seconds, so nothing may connect until the question is answered.
        server?.holdOff()
        defer {
            server?.resume()
            forgetting = false
        }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "Edgepad"
        let phones = trust.trustedPhones
        let which = phones.isEmpty ? "the trusted phones" : "every trusted phone (\(phones.joined(separator: ", ")))"
        alert.informativeText =
            "Forget \(which)? Each phone, this one included, will have to be trusted here again before it can use "
            + "this Mac."
        // Cancel is first, so it is the default: a stray Return forgets nothing.
        alert.addButton(withTitle: "Cancel")
        alert.addButton(withTitle: "Forget")
        NSApp.activate(ignoringOtherApps: true)
        let answer = alert.runModal()
        alert.window.orderOut(nil)
        if answer == .alertSecondButtonReturn {
            let forgotten = trust.forget()
            // Started before the hold ends (the defer above), so there is no instant in which a connection would
            // be taken.
            server?.stayQuiet()
            if !forgotten {
                let failure = NSAlert()
                failure.alertStyle = .critical
                failure.messageText = "Edgepad"
                failure.informativeText =
                    "The trusted phones could not be forgotten: their file could not be removed or emptied. No phone "
                    + "can connect while Edgepad stays running, but those phones will be trusted again when Edgepad "
                    + "starts. Choose Forget trusted phones again, or delete the file yourself:\n\n\(trust.filePath)"
                failure.addButton(withTitle: "OK")
                _ = failure.runModal()
                failure.window.orderOut(nil)
            }
        }
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }

    /// Asks whether to trust `phone`, and waits. Called by the session while it holds the phone's HELLO_ACK.
    private func askTrust(_ phone: String) -> Bool {
        setStatus("Asking whether to trust \(phone)")
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "Trust this phone?"
        let whatIsTrusted =
            phone.contains("\"")
            ? "The name in quotes is only what the phone calls itself; the address is what will be trusted."
            : "The address is what will be trusted."
        alert.informativeText =
            "\(phone) is asking to control this Mac's pointer and keyboard. \(whatIsTrusted) "
            + "Trust it only if it is yours. With no answer in \(Int(Self.trustPromptSeconds)) seconds, "
            + "it is not trusted."
        // Don't Trust is first, so it is the default: a stray Return trusts nothing.
        alert.addButton(withTitle: "Don't Trust")
        let trustButton = alert.addButton(withTitle: "Trust")
        trustButton.isEnabled = false
        // .common modes so the timers fire inside the modal loop.
        let timeout = Timer(timeInterval: Self.trustPromptSeconds, repeats: false) { _ in NSApp.abortModal() }
        let enable = Timer(timeInterval: Self.trustButtonDelaySeconds, repeats: false) { _ in
            trustButton.isEnabled = true
        }
        RunLoop.main.add(timeout, forMode: .common)
        RunLoop.main.add(enable, forMode: .common)
        // Raised by a Bluetooth event while another app is in front, which macOS may not let take the foreground.
        alert.window.level = .floating
        NSApp.activate(ignoringOtherApps: true)
        let answer = alert.runModal()
        // abortModal() ends the loop but leaves the window up, still looking like a question nobody will hear.
        alert.window.orderOut(nil)
        timeout.invalidate()
        enable.invalidate()
        let trusted = answer == .alertSecondButtonReturn
        Log.write(trusted ? "The owner trusted \(phone)" : "The owner did not trust \(phone)")
        return trusted
    }
}
