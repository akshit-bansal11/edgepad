import AppKit
import EdgepadCore

/// What the Integrations window needs from the rest of the app, filled by the wiring step: the settings, the plugin
/// installer, the GitHub sign-in, when an agent was last heard, and who to tell that a switch moved.
struct IntegrationsHooks {
    let settings: IntegrationSettings
    let installer: PluginInstaller
    let signIn: GitHubSignIn
    /// The signed-in GitHub login, for display only; nil when it cannot be read.
    let login: () async -> String?
    /// UTC minutes of the last hook message from `claude` or `codex`, -1 for never.
    let lastHeard: (String) -> Int64
    /// An integration (an `IntegrationSettings` key) was switched on or off, or its settings changed.
    let switched: (String, Bool) -> Void
    /// The installer's `ensureCurrent`, which also says when Codex must trust Edgepad again.
    let ensureCurrent: () async -> [(agent: String, state: PluginState)]
}

/// The Integrations window: system stats, weather, Claude Code, Codex and GitHub, every one off until the owner turns
/// it on, the twin of the Windows and Linux windows. Switches write `IntegrationSettings` at once. No network call is
/// made from here except the GitHub sign-in the owner starts; opening it runs the installer's `ensureCurrent`.
/// Non-modal; closing it stops a sign-in in progress. Main thread only.
final class IntegrationsWindow: NSObject, NSWindowDelegate, NSTextFieldDelegate {
    private static let width: CGFloat = 480

    /// The menu item's title, for the wiring step.
    static var menuTitle: String { tr("Menu_Integrations", "Integrations…") }

    private let hooks: IntegrationsHooks
    private var window: NSWindow?
    private let stats = NSButton(checkboxWithTitle: "", target: nil, action: nil)
    private let weather = NSButton(checkboxWithTitle: "", target: nil, action: nil)
    private let city = NSTextField()
    private var rows: [String: AgentRow] = [:]
    private let gitHubStatus = NSTextField(wrappingLabelWithString: "")
    private let gitHubSteps = NSTextField(wrappingLabelWithString: "")
    private let signButton = NSButton(title: "", target: nil, action: nil)
    private let privateRepos = NSButton(checkboxWithTitle: "", target: nil, action: nil)
    private let codeLabel = NSTextField(labelWithString: "")
    private let codeButtons = NSStackView()
    private let waitingLabel = NSTextField(wrappingLabelWithString: "")
    private var userCode = ""
    private var signingIn = false
    private var gitHubProblem: String?

    init(hooks: IntegrationsHooks) {
        self.hooks = hooks
        super.init()
    }

