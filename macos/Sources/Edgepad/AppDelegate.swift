import AppKit
import ApplicationServices
import CoreBluetooth
import EdgepadCore
import Foundation
import IOBluetooth
import ServiceManagement

/// The whole UI: a menu-bar item whose menu is rebuilt each time it opens, so it never shows a stale state.
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private static let documentationUrl = "https://edgepad.akshitbansal.dev/docs"
    private static let accessibilityPaneUrl =
        // Still resolves on macOS 27, where the pane is Device Control and Data Access.
        "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility"
    private static var permissionSteps: String {
        tr(
            "App_PermissionSteps",
            "System Settings › Privacy & Security › Device Control and Data Access › + › Edgepad")
    }
    /// No answer to the trust question within this is a no: a Mac left alone must not end up trusting whichever
    /// paired phone happened to try it.
    private static let trustPromptSeconds: TimeInterval = 60
    /// A click already in flight when the question appears must not land on "Trust".
    private static let trustButtonDelaySeconds: TimeInterval = 2
    /// How often an open trust question checks whether its session ended under it, as Windows polls its dialog.
    private static let trustWithdrawPollSeconds: TimeInterval = 0.25
    /// How long before Bluetooth is tried again after it failed to start.
    private static let bluetoothRetrySeconds: TimeInterval = 30
    /// How long a newer copy waits for the one it replaces to exit before it starts listening anyway.
    private static let takeOverWaitSeconds: TimeInterval = 5
    /// How often the missing input permission is looked at again, so a grant shows without a relaunch.
    private static let permissionPollSeconds: TimeInterval = 1
    private static let welcomedKey = "welcomed"
    private static let lastVersionKey = "lastRunVersion"
    private static let dailyUpdatesKey = "checkUpdatesDaily"

    private let runningVersion = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0.0.0"
    /// Given the last-connected times, so every way of forgetting phones (Reset PIN included) forgets those too.
    private lazy var trust = TrustStore.forCurrentUser(connections: lastConnected)
    /// One store for the server and the Security panel, so a level or PIN change is the one the server reads.
    private let pin = PinStore.forCurrentUser()
    /// The Wi-Fi pairings, forgotten with the trusted phones.
    private let paired = PairedLinks.forCurrentUser()
    private let lastConnected = LastConnected.forCurrentUser()
    private let networkSettings = NetworkSettings.forCurrentUser()
    private var statusItem: NSStatusItem?
    private var host: SessionHost?
    /// Holds the Mac off while another user is switched in or the screen is locked; started once at launch.
    private var sessionWatch: SessionWatch?
    private var server: RfcommServer?
    private var network: NetworkServer?
    private var notifier: Notifier?
    /// The Wi-Fi row's hint: off, listening, or why not.
    private var networkHint = NetworkHint.off
    private var status = StatusText.starting
    /// The "allow Edgepad again" line while it is still the status, so a grant clears it whatever language it is in.
    private var updatedNotice: String?
    private var newerVersion: Version?
    private let updater = MacUpdater()
    private let updateChoice = UpdateChoice(
        path: (FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support") + "/Edgepad/update-choice.txt")
    /// The menu-bar item may stay clickable under the app-modal alert, and a second Forget would lift the hold while
    /// the first was still open.
    private var forgetting = false
    /// The same guard for the Security panel.
    private var securing = false
    /// And for the macro editor, which is also held like Forget.
    private var editingMacros = false
    /// The first-run question (Wi-Fi and clipboard) is put once input control is settled, a phone has connected or
    /// the owner has opened the menu, so it does not land on top of the system prompts.
    private var firstRunQuestionDue = false
    private var bluetoothRetryPending = false
    private var permissionTimer: Timer?
    private var bluetoothPower: BluetoothPower?
    private var updateTimer: Timer?
    private var badged = false
    /// The last inputReady the poll saw, so the phone is told (TEXT 18) only on a change.
    private var lastReady = AXIsProcessTrusted() && CGPreflightPostEventAccess()
    /// A count, not a flag: the Wi-Fi question and the trust question can be open one over the other, and the first to
    /// close must not clear the "!" while the other still waits.
    private var openQuestions = 0
    /// Set once the owner has opened the menu, which the first-run question waits for at the latest.
    private var menuSeen = false

    private var inputReady: Bool { AXIsProcessTrusted() && CGPreflightPostEventAccess() }

    /// The menu bar item's rectangle on screen, which the pairing panel grows from; nil when it cannot be found.
    private func statusItemRect() -> NSRect? {
        guard let button = statusItem?.button, let window = button.window else { return nil }
        return window.convertToScreen(button.convert(button.bounds, to: nil))
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        if exitIfAlreadyRunning() { return }

        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.button?.image = MenuBarMark.image()
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        statusItem = item
        refreshButton()

        notifier = Notifier(onClick: { [weak self] id in
            if id == "update" { self?.openRelease() }
        })
        let firstRun = !networkSettings.hasChoice && !UserDefaults.standard.bool(forKey: Self.welcomedKey)
        if firstRun {
            welcome()
        } else if networkSettings.hasChoice {
            ClipboardSetting.adoptEarlierDefault()
        }
        notifier?.requestPermission()

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
        // inputReady, not Accessibility alone: with event posting denied every tap is dropped, so the phone must hear it.
        host.inputBlocked = { [weak self] in !(self?.inputReady ?? true) }
        host.onFileArrived = { [weak self] path in
            self?.setStatus(
                tr(
                    "Status_Received", "Received %1$@ in Downloads/Edgepad",
                    StatusText.middleTruncated((path as NSString).lastPathComponent)))
        }
        // Off the main queue: a file read and rewrite has no place on the path that admits a phone.
        // The generation is read here, at admit, so a Forget that lands before the write leaves the phone forgotten.
        host.onVerified = { [lastConnected] address in
            let since = lastConnected.generation
            DispatchQueue.global(qos: .utility).async { lastConnected.record(address, since: since) }
        }
        self.host = host
        let watch = SessionWatch(
            hold: { [weak self] in self?.host?.holdOff() }, resume: { [weak self] in self?.host?.resume() })
        watch.start()
        sessionWatch = watch
        server = RfcommServer(host: host, onStatus: { [weak self] in self?.setStatus($0) })
        startServer()
        // After the start, whose failure would otherwise overwrite the hint at once.
        noteUpdatedVersion()

        // Off until the owner says yes (network.txt); only a phone that proves itself is handed to the host.
        network = NetworkServer(
            identity: identity,
            paired: paired,
            trust: trust,
            prompt: PairingPanel(
                origin: { [weak self] in self?.statusItemRect() },
                announce: { [weak self] in
                    self?.setStatus(StatusText.pairing($0))
                    self?.notifier?.post(
                        id: "pair", title: tr("Note_PairTitle", "Pair %1$@?", $0),
                        body: tr("Note_PairBody", "Type the pairing code shown on the phone."))
                }),
            host: host,
            onPaired: { [weak self] in self?.setStatus(StatusText.paired($0)) })
        if networkSettings.hasChoice {
            applyNetwork()
        } else {
            firstRunQuestionDue = true
        }
        watchInputPermission()

        // A sleeping Mac may come back without its Bluetooth record or its listener.
        NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main
        ) { [weak self] _ in self?.listenAgain() }
        // And Bluetooth switched off and on: the record does not necessarily survive it.
        bluetoothPower = BluetoothPower { [weak self] on in
            guard let self else { return }
            if on {
                // Bluetooth only: listenAgain() would also restart Wi-Fi, closing every live Wi-Fi link.
                self.server?.stop()
                self.startServer()
            } else {
                self.server?.stop()
                if self.host?.hasPhone != true {
                    self.setStatus(StatusText.bluetoothProblem(denied: false, off: true))
                }
            }
        }

        scheduleUpdateChecks()
    }

    /// The start-up check and the daily timer, both only while "Check for updates daily" is on (a missing setting
    /// reads as on).
    private func scheduleUpdateChecks() {
        updateTimer?.invalidate()
        updateTimer = nil
        guard checksDaily else { return }
        checkForUpdate()
        updateTimer = Timer.scheduledTimer(withTimeInterval: UpdateCheck.every, repeats: true) { [weak self] _ in
            self?.checkForUpdate()
        }
    }

    private var checksDaily: Bool {
        UserDefaults.standard.object(forKey: Self.dailyUpdatesKey) as? Bool ?? true
    }

    @objc private func toggleDailyUpdates() {
        UserDefaults.standard.set(!checksDaily, forKey: Self.dailyUpdatesKey)
        scheduleUpdateChecks()
    }

    /// Opening Edgepad again while it runs shows where it is instead of replacing it.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        statusItem?.button?.performClick(nil)
        return false
    }

    /// A second copy of the same version says so and exits; a different version takes over from the running one, as
    /// a newer exe does on Windows. True when this copy is leaving.
    private func exitIfAlreadyRunning() -> Bool {
        guard let bundleId = Bundle.main.bundleIdentifier else { return false }
        let others = NSRunningApplication.runningApplications(withBundleIdentifier: bundleId)
            .filter { $0 != NSRunningApplication.current }
        let versions = others.map { other in
            other.bundleURL.flatMap { Bundle(url: $0)?.infoDictionary?["CFBundleShortVersionString"] as? String }
        }
        switch SingleInstance.decide(mine: runningVersion, others: versions) {
        case .takeOver:
            for other in others {
                _ = other.terminate()
            }
            // terminate() only asks. Until the old copy is gone it holds the Wi-Fi port, and a listener started now
            // would fall back to a random port for the whole run.
            let deadline = Date().addingTimeInterval(Self.takeOverWaitSeconds)
            while others.contains(where: { !$0.isTerminated }) && Date() < deadline {
                RunLoop.current.run(until: Date().addingTimeInterval(0.1))
            }
            return false
        case .exitWithNotice:
            let alert = NSAlert()
            alert.messageText = tr("App_AlreadyRunning", "Edgepad is already running.")
            alert.informativeText = tr(
                "App_AlreadyRunningBody",
                "Find it in the menu bar, at the right. Hold Command and drag to reorder icons.")
            NSApp.activate(ignoringOtherApps: true)
            alert.addButton(withTitle: tr("Common_Close", "Close"))
            alert.runModal()
            NSApp.terminate(nil)
            return true
        }
    }

    /// What macOS is about to ask, said before it asks.
    private func welcome() {
        let alert = NSAlert()
        alert.messageText = tr("App_Welcome", "Edgepad lives in the menu bar")
        alert.informativeText = tr(
            "App_WelcomeBody",
            "macOS will now ask for what Edgepad needs. Bluetooth, to hear your phone. Device Control, to move the "
                + "pointer and press keys. Notifications, to tell you when a phone asks to connect. Your Desktop, "
                + "Documents or Downloads folder, only when the phone asks for a screenshot or recording.")
        alert.addButton(withTitle: tr("Welcome_SetUp", "Set up"))
        NSApp.activate(ignoringOtherApps: true)
        alert.runModal()
        alert.window.orderOut(nil)
        UserDefaults.standard.set(true, forKey: Self.welcomedKey)
    }

    /// The first run of a new version with the permission gone: the ad hoc signature changes with every build.
    private func noteUpdatedVersion() {
        let defaults = UserDefaults.standard
        let last = defaults.string(forKey: Self.lastVersionKey)
        defaults.set(runningVersion, forKey: Self.lastVersionKey)
        if let last, last != runningVersion, !inputReady {
            let notice = tr("App_UpdatedAllow", "Updated: allow Edgepad again in Device Control")
            updatedNotice = notice
            setStatus(notice)
        }
    }

    /// Looks at the input permission once a second for the life of the app, so the badge and the phone follow a grant
    /// and a later revoke alike, and the first-run question comes the moment the owner has granted it.
    private func watchInputPermission() {
        guard permissionTimer == nil else { return }
        permissionTimer = Timer.scheduledTimer(withTimeInterval: Self.permissionPollSeconds, repeats: true) {
            [weak self] _ in
            guard let self else { return }
            self.refreshButton()
            // The phone hears a grant (or a revoke) here too, from the poll that already runs.
            let ready = self.inputReady
            if ready != self.lastReady {
                self.lastReady = ready
                self.host?.inputStateChanged()
            }
            if self.inputReady {
                if self.status == self.updatedNotice {
                    self.updatedNotice = nil
                    self.setStatus(StatusText.waiting)
                }
                self.askFirstRunIfDue()
            }
        }
    }

    /// The badge, the tooltip and the "!" that says a question waits.
    private func refreshButton() {
        guard let button = statusItem?.button else { return }
        let missing = !inputReady
        if missing != badged {
            badged = missing
            button.image = MenuBarMark.image(badged: missing)
        }
        button.title = openQuestions > 0 ? "!" : ""
        button.toolTip =
            missing
            ? tr("App_TipBlocked", "Edgepad cannot control this Mac yet") : tr("App_TipStatus", "Edgepad: %1$@", status)
    }

    /// With Start at login the app can launch before Bluetooth is ready or its permission prompt is answered, so a
    /// failed start is tried again rather than left for good.
    private func startServer() {
        guard server?.start() == false else { return }
        let authorization = CBManager.authorization
        // The retry re-enters here every 30 s; a Wi-Fi phone in use keeps its line.
        if host?.hasPhone != true {
            setStatus(
                StatusText.bluetoothProblem(
                    denied: authorization == .denied || authorization == .restricted, off: bluetoothOff))
        }
        guard !bluetoothRetryPending else { return }
        bluetoothRetryPending = true
        Timer.scheduledTimer(withTimeInterval: Self.bluetoothRetrySeconds, repeats: false) { [weak self] _ in
            self?.bluetoothRetryPending = false
            self?.startServer()
        }
    }

    private var bluetoothOff: Bool {
        IOBluetoothHostController.default()?.powerState == kBluetoothHCIPowerStateOFF
    }

    /// After sleep: withdraw and publish the Bluetooth record again, and listen again on Wi-Fi.
    private func listenAgain() {
        server?.stop()
        startServer()
        if networkSettings.load().enabled {
            applyNetwork()
        }
    }

    /// Releases everything the phone was holding down before the app goes.
    func applicationWillTerminate(_ notification: Notification) {
        // Before the host stops: stopping the watch ends any hold it started.
        sessionWatch?.stop()
        network?.stop()
        server?.stop()
        host?.stop()
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        menuSeen = true
        menu.addItem(statusRow())
        if let version = newerVersion, updateChoice.shouldShow(version, now: Date()) {
            if updater.installing {
                menu.addItem(disabled(tr("Menu_Installing", "Installing Edgepad %1$@…", "\(version)")))
            } else {
                let canInstall = UpdateKey.isSet && MacUpdater.canReplaceSelf()
                menu.addItem(
                    canInstall
                        ? item(tr("Menu_Install", "Install Edgepad %1$@", "\(version)"), #selector(installUpdate))
                        : item(
                            tr("Menu_UpdateAvailable", "Update available: %1$@", "\(version)"),
                            #selector(openRelease)))
                menu.addItem(item(tr("Menu_Later", "Later"), #selector(updateLater)))
                menu.addItem(item(tr("Menu_Skip", "Skip this version"), #selector(skipUpdate)))
            }
        }
        // Newer macOS tracks event posting separately from the Accessibility trust, under the
        // same pane. Either one missing means input goes nowhere.
        if !inputReady {
            menu.addItem(
                item(tr("Menu_AllowControl", "Allow Edgepad to control this Mac…"), #selector(allowAccessibility)))
            let steps = disabled(Self.permissionSteps)
            steps.indentationLevel = 1
            menu.addItem(steps)
        }
        menu.addItem(.separator())
        let wifi = item(wifiTitle(), #selector(toggleNetwork))
        wifi.state = networkSettings.load().enabled ? .on : .off
        wifi.toolTip = tr(
            "Menu_WifiTip", "Wi-Fi only on a Mac: USB tethering needs a driver macOS does not include.")
        menu.addItem(wifi)
        let clipboard = item(tr("Menu_Clipboard", "Share clipboard"), #selector(toggleClipboard))
        clipboard.state = ClipboardSetting.enabled ? .on : .off
        clipboard.toolTip = tr("Menu_ClipboardTip", "Copy on one, paste on the other: text and images.")
        menu.addItem(clipboard)
        let login = item(tr("Menu_Login", "Start at login"), #selector(toggleLogin))
        login.state = SMAppService.mainApp.status == .enabled ? .on : .off
        menu.addItem(login)
        let updates = item(tr("Menu_DailyUpdates", "Check for updates daily"), #selector(toggleDailyUpdates))
        updates.state = checksDaily ? .on : .off
        menu.addItem(updates)
        menu.addItem(.separator())
        menu.addItem(
            host?.hasPhone == true
                ? item(tr("Menu_SendFile", "Send a file…"), #selector(sendFile))
                : disabled(tr("Menu_SendFileNoPhone", "Send a file… (no phone connected)")))
        menu.addItem(item(tr("Menu_Macros", "Macros…"), #selector(showMacros)))
        let names = SecurityPanel.levelNames
        let level = names.indices.contains(pin.level - 1) ? names[pin.level - 1] : ""
        menu.addItem(item(tr("Menu_Security", "Security… (%1$@)", level), #selector(showSecurity)))
        menu.addItem(.separator())
        menu.addItem(item(tr("Menu_Documentation", "Documentation"), #selector(openDocumentation)))
        menu.addItem(item(tr("Menu_OpenLog", "Open log"), #selector(openLog)))
        menu.addItem(item(tr("Menu_About", "About Edgepad"), #selector(showAbout)))
        menu.addItem(.separator())
        let quit = item(tr("Menu_Quit", "Quit Edgepad"), #selector(quit))
        quit.keyEquivalent = "q"
        menu.addItem(quit)
        // On the next turn: the menu is open now, and the question is a modal alert.
        DispatchQueue.main.async { [weak self] in self?.askFirstRunIfDue() }
    }

    /// The status in full-strength text: a disabled row is dimmed, and this is the line the owner came to read.
    private func statusRow() -> NSMenuItem {
        let row = disabled(status)
        row.attributedTitle = NSAttributedString(
            string: status, attributes: [.foregroundColor: NSColor.labelColor, .font: NSFont.menuFont(ofSize: 0)])
        return row
    }

    /// The row says the Mac supports Wi-Fi only; a hint is added only when something needs the owner's eye.
    private func wifiTitle() -> String {
        guard networkSettings.load().enabled, let hint = networkHint.problem else {
            return tr("Menu_Wifi", "Wi-Fi connections")
        }
        return tr("Menu_WifiHint", "Wi-Fi connections (%1$@)", hint)
    }

    private func setStatus(_ text: String) {
        // A session that ends resets the line to Waiting; with Bluetooth off, that would hide why nothing connects.
        status =
            text == StatusText.waiting && bluetoothOff ? StatusText.bluetoothProblem(denied: false, off: true) : text
        refreshButton()
        if host?.hasPhone == true {
            DispatchQueue.main.async { [weak self] in self?.askFirstRunIfDue() }
        }
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
                self.newerVersion = version
                // One notification per version, and none for a skipped version or while "Later" holds.
                if !self.updateChoice.wasNotified(version), self.updateChoice.shouldShow(version, now: Date()) {
                    Log.write("Edgepad \(version) is available")
                    self.updateChoice.markNotified(version)
                    self.notifier?.post(
                        id: "update", title: tr("Note_UpdateTitle", "Edgepad %1$@ is available", "\(version)"),
                        body: tr("Note_UpdateBody", "Click to open the download page."))
                }
            }
        }
    }

    @objc private func updateLater() {
        updateChoice.later(now: Date())
    }

    @objc private func skipUpdate() {
        if let version = newerVersion { updateChoice.skip(version) }
    }

    @objc private func installUpdate() {
        guard let version = newerVersion else { return }
        let decision = UpdatePolicy.decide(
            offered: version, running: runningVersion, skipped: nil, keySet: UpdateKey.isSet)
        switch decision {
        case .install:
            break
        case .installNeedsMajorConfirm:
            let alert = NSAlert()
            alert.messageText = tr(
                "Alert_MajorTitle", "Edgepad %1$@ needs the phone app updated too.", "\(version)")
            alert.informativeText = tr(
                "Alert_MajorBody", "Install both together, or the phone and this Mac will not connect.")
            alert.addButton(withTitle: tr("Common_Install", "Install"))
            alert.addButton(withTitle: tr("Common_Cancel", "Cancel"))
            NSApp.activate(ignoringOtherApps: true)
            guard alert.runModal() == .alertFirstButtonReturn else { return }
        case .noKey, .notNewer, .skipped:
            openRelease()
            return
        }
        updater.install(version) { [weak self] failure in
            guard let failure else { return }
            self?.notifier?.post(
                id: "update", title: tr("Note_NotUpdated", "Edgepad was not updated"), body: failure)
        }
    }

    @objc private func openRelease() {
        open(UpdateCheck.latestUrl)
    }

    @objc private func showAbout() {
        NSApp.activate(ignoringOtherApps: true)
        NSApp.orderFrontStandardAboutPanel(options: [
            .credits: NSAttributedString(
                string: "https://edgepad.akshitbansal.dev\n" + tr("Alert_Credits", "Fonts and icons: Lato, Lucide."))
        ])
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
        watchInputPermission()
    }

    @objc private func toggleLogin() {
        setLogin(SMAppService.mainApp.status != .enabled)
    }

    /// Registers or unregisters the login item, and says so when macOS wants the owner's approval or refuses.
    private func setLogin(_ on: Bool) {
        do {
            if on {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
        } catch {
            Log.write("Could not change start at login: \(error.localizedDescription)")
            tell(tr("Alert_LoginFailed", "Could not change Start at login"), error.localizedDescription)
            return
        }
        if on && SMAppService.mainApp.status == .requiresApproval {
            tell(
                tr("Alert_LoginApproval", "Allow Edgepad to start at login"),
                tr("Alert_LoginApprovalBody", "Allow Edgepad in System Settings › General › Login Items."))
            SMAppService.openSystemSettingsLoginItems()
        }
    }

    /// A one-button alert titled with what happened.
    private func tell(_ title: String, _ text: String, style: NSAlert.Style = .warning) {
        let alert = NSAlert()
        alert.alertStyle = style
        alert.messageText = title
        alert.informativeText = text
        alert.addButton(withTitle: tr("Common_Close", "Close"))
        NSApp.activate(ignoringOtherApps: true)
        _ = alert.runModal()
        alert.window.orderOut(nil)
    }

    @objc private func toggleClipboard() {
        ClipboardSetting.enabled.toggle()
    }

    @objc private func openLog() {
        Log.write("Log opened")
        NSWorkspace.shared.open(URL(fileURLWithPath: Log.filePath))
    }

    @objc private func openDocumentation() {
        open(Self.documentationUrl)
    }

    /// Asked first because it cannot be undone: every phone after it has to be trusted again. The connected phone,
    /// and any still being asked about, is hung up on, and connections are refused, while the question is open. If
    /// the trust file cannot be removed the owner is told, and the store refuses every phone until a later Forget
    /// succeeds. Reached from the Security window; true when the phones were forgotten (or the attempt was made).
    private func confirmForget() -> Bool {
        guard !forgetting else { return false }
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
        alert.messageText = tr("Alert_ForgetAll", "Forget all trusted phones?")
        let phones = trust.trustedPhones
        let which =
            phones.isEmpty
            ? tr("Alert_TheTrustedPhones", "the trusted phones")
            : tr("Alert_EveryTrustedPhone", "every trusted phone (%1$@)", phones.joined(separator: ", "))
        alert.informativeText = tr(
            "Alert_ForgetAllBody",
            "Forget %1$@? Each phone, this one included, will have to be trusted here again before it can use "
                + "this Mac.", which)
        // Cancel is first, so it is the default: a stray Return forgets nothing.
        alert.addButton(withTitle: tr("Common_Cancel", "Cancel"))
        alert.addButton(withTitle: tr("Common_Forget", "Forget")).hasDestructiveAction = true
        NSApp.activate(ignoringOtherApps: true)
        let answer = alert.runModal()
        alert.window.orderOut(nil)
        guard answer == .alertSecondButtonReturn else { return false }
        let forgotten = trust.forget()
        // A forgotten phone that had entered the PIN would otherwise skip it at level 2 after one mistaken yes.
        let verifiedForgotten = pin.forgetVerified()
        // Started before the hold ends (the defer above), so there is no instant in which a connection would
        // be taken.
        host?.stayQuiet()
        if !forgotten {
            tell(
                tr("Alert_ForgetFailed", "Could not forget the trusted phones"),
                tr(
                    "Alert_ForgetFailedBody",
                    "Their file could not be removed or emptied. No phone can connect while Edgepad stays running, "
                        + "but those phones will be trusted again when Edgepad starts. Choose Forget all trusted "
                        + "phones again, or delete the file yourself:\n\n%1$@", trust.filePath),
                style: .critical)
        }
        // The Wi-Fi pairings too, or a forgotten phone would resume its link and only meet the trust question, not
        // a new pairing. Still under the hold and the quiet period, so none can pair meanwhile.
        forgetPairedLinks()
        if !verifiedForgotten {
            tell(
                tr("Alert_ForgetPinFailed", "Could not forget the phones that entered the PIN"),
                tr(
                    "Alert_ForgetPinFailedBody",
                    "Their list could not be removed. While Edgepad stays running every phone is asked for the PIN "
                        + "on every connection, but those phones will skip it at “PIN once per phone” again when "
                        + "Edgepad starts. Choose Forget all trusted phones again, or delete pin-verified.txt "
                        + "yourself from:\n\n%1$@", pin.folder),
                style: .critical)
        }
        return true
    }

    /// Every trusted phone with the name the owner knows it by: a Wi-Fi phone's pairing name, else its address.
    private func trustedList() -> [(address: String, name: String, shown: String)] {
        let names = Dictionary(
            paired.list().map { ("link:" + $0.id.lowercased(), $0.name) }, uniquingKeysWith: { first, _ in first })
        let last = lastConnected.snapshot()
        return trust.trustedPhones.map { address in
            let name = names[address.lowercased()] ?? address
            return (
                address: address, name: name,
                shown: LastConnected.rowText(name: name, last: last(address))
            )
        }
    }

    /// Forgets one phone after asking. The Security window already holds phones off, so nothing can answer. The
    /// forgotten phone, and only it, is then refused for the quiet period of Forget all.
    private func confirmForgetOne(_ address: String, _ name: String) -> Bool {
        guard !forgetting else { return false }
        forgetting = true
        defer { forgetting = false }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = tr("Alert_ForgetOne", "Forget %1$@?", name)
        alert.informativeText = tr(
            "Alert_ForgetOneBody", "It will have to be trusted here again before it can use this Mac.")
        alert.addButton(withTitle: tr("Common_Cancel", "Cancel"))
        alert.addButton(withTitle: tr("Common_Forget", "Forget")).hasDestructiveAction = true
        NSApp.activate(ignoringOtherApps: true)
        let answer = alert.runModal()
        alert.window.orderOut(nil)
        guard answer == .alertSecondButtonReturn else { return false }
        var failed = !trust.forgetOne(address)
        failed = !pin.forgetVerified(address) || failed
        if address.lowercased().hasPrefix("link:") {
            failed = !paired.forgetOne(String(address.dropFirst(5))) || failed
        }
        host?.stayQuiet(for: address)
        if failed {
            tell(
                tr("Alert_ForgetOneFailed", "Could not forget %1$@", name),
                tr(
                    "Alert_ForgetOneFailedBody",
                    "A file could not be rewritten, so it may still be trusted. The log has the reason."),
                style: .critical)
        }
        return true
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
            forgetAll: { [weak self] in self?.confirmForget() ?? false },
            phones: { [weak self] in self?.trustedList() ?? [] },
            forgetOne: { [weak self] in self?.confirmForgetOne($0, $1) ?? false },
            hangUp: { [weak self] in self?.host?.stayQuiet() }
        ).run()
    }

    /// Forgets the Wi-Fi pairings, and tells the owner when they could not be. Every way of forgetting phones
    /// (Forget all trusted phones, Reset PIN in the Security panel) goes through here.
    private func forgetPairedLinks() {
        guard !paired.forget() else { return }
        tell(
            tr("Alert_PairingsFailed", "Could not forget the Wi-Fi pairings"),
            tr(
                "Alert_PairingsFailedBody",
                "Their file could not be removed or emptied. Paired phones cannot reconnect over Wi-Fi while "
                    + "Edgepad stays running, but they will be able to again the next time it starts. Choose Forget "
                    + "all trusted phones again, or delete paired-links.txt yourself from:\n\n%1$@", paired.folder),
            style: .critical)
    }

    /// Held like the Security panel, for the same reason, and guarded against a second click under the modal alert.
    /// Finder's Open With, and a file dropped on the icon: sent to the phone in use, as the menu's Send a file does.
    func application(_ sender: NSApplication, openFiles filenames: [String]) {
        guard host?.hasPhone == true else {
            tell(
                tr("Alert_NoPhone", "No phone is connected"),
                tr("Alert_NoPhoneBody", "Connect a phone to Edgepad, then send the file again."))
            sender.reply(toOpenOrPrint: .failure)
            return
        }
        send(filenames)
        sender.reply(toOpenOrPrint: .success)
    }

    @objc private func sendFile() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = true
        panel.message = tr("Alert_ChooseFiles", "Choose files to send to the phone")
        panel.prompt = tr("Common_Send", "Send")
        NSApp.activate(ignoringOtherApps: true)
        guard panel.runModal() == .OK else { return }
        send(panel.urls.map(\.path))
    }

    /// Queues each regular file for the phone in use; the sender takes them one at a time.
    private func send(_ paths: [String]) {
        for path in paths {
            var isFolder: ObjCBool = false
            guard FileManager.default.fileExists(atPath: path, isDirectory: &isFolder), !isFolder.boolValue else {
                setStatus(tr("Status_OnlyFiles", "Only files can be sent, not folders"))
                continue
            }
            let name = StatusText.middleTruncated((path as NSString).lastPathComponent)
            host?.sendFile(path) { [weak self] code in self?.setStatus(Self.sentLine(name, code)) }
        }
    }

    private static func sentLine(_ name: String, _ code: XferCode) -> String {
        switch code {
        case .saved:
            return tr("Xfer_Sent", "Sent %1$@ to the phone", name)
        case .ready, .progress:
            return tr("Xfer_Sending", "Sending %1$@", name)
        case .badHash:
            return tr("Xfer_BadHash", "%1$@ arrived damaged on the phone; send it again", name)
        case .rejected:
            return tr("Xfer_Rejected", "The phone would not take %1$@", name)
        case .writeFailed:
            return tr("Xfer_WriteFailed", "%1$@ could not be read or saved", name)
        case .cancelled:
            return tr("Xfer_Cancelled", "Cancelled sending %1$@", name)
        case .busy:
            return tr("Xfer_Busy", "The phone is already receiving a file; send %1$@ again after it", name)
        case .timedOut:
            return tr("Xfer_TimedOut", "The phone did not answer while %1$@ was sending", name)
        case .sessionEnded:
            return tr("Xfer_SessionEnded", "The phone disconnected before %1$@ was sent", name)
        }
    }

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

    /// The Wi-Fi switch. Turning it on says first what will happen, since macOS may ask about it; a no there leaves
    /// it off.
    @objc private func toggleNetwork() {
        let on = !networkSettings.load().enabled
        if on
            && !confirmNetwork(
                yes: tr("Alert_TurnOn", "Turn on"), no: tr("Common_Cancel", "Cancel"), clipboard: false
            ).allowed
        {
            return
        }
        saveNetwork(on)
    }

    /// Puts the first-run question once input control is settled, a phone has connected or the menu was opened.
    private func askFirstRunIfDue() {
        guard firstRunQuestionDue, openQuestions == 0, inputReady || host?.hasPhone == true || menuSeen else {
            return
        }
        firstRunQuestionDue = false
        askAboutNetwork()
    }

    /// The first run's one question, in two separate choices: Wi-Fi, and sharing the clipboard. Nothing listens until
    /// it is answered, and either answer is saved, so it is never asked again; the menu is the way back.
    private func askAboutNetwork() {
        let answer = confirmNetwork(yes: tr("Alert_Allow", "Allow"), no: tr("Alert_NotNow", "Not Now"), clipboard: true)
        if let share = answer.clipboard {
            ClipboardSetting.enabled = share
        }
        // Off until ticked; a failure is told by setLogin and leaves it off.
        if answer.login == true {
            setLogin(true)
        }
        saveNetwork(answer.allowed)
    }

    /// Says what turning Wi-Fi on will do. `allowed` is true only for `yes`; `clipboard` is the checkbox, asked on
    /// the first run only, off until ticked.
    private func confirmNetwork(
        yes: String, no: String, clipboard: Bool
    ) -> (allowed: Bool, clipboard: Bool?, login: Bool?) {
        let alert = NSAlert()
        alert.messageText = tr("Alert_Network", "Let phones find this laptop?")
        alert.informativeText = tr(
            "Alert_NetworkBody",
            "Wi-Fi only on a Mac: USB tethering needs a driver macOS does not include. Phones on the same Wi-Fi "
                + "will be able to find this Mac and ask to pair. Nothing connects without the pairing code typed "
                + "here. macOS may ask whether Edgepad may find devices on your local network, and the firewall "
                + "whether it may accept incoming connections: allow both.")
        var box: NSButton?
        var loginBox: NSButton?
        if clipboard {
            let share = NSButton(
                checkboxWithTitle: tr("Menu_Clipboard", "Share clipboard"), target: nil, action: nil)
            share.state = .off
            let note = NSTextField(
                wrappingLabelWithString: tr(
                    "Menu_ClipboardTip", "Copy on one, paste on the other: text and images."))
            note.textColor = .secondaryLabelColor
            note.preferredMaxLayoutWidth = 280
            let login = NSButton(
                checkboxWithTitle: tr("Menu_Login", "Start at login"), target: nil, action: nil)
            login.state = .off
            let stack = NSStackView(views: [share, note, login])
            stack.orientation = .vertical
            stack.alignment = .leading
            stack.spacing = 4
            stack.setFrameSize(NSSize(width: 280, height: stack.fittingSize.height))
            alert.accessoryView = stack
            box = share
            loginBox = login
        }
        // The no is first, so it is the default: a stray Return turns nothing on.
        alert.addButton(withTitle: no)
        alert.addButton(withTitle: yes)
        openQuestions += 1
        refreshButton()
        NSApp.activate(ignoringOtherApps: true)
        let answer = alert.runModal()
        alert.window.orderOut(nil)
        openQuestions -= 1
        refreshButton()
        return (answer == .alertSecondButtonReturn, box.map { $0.state == .on }, loginBox.map { $0.state == .on })
    }

    /// Saves the owner's answer and acts on it. A setting that cannot be saved leaves things as they were.
    private func saveNetwork(_ on: Bool) {
        guard networkSettings.setEnabled(on) else {
            tell(
                tr("Alert_SaveFailed", "Could not save the setting"),
                tr("Alert_SaveFailedBody", "Nothing was changed. The log has the reason."), style: .critical)
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
            networkHint = .off
            return
        }
        networkHint = .starting
        network.start(port: config.port) { [weak self] hint in self?.networkHint = hint }
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }

    /// Asks whether to trust `phone`, and waits. Called by the session while it holds the phone's HELLO_ACK. A
    /// session that ends meanwhile (`ended`: a hold, a hang-up) is a no too, and takes the question down.
    private func askTrust(_ phone: String, _ ended: Cancellation) -> Bool {
        guard !ended.isCancelled else { return false }
        setStatus(StatusText.asking(phone))
        // The alert may stay behind another app, so it is also said in a notification and by the "!" in the bar.
        notifier?.post(
            id: "trust", title: tr("Note_TrustTitle", "Trust %1$@?", phone),
            body: tr("Note_TrustBody", "%1$@ wants to connect. Choose Trust or Don’t Trust.", phone))
        openQuestions += 1
        refreshButton()
        defer {
            openQuestions -= 1
            refreshButton()
            notifier?.withdraw(id: "trust")
        }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = tr("Alert_Trust", "Trust %1$@?", phone)
        let whatIsTrusted =
            phone.contains("\"")
            ? tr(
                "Alert_TrustQuoted",
                "The name in quotes is only what the phone calls itself; the address is what will be trusted.")
            : tr("Alert_TrustAddress", "The address is what will be trusted.")
        alert.informativeText = tr(
            "Alert_TrustBody",
            "%1$@ is asking to control this Mac’s pointer and keyboard. %2$@ Trust it only if it is yours. "
                + "With no answer in %3$@ seconds, it is not trusted.",
            phone, whatIsTrusted, "\(Int(Self.trustPromptSeconds))")
        // Don't Trust is first, so it is the default: a stray Return trusts nothing.
        alert.addButton(withTitle: tr("Common_DontTrust", "Don’t Trust"))
        let trustButton = alert.addButton(withTitle: tr("Common_Trust", "Trust"))
        trustButton.isEnabled = false
        // .common modes so the timers fire inside the modal loop. The time limit is checked by the poll below rather
        // than aborted by a one-shot timer, which would end a Security panel opened over the question instead.
        let deadline = Date().addingTimeInterval(Self.trustPromptSeconds)
        let enable = Timer(timeInterval: Self.trustButtonDelaySeconds, repeats: false) { _ in
            trustButton.isEnabled = true
        }
        // Polled on the main run loop rather than called back from end(), which can run inside another dialog's
        // action (Security holds phones off before its panel opens) where an abort would end the wrong loop. The
        // window goes at once; the loop is aborted only while it is this alert's, so a panel opened over the
        // question keeps its own, and the alert's loop ends at the next tick after that panel closes.
        let withdraw = Timer(timeInterval: Self.trustWithdrawPollSeconds, repeats: true) { _ in
            guard ended.isCancelled || Date() >= deadline else { return }
            alert.window.orderOut(nil)
            if NSApp.modalWindow === alert.window {
                NSApp.abortModal()
            }
        }
        RunLoop.main.add(enable, forMode: .common)
        RunLoop.main.add(withdraw, forMode: .common)
        // Raised by a Bluetooth event while another app is in front, which macOS may not let take the foreground.
        alert.window.level = .floating
        NSApp.activate(ignoringOtherApps: true)
        let answer = alert.runModal()
        // abortModal() ends the loop but leaves the window up, still looking like a question nobody will hear.
        alert.window.orderOut(nil)
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
