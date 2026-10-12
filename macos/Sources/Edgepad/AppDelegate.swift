import AppKit
import ApplicationServices
import EdgepadCore
import Foundation
import ServiceManagement

/// The whole UI: a menu-bar item whose menu is rebuilt each time it opens, so it never shows a stale state.
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private static let documentationUrl = "https://edgepad.vercel.app/docs"
    private static let accessibilityPaneUrl =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility"
    /// No answer to the trust question within this is a no: a Mac left alone must not end up trusting whichever
    /// paired phone happened to try it.
    private static let trustPromptSeconds: TimeInterval = 60

    private let runningVersion = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0.0.0"
    private let trust = TrustStore.forCurrentUser()
    private var statusItem: NSStatusItem?
    private var server: RfcommServer?
    private var status = "Starting"
    private var newerVersion: Version?

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

        // Without it macOS silently discards every event this app posts.
        Log.write("Accessibility permission granted: \(requestAccessibility())")

        let server = RfcommServer(
            trust: trust,
            askTrust: { [weak self] in self?.askTrust($0) ?? false },
            onStatus: { [weak self] in self?.setStatus($0) })
        self.server = server
        if !server.start() {
            setStatus("Bluetooth is unavailable; see the log")
        }

        checkForUpdate()
        Timer.scheduledTimer(withTimeInterval: UpdateCheck.every, repeats: true) { [weak self] _ in
            self?.checkForUpdate()
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
        if !AXIsProcessTrusted() {
            menu.addItem(item("Allow Edgepad to control this Mac…", #selector(allowAccessibility)))
        }
        menu.addItem(.separator())
        let login = item("Start at login", #selector(toggleLogin))
        login.state = SMAppService.mainApp.status == .enabled ? .on : .off
        menu.addItem(login)
        menu.addItem(item("Open log", #selector(openLog)))
        menu.addItem(item("Documentation", #selector(openDocumentation)))
        let forget = item("Forget trusted phone", #selector(confirmForget))
        forget.isEnabled = trust.trusted != nil
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
        _ = requestAccessibility()
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

    /// Asked first because the menu cannot undo it: every phone after it has to be trusted again.
    @objc private func confirmForget() {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "Edgepad"
        alert.informativeText =
            "Forget \(trust.trusted ?? "the trusted phone")? The next phone to connect, this one included, will not "
            + "be able to use this Mac until you trust it here."
        // Cancel is first, so it is the default: a stray Return forgets nothing.
        alert.addButton(withTitle: "Cancel")
        alert.addButton(withTitle: "Forget")
        NSApp.activate(ignoringOtherApps: true)
        if alert.runModal() == .alertSecondButtonReturn {
            trust.forget()
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
        alert.informativeText =
            "\(phone) is asking to control this Mac's pointer and keyboard. Trust it only if it is yours. "
            + "With no answer in \(Int(Self.trustPromptSeconds)) seconds, it is not trusted."
        // Don't Trust is first, so it is the default: a stray Return trusts nothing.
        alert.addButton(withTitle: "Don't Trust")
        alert.addButton(withTitle: "Trust")
        // .common modes so the timer fires inside the modal loop.
        let timeout = Timer(timeInterval: Self.trustPromptSeconds, repeats: false) { _ in NSApp.abortModal() }
        RunLoop.main.add(timeout, forMode: .common)
        NSApp.activate(ignoringOtherApps: true)
        let answer = alert.runModal()
        timeout.invalidate()
        let trusted = answer == .alertSecondButtonReturn
        Log.write(trusted ? "The owner trusted \(phone)" : "The owner did not trust \(phone)")
        return trusted
    }
}