    /// Shows the window, or brings it to the front when it is open already.
    func show() {
        if window == nil {
            window = makeWindow()
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
        refresh()
        let ensureCurrent = hooks.ensureCurrent
        Task { @MainActor [weak self] in
            let notices = await ensureCurrent()
            for notice in notices {
                self?.rows[notice.agent]?.notice(notice.state)
            }
            self?.refresh()
        }
    }

    func windowWillClose(_ notification: Notification) {
        hooks.signIn.cancel()
        signingIn = false
        window = nil
    }

    // ---- building

    private func makeWindow() -> NSWindow {
        let settings = hooks.settings
        stats.title = tr("Integrations_Stats", "Show system stats on the phone")
        stats.target = self
        stats.action = #selector(statsChanged)
        weather.title = tr("Integrations_Weather", "Show weather on the phone")
        weather.target = self
        weather.action = #selector(weatherChanged)
        city.stringValue = settings.text(IntegrationSettings.city)
        city.placeholderString = tr("Integrations_City", "City")
        city.delegate = self
        city.widthAnchor.constraint(equalToConstant: 240).isActive = true
        let cityRow = NSStackView(views: [NSTextField(labelWithString: tr("Integrations_City", "City")), city])
        cityRow.spacing = 8

        let claude = AgentRow(
            agent: AgentToken.claude.rawValue, title: tr("Integrations_Claude", "Claude Code"), owner: self)
        let codex = AgentRow(agent: AgentToken.codex.rawValue, title: tr("Integrations_Codex", "Codex"), owner: self)
        rows = [claude.agent: claude, codex.agent: codex]

        signButton.target = self
        signButton.action = #selector(signPressed)
        privateRepos.title = tr("Integrations_Private", "Include private repositories")
        privateRepos.target = self
        privateRepos.action = #selector(privateChanged)
        codeLabel.font = .monospacedSystemFont(ofSize: 22, weight: .semibold)
        codeLabel.isSelectable = true
        let copyCode = NSButton(title: tr("Integrations_Copy", "Copy"), target: self, action: #selector(copyCode))
        let openPage = NSButton(
            title: tr("Integrations_OpenPage", "Open github.com/login/device"), target: self,
            action: #selector(openPage))
        let cancel = NSButton(title: tr("Common_Cancel", "Cancel"), target: self, action: #selector(cancelSignIn))
        codeButtons.setViews([copyCode, openPage, cancel], in: .leading)
        codeButtons.spacing = 8
        waitingLabel.stringValue = tr("Integrations_Waiting", "Waiting for you to approve in the browser…")
        gitHubSteps.stringValue = tr(
            "Integrations_GitHubSteps",
            "1. Click Sign in with GitHub. A github.com page opens.\n"
                + "2. Enter the code shown here and click Authorize.\n"
                + "3. On your phone, open Controls > GitHub.")
        gitHubSteps.textColor = .secondaryLabelColor
        for label in [gitHubStatus, gitHubSteps, waitingLabel] {
            label.preferredMaxLayoutWidth = Self.width
        }

        let close = NSButton(title: tr("Common_Close", "Close"), target: self, action: #selector(closePressed))
        close.keyEquivalent = "\r"
        let intro = NSTextField(
            wrappingLabelWithString: tr("Integrations_Intro", "Everything here is off until you turn it on."))
        let note = NSTextField(
            wrappingLabelWithString: tr("Integrations_WeatherNote", "Sends only the city name to open-meteo.com."))
        note.textColor = .secondaryLabelColor
        let gitHubTitle = Self.heading(tr("Integrations_GitHub", "GitHub"))
        let developersTitle = Self.heading(tr("Integrations_ForDevelopers", "For developers"))
        developersTitle.textColor = .secondaryLabelColor
        let views: [NSView] = [
            intro, stats, weather, cityRow, note, developersTitle, claude.view, codex.view, gitHubTitle,
            gitHubStatus, gitHubSteps,
            signButton,
            privateRepos, codeLabel, codeButtons, waitingLabel, close,
        ]
        let stack = NSStackView(views: views)
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 10
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 20, right: 20)
        stack.setCustomSpacing(18, after: note)
        stack.setCustomSpacing(10, after: developersTitle)
        stack.setCustomSpacing(18, after: codex.view)

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: Self.width + 40, height: 600), styleMask: [.titled, .closable],
            backing: .buffered, defer: false)
        window.title = tr("Integrations_Title", "Integrations")
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.contentView = stack
        window.center()
        return window
    }

    static func heading(_ text: String) -> NSTextField {
        let label = NSTextField(labelWithString: text)
        label.font = .boldSystemFont(ofSize: NSFont.systemFontSize)
        return label
    }

    // ---- showing

    func refresh() {
        let settings = hooks.settings
        stats.state = settings.flag(IntegrationSettings.stats) ? .on : .off
        weather.state = settings.flag(IntegrationSettings.weather) ? .on : .off
        for row in rows.values {
            row.show(hooks.installer.status(row.agent), heard: heard(hooks.lastHeard(row.agent)))
        }
        showGitHub()
        window?.contentView?.layoutSubtreeIfNeeded()
    }

    private func showGitHub() {
        let configured = GitHubApp.isConfigured(GitHubApp.clientId)
        let signedIn = hooks.signIn.isSignedIn
        let login = hooks.settings.text(IntegrationSettings.gitHubLogin)
        privateRepos.state = hooks.settings.flag(IntegrationSettings.gitHubPrivate) ? .on : .off
        if !configured {
            gitHubStatus.stringValue = tr("Integrations_NoClientId", "Sign-in is not set up in this build.")
        } else if signedIn {
            let shown = login.isEmpty ? "GitHub" : login
            gitHubStatus.stringValue = tr("Integrations_SignedIn", "Signed in as %1$@", shown)
        } else {
            gitHubStatus.stringValue = gitHubProblem ?? tr("Integrations_NotConnected", "Not connected")
        }
        signButton.title =
            signedIn ? tr("Integrations_SignOut", "Sign out") : tr("Integrations_SignIn", "Sign in with GitHub")
        gitHubSteps.isHidden = !configured || signedIn
        signButton.isEnabled = configured && !signingIn
        privateRepos.isEnabled = configured && !signingIn
        codeLabel.stringValue = tr("Integrations_Code", "Your code: %1$@", userCode)
        codeLabel.isHidden = !signingIn || userCode.isEmpty
        codeButtons.isHidden = codeLabel.isHidden
        waitingLabel.isHidden = codeLabel.isHidden
    }

    /// "14:05" today, "8 Nov" before, "never" when nothing was heard.
    private func heard(_ minutes: Int64) -> String {
        guard minutes >= 0 else { return tr("Integrations_Never", "never") }
        let date = Date(timeIntervalSince1970: TimeInterval(minutes) * 60)
        let formatter = DateFormatter()
        if Calendar.current.isDateInToday(date) {
            formatter.timeStyle = .short
            formatter.dateStyle = .none
        } else {
            formatter.setLocalizedDateFormatFromTemplate("d MMM")
        }
        return formatter.string(from: date)
    }

    // ---- switches

    @objc private func statsChanged() {
        let on = stats.state == .on
        hooks.settings.setFlag(IntegrationSettings.stats, on)
        hooks.switched(IntegrationSettings.stats, on)
    }

    @objc private func weatherChanged() {
        let on = weather.state == .on
        saveCity()
        hooks.settings.setFlag(IntegrationSettings.weather, on)
        hooks.switched(IntegrationSettings.weather, on)
    }

    func controlTextDidEndEditing(_ notification: Notification) {
        guard saveCity() else { return }
        hooks.switched(IntegrationSettings.weather, hooks.settings.flag(IntegrationSettings.weather))
    }

    /// Stores the typed city and forgets the place looked up for the old one. True when it changed.
    @discardableResult
    private func saveCity() -> Bool {
        let settings = hooks.settings
        let typed = city.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard typed != settings.text(IntegrationSettings.city) else { return false }
        settings.set(IntegrationSettings.city, typed)
        for key in [IntegrationSettings.lat, IntegrationSettings.lon, IntegrationSettings.place] {
            settings.set(key, "")
        }
        return true
    }

    // ---- agents

    fileprivate func connect(_ row: AgentRow, _ on: Bool) {
        let installer = hooks.installer
        let agent = row.agent
        row.busy(true)
        Task { @MainActor [weak self] in
            let result: PluginResult
            if on {
                result = await installer.connect(agent)
            } else {
                result = await installer.disconnect(agent)
            }
            row.busy(false)
            row.result(result, connecting: on)
            self?.hooks.switched(agent, self?.hooks.settings.flag(agent) ?? false)
            self?.refresh()
        }
    }

    fileprivate func copy(_ text: String) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(text, forType: .string)
    }

    // ---- GitHub

    @objc private func signPressed() {
        if hooks.signIn.isSignedIn {
            hooks.signIn.signOut()
            hooks.settings.setFlag(IntegrationSettings.gitHub, false)
            hooks.settings.set(IntegrationSettings.gitHubLogin, "")
            hooks.switched(IntegrationSettings.gitHub, false)
            refresh()
        } else {
            startSignIn()
        }
    }

    @objc private func privateChanged() {
        let on = privateRepos.state == .on
        hooks.settings.setFlag(IntegrationSettings.gitHubPrivate, on)
        // The scope changes with the box, so a signed-in owner signs in again to grant (or drop) it.
        if hooks.signIn.isSignedIn {
            startSignIn()
        }
    }

    private func startSignIn() {
        let signIn = hooks.signIn
        let includePrivate = hooks.settings.flag(IntegrationSettings.gitHubPrivate)
        gitHubProblem = nil
        signingIn = true
        userCode = ""
        refresh()
        Task { @MainActor [weak self] in
            let code = await signIn.start(includePrivate: includePrivate)
            guard let self, code.state == .started else {
                self?.endSignIn(code.state)
                return
            }
            self.userCode = code.userCode
            self.refresh()
            let end = await signIn.wait()
            if end == .signedIn {
                let login = await self.hooks.login()
                self.hooks.settings.set(IntegrationSettings.gitHubLogin, login ?? "")
                self.hooks.settings.setFlag(IntegrationSettings.gitHub, true)
                self.hooks.switched(IntegrationSettings.gitHub, true)
            }
            self.endSignIn(end)
        }
    }

    private func endSignIn(_ state: SignInState) {
        signingIn = false
        userCode = ""
        switch state {
        case .signedIn, .cancelled: gitHubProblem = nil
        case .notConfigured: gitHubProblem = tr("Integrations_NoClientId", "Sign-in is not set up in this build.")
        default: gitHubProblem = tr("Integrations_SignInFailed", "Sign-in did not finish. Try again.")
        }
        refresh()
    }

    @objc private func copyCode() {
        copy(userCode)
    }

    @objc private func openPage() {
        if let url = URL(string: GitHubApp.devicePage) {
            NSWorkspace.shared.open(url)
        }
    }

    @objc private func cancelSignIn() {
        hooks.signIn.cancel()
    }

    @objc private func closePressed() {
        window?.performClose(nil)
    }
}

/// One Claude Code or Codex row: its name, where it stands, the Connect or Disconnect button, the next step, and the
/// Copy box with the commands when Edgepad could not run them itself.
private final class AgentRow: NSObject {
    let agent: String
    let view: NSStackView
    private weak var owner: IntegrationsWindow?
    private let status = NSTextField(wrappingLabelWithString: "")
    private let button = NSButton(title: "", target: nil, action: nil)
    private let step = NSTextField(wrappingLabelWithString: "")
    private let commands = NSTextField(wrappingLabelWithString: "")
    private let copyButton = NSButton(title: tr("Integrations_Copy", "Copy"), target: nil, action: nil)
    private var problem: PluginState?
    private var manual: [String] = []
    private var restart = false
    private var connected = false

