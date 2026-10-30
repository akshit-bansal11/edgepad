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
    /// How often an open trust question checks whether its session ended under it, as Windows polls its dialog.
    private static let trustWithdrawPollSeconds: TimeInterval = 0.25
    /// How long before Bluetooth is tried again after it failed to start.
    private static let bluetoothRetrySeconds: TimeInterval = 30

    private let runningVersion = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0.0.0"
    private let trust = TrustStore.forCurrentUser()
    /// One store for the server and the Security panel, so a level or PIN change is the one the server reads.
    private let pin = PinStore.forCurrentUser()
    /// The Wi-Fi and USB pairings, forgotten with the trusted phones.
    private let paired = PairedLinks.forCurrentUser()
    private let networkSettings = NetworkSettings.forCurrentUser()
    private var statusItem: NSStatusItem?
    private var host: SessionHost?
    private var server: RfcommServer?
    private var network: NetworkServer?
    /// The Wi-Fi and USB row's hint: off, the port listened on, or why not.
    private var networkHint = "Off"
    private var status = "Starting"
    private var newerVersion: Version?
    /// The menu-bar item may stay clickable under the app-modal alert, and a second Forget would lift the hold while
    /// the first was still open.
    private var forgetting = false
    /// The same guard for the Security panel.
    private var securing = false
    /// And for the macro editor, which is also held like Forget.
    private var editingMacros = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Newest wins, as on Windows where a newer exe asks the running one to quit.
        if let bundleId = Bundle.main.bundleIdentifier {
            for other in NSRunningApplication.runningApplications(withBundleIdentifier: bundleId)
            where other != NSRunningApplication.current {
                _ = other.terminate()
            }
        }

        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.button?.image = MenuBarMark.image()
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        statusItem = item

        // Without them macOS silently discards every event this app posts.
        Log.write(
            "Input access: accessibility=\(AXIsProcessTrusted()), eventPosting=\(CGPreflightPostEventAccess())"
        )
        _ = requestAccessibility()

        let identity = LinkIdentity.forCurrentUser(name: Host.current().localizedName ?? "Mac")
        let host = SessionHost(
            trust: trust,
            pin: pin,
            askTrust: { [weak self] in self?.askTrust($0, $1) ?? false },
            identity: identity,
            onStatus: { [weak self] in self?.setStatus($0) })
        self.host = host
        server = RfcommServer(host: host, onStatus: { [weak self] in self?.setStatus($0) })
        startServer()

        // Off until the owner says yes (network.txt); only a phone that proves itself is handed to the host.
        network = NetworkServer(
            identity: identity,
            paired: paired,
            trust: trust,
            prompt: PairingPanel(announce: { [weak self] in
                self?.setStatus("Pair \($0): type the code shown on the phone")
            }),
            host: host,
            onPaired: { [weak self] in self?.setStatus("Paired with \($0)") })
        if networkSettings.hasChoice {
            applyNetwork()
        } else {
            // On the next turn, so launch finishes rather than waiting on the answer.
            DispatchQueue.main.async { [weak self] in self?.askAboutNetwork() }
        }

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
        network?.stop()
        server?.stop()
        host?.stop()
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
        menu.addItem(item("Security… (level \(pin.level))", #selector(showSecurity)))
        let macroCount = host?.macros.macros.count ?? 0
        menu.addItem(item("Macros… (\(macroCount) of \(MacroStore.maxMacros))", #selector(showMacros)))
        let wifi = item("Wi-Fi and USB connections", #selector(toggleNetwork))
        wifi.state = networkSettings.load().enabled ? .on : .off
        menu.addItem(wifi)
        menu.addItem(disabled("    \(networkHint)"))
        let forget = item("Forget trusted phones", #selector(confirmForget))
        forget.isEnabled = trust.canForget || paired.canForget
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
        host?.holdOff()
        defer {
            host?.resume()
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
            // A forgotten phone that had entered the PIN would otherwise skip it at level 2 after one mistaken yes.
            let verifiedForgotten = pin.forgetVerified()
            // Started before the hold ends (the defer above), so there is no instant in which a connection would
            // be taken.
            host?.stayQuiet()
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
            // The Wi-Fi and USB pairings too, or a forgotten phone would resume its link and only meet the trust
            // question, not a new pairing. Still under the hold and the quiet period, so none can pair meanwhile.
            forgetPairedLinks()
            if !verifiedForgotten {
                let failure = NSAlert()
                failure.alertStyle = .critical
                failure.messageText = "Edgepad"
                failure.informativeText =
                    "The phones that entered the PIN could not be forgotten: their list could not be removed. While "
                    + "Edgepad stays running every phone is asked for the PIN on every connection, but those phones "
                    + "will skip it at level 2 again when Edgepad starts. Choose Forget trusted phones again, or "
                    + "delete pin-verified.txt yourself from:\n\n\(pin.folder)"
                failure.addButton(withTitle: "OK")
                _ = failure.runModal()
                failure.window.orderOut(nil)
            }
        }
    }

    /// Held like Forget, for the same reason: a connected phone is a keyboard and could answer the panel.
    @objc private func showSecurity() {
        guard !securing else { return }
        securing = true
        host?.holdOff()
        defer {
            host?.resume()
            securing = false
        }
        SecurityPanel(
            pin: pin, trust: trust, forgetPairings: { [weak self] in self?.forgetPairedLinks() },
            hangUp: { [weak self] in self?.host?.stayQuiet() }
        ).run()
    }

    /// Forgets the Wi-Fi and USB pairings, and tells the owner when they could not be. Every way of forgetting phones
    /// (Forget trusted phones, Reset PIN in the Security panel) goes through here.
    private func forgetPairedLinks() {
        guard !paired.forget() else { return }
        let failure = NSAlert()
        failure.alertStyle = .critical
        failure.messageText = "Edgepad"
        failure.informativeText =
            "The Wi-Fi and USB pairings could not be forgotten: their file could not be removed or emptied. Paired "
            + "phones cannot reconnect over Wi-Fi or USB while Edgepad stays running, but they will be able to again "
            + "the next time it starts. Choose Forget trusted phones again, or delete paired-links.txt yourself "
            + "from:\n\n\(paired.folder)"
        failure.addButton(withTitle: "OK")
        _ = failure.runModal()
        failure.window.orderOut(nil)
    }

    /// Held like the Security panel, for the same reason, and guarded against a second click under the modal alert.
    @objc private func showMacros() {
        guard !editingMacros, let host else { return }
        editingMacros = true
        host.holdOff()
        defer {
            host.resume()
            editingMacros = false
        }
        MacroEditorWindow(store: host.macros).run()
    }

    /// The Wi-Fi and USB switch. Turning it on says first what will happen, since macOS may ask about it; a no there
    /// leaves it off.
    @objc private func toggleNetwork() {
        let on = !networkSettings.load().enabled
        if on && !confirmNetwork(yes: "Turn on", no: "Cancel") {
            return
        }
        saveNetwork(on)
    }

    /// The first launch's one question. Nothing listens until it is answered, and either answer is saved, so it is
    /// never asked again; the switch in the menu is the way back.
    private func askAboutNetwork() {
        saveNetwork(confirmNetwork(yes: "Allow", no: "Not Now"))
    }

    /// Says what turning this on will do. True only for `yes`.
    private func confirmNetwork(yes: String, no: String) -> Bool {
        let alert = NSAlert()
        alert.messageText = "Wi-Fi and USB connections"
        alert.informativeText =
            "Phones on the same Wi-Fi will be able to find this Mac and ask to pair. Nothing connects without the "
            + "code typed here. macOS may ask whether Edgepad may find devices on your local network, and the "
            + "firewall whether it may accept incoming connections: allow both. USB tethering needs a driver macOS "
            + "does not include, so it is not supported on a Mac."
        // The no is first, so it is the default: a stray Return turns nothing on.
        alert.addButton(withTitle: no)
        alert.addButton(withTitle: yes)
        NSApp.activate(ignoringOtherApps: true)
        let answer = alert.runModal()
        alert.window.orderOut(nil)
        return answer == .alertSecondButtonReturn
    }

    /// Saves the owner's answer and acts on it. A setting that cannot be saved leaves things as they were.
    private func saveNetwork(_ on: Bool) {
        guard networkSettings.setEnabled(on) else {
            let failure = NSAlert()
            failure.alertStyle = .critical
            failure.messageText = "Edgepad"
            failure.informativeText = "The setting could not be saved, so nothing was changed. The log has the reason."
            failure.addButton(withTitle: "OK")
            _ = failure.runModal()
            failure.window.orderOut(nil)
            return
        }
        applyNetwork()
    }

    /// Listens or stops as network.txt says, and words the row's hint for it.
    private func applyNetwork() {
        guard let network else { return }
        let config = networkSettings.load()
        guard config.enabled else {
            network.stop()
            networkHint = "Off"
            return
        }
        networkHint = "Starting"
        network.start(port: config.port) { [weak self] hint in self?.networkHint = hint }
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }

    /// Asks whether to trust `phone`, and waits. Called by the session while it holds the phone's HELLO_ACK. A
    /// session that ends meanwhile (`ended`: a hold, a hang-up) is a no too, and takes the question down.
    private func askTrust(_ phone: String, _ ended: Cancellation) -> Bool {
        guard !ended.isCancelled else { return false }
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
        // Polled on the main run loop rather than called back from end(), which can run inside another dialog's
        // action (Security holds phones off before its panel opens) where an abort would end the wrong loop. The
        // window goes at once; the loop is aborted only while it is this alert's, so a panel opened over the
        // question keeps its own, and the alert's loop ends at the next tick after that panel closes.
        let withdraw = Timer(timeInterval: Self.trustWithdrawPollSeconds, repeats: true) { _ in
            guard ended.isCancelled else { return }
            alert.window.orderOut(nil)
            if NSApp.modalWindow === alert.window {
                NSApp.abortModal()
            }
        }
        RunLoop.main.add(timeout, forMode: .common)
        RunLoop.main.add(enable, forMode: .common)
        RunLoop.main.add(withdraw, forMode: .common)
        // Raised by a Bluetooth event while another app is in front, which macOS may not let take the foreground.
        alert.window.level = .floating
        NSApp.activate(ignoringOtherApps: true)
        let answer = alert.runModal()
        // abortModal() ends the loop but leaves the window up, still looking like a question nobody will hear.
        alert.window.orderOut(nil)
        timeout.invalidate()
        enable.invalidate()
        withdraw.invalidate()
        if ended.isCancelled {
            // A Trust click that raced the hold counts for nothing: the session is gone and reads no answer.
            Log.write("Withdrew the trust question about \(phone): its session ended")
            return false
        }
        let trusted = answer == .alertSecondButtonReturn
        Log.write(trusted ? "The owner trusted \(phone)" : "The owner did not trust \(phone)")
        return trusted
    }
}