    init(agent: String, title: String, owner: IntegrationsWindow) {
        self.agent = agent
        self.owner = owner
        let width: CGFloat = 440
        for label in [status, step, commands] {
            label.preferredMaxLayoutWidth = width
        }
        commands.font = .monospacedSystemFont(ofSize: NSFont.smallSystemFontSize, weight: .regular)
        commands.isSelectable = true
        step.textColor = .secondaryLabelColor
        let heading = IntegrationsWindow.heading(title)
        view = NSStackView(views: [heading, status, button, step, commands, copyButton])
        view.orientation = .vertical
        view.alignment = .leading
        view.spacing = 6
        super.init()
        button.target = self
        button.action = #selector(pressed)
        copyButton.target = self
        copyButton.action = #selector(copyCommands)
    }

    /// A finding from `ensureCurrent`: a re-stamp asks for a restart; a failure is shown until the next connect. Codex
    /// awaiting trust again needs nothing here: the installer's status says so until Codex sends its next event.
    func notice(_ state: PluginState) {
        if state == .restartSessions {
            restart = true
        } else if state != .awaitingTrust {
            problem = state
        }
    }

    func busy(_ on: Bool) {
        button.isEnabled = !on
    }

    func result(_ result: PluginResult, connecting: Bool) {
        manual = result.state == .manualSteps ? result.commands : []
        restart = connecting && agent == AgentToken.claude.rawValue && result.state == .connected
        switch result.state {
        case .connected, .awaitingTrust, .notConnected: problem = nil
        default: problem = result.state
        }
    }

    func show(_ state: PluginState, heard: String) {
        connected = state != .notConnected
        let name = agent == AgentToken.claude.rawValue ? "claude" : "codex"
        switch problem ?? state {
        case .cliNotFound: status.stringValue = tr("Integrations_NotFound", "%1$@ was not found on this computer", name)
        case .updateCodex:
            status.stringValue = tr("Integrations_UpdateCodex", "Update Codex to version 0.159 or newer.")
        case .manualSteps:
            status.stringValue = tr(
                "Integrations_RunYourself", "Edgepad could not run this for you. Run these commands in a terminal:")
        case .awaitingTrust:
            status.stringValue = tr("Integrations_WaitingTrust", "Waiting for Codex to trust the plugin…")
        case .connected, .restartSessions:
            status.stringValue = tr("Integrations_Connected", "Connected · last heard %1$@", heard)
        default: status.stringValue = tr("Integrations_NotConnected", "Not connected")
        }
        button.title = connected ? tr("Integrations_Disconnect", "Disconnect") : tr("Integrations_Connect", "Connect")
        if state == .awaitingTrust {
            step.stringValue = tr("Integrations_TrustCodex", "In Codex, open /hooks and trust Edgepad.")
        } else if restart && connected {
            step.stringValue = tr(
                "Integrations_Restart", "Restart open sessions, or run /reload-plugins in Claude Code.")
        } else if !connected {
            step.stringValue = agent == AgentToken.claude.rawValue ? Self.claudeSteps : Self.codexSteps
        } else {
            step.stringValue = ""
        }
        step.isHidden = step.stringValue.isEmpty
        commands.stringValue = manual.joined(separator: "\n")
        commands.isHidden = manual.isEmpty
        copyButton.isHidden = manual.isEmpty
    }

    private static var claudeSteps: String {
        tr(
            "Integrations_ClaudeSteps",
            "1. Install Claude Code on this computer if you have not: claude.com/claude-code\n"
                + "2. Click Connect. Edgepad adds its plugin to Claude Code.\n"
                + "3. Restart open Claude Code sessions, or type /reload-plugins in them.\n"
                + "4. On your phone, open Controls > AI.")
    }

    private static var codexSteps: String {
        tr(
            "Integrations_CodexSteps",
            "1. Install Codex on this computer if you have not: developers.openai.com/codex\n"
                + "2. Click Connect. Edgepad adds its plugin to Codex.\n"
                + "3. In Codex, type /hooks and trust Edgepad. In the Codex app: Settings > Hooks > Review hooks.\n"
                + "4. On your phone, open Controls > AI. It appears once Codex sends its first event.")
    }

    @objc private func pressed() {
        owner?.connect(self, !connected)
    }

    @objc private func copyCommands() {
        owner?.copy(manual.joined(separator: "\n"))
    }
}
