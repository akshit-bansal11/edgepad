import AppKit
import EdgepadCore

/// What the For developers window needs from the rest of the app, filled by `MacDashboard`.
struct DevelopersHooks {
    let settings: IntegrationSettings
    let signIn: GitHubSignIn
    let gitlabSignIn: GitLabSignIn
    /// The signed-in GitHub login, for display only; nil when it cannot be read.
    let login: () async -> String?
    /// An AI tool's state from the saved switch.
    let status: (String) -> PluginState
    /// UTC minutes of the last hook message from the tool, -1 for never.
    let lastHeard: (String) -> Int64
    let connect: (String) async -> PluginResult
    let disconnect: (String) async -> PluginResult
    /// The installers' `ensureCurrent`, which also says when Codex must trust Edgepad again.
    let ensureCurrent: () async -> [(agent: String, state: PluginState)]
    /// Developer mode on or off.
    let setDeveloper: (Bool) -> Void
    /// An AI tool or GitHub was switched on or off (an `IntegrationSettings` key).
    let switched: (String, Bool) -> Void
    /// A token host was saved or signed out.
    let gitChanged: () -> Void
    let hosts: DevTokenHosts
}

/// The For developers window: the Developer mode switch, then AI tools and git hosts, one row each, built from
/// `DevProviders` (no screen keeps a tool list of its own). A row is a logo, a name, one status line and one button;
/// "How to connect" opens that row's steps under it, GitHub's sign-in code opens in its own row, and the token hosts
/// open a small form in their row. The twin of the Windows window. Non-modal; closing it stops a sign-in in progress
/// and clears the token field. Main thread only.
final class DevelopersWindow: NSObject, NSWindowDelegate {
    private static let width: CGFloat = 480
    private static let pad: CGFloat = 24
    private static let indent: CGFloat = 44

    private let hooks: DevelopersHooks
    private var window: NSWindow?
    private var rows: [DevRowControl] = []
    private let heading = NSTextField(labelWithString: tr("Integrations_ForDevelopers", "For developers"))
    private let subtitle = NSTextField(
        wrappingLabelWithString: tr("Dev_Subtitle", "Connect your AI tools and git hosts to see them on your phone."))
    private let hint = NSTextField(
        wrappingLabelWithString: tr("Dev_NeedDeveloper", "Turn on Developer mode to use these."))
    private let developerSwitch = NSButton(
        checkboxWithTitle: tr("Menu_DeveloperMode", "Developer mode"), target: nil, action: nil)
    private let aiHeading = NSTextField(labelWithString: "")
    private let gitHeading = NSTextField(labelWithString: "")
    private let closeButton = NSButton(title: tr("Common_Close", "Close"), target: nil, action: nil)
    private var stack = NSStackView()

    // Per AI tool: what the last Connect, Disconnect or re-stamp said, and what the row shows beyond its status line.
    private var problem: [String: PluginState] = [:]
    private var manual: [String: [String]] = [:]
    private var restart: Set<String> = []
    private var busy: Set<String> = []
    private var expanded: Set<String> = []
    private var editing: String?
    private var saving = false
    private var saveNote: DevSaveNote?

    // GitHub sign-in: the code while one is shown, and a count that tells a stale flow's continuation it was replaced.
    private var userCode = ""
    private var signingIn = false
    private var signInRun = 0
    private var gitHubProblem: String?

    // GitLab sign-in, the same way.
    private var gitlabCode = ""
    private var gitlabIn = false
    private var gitlabRun = 0
    private var gitlabProblem: String?

    init(hooks: DevelopersHooks) {
        self.hooks = hooks
        super.init()
    }

    /// Shows the window, or brings it to the front when it is open already.
    func show() {
        let first = window == nil
        if first {
            window = makeWindow()
        }
        NSApp.activate(ignoringOtherApps: true)
        refresh()
        if first {
            reveal()
        }
        window?.makeKeyAndOrderFront(nil)
        let ensureCurrent = hooks.ensureCurrent
        Task { @MainActor [weak self] in
            for notice in await ensureCurrent() {
                if notice.state == .restartSessions {
                    self?.restart.insert(notice.agent)
                } else if notice.state != .awaitingTrust {
                    self?.problem[notice.agent] = notice.state
                }
            }
            self?.refresh()
        }
    }

    /// Closes the window if it is open.
    func close() {
        window?.close()
    }

    func windowWillClose(_ notification: Notification) {
        if signingIn {
            hooks.signIn.cancel()
        }
        signInRun += 1
        signingIn = false
        if gitlabIn {
            hooks.gitlabSignIn.cancel()
        }
        gitlabRun += 1
        gitlabIn = false
        for row in rows {
            row.clearToken()
        }
        window = nil
    }

    // ---- building

    private func makeWindow() -> NSWindow {
        let providers = DevProviders.of(.ai) + DevProviders.of(.git)
        rows = providers.map { DevRowControl(provider: $0, owner: self) }
        heading.font = .boldSystemFont(ofSize: 18)
        developerSwitch.target = self
        developerSwitch.action = #selector(developerChanged)
        developerSwitch.setAccessibilityLabel(tr("Menu_DeveloperMode", "Developer mode"))
        subtitle.textColor = .secondaryLabelColor
        hint.font = .boldSystemFont(ofSize: NSFont.systemFontSize)
        for label in [subtitle, hint] {
            label.preferredMaxLayoutWidth = Self.width - Self.pad * 2
        }
        for label in [aiHeading, gitHeading] {
            label.font = .boldSystemFont(ofSize: NSFont.smallSystemFontSize)
            label.textColor = .secondaryLabelColor
        }
        aiHeading.stringValue = tr("Dev_SectionAi", "AI tools").uppercased()
        gitHeading.stringValue = tr("Dev_SectionGit", "Git hosts").uppercased()
        closeButton.target = self
        closeButton.action = #selector(closePressed)
        // Escape closes the form first and the window second.
        closeButton.keyEquivalent = "\u{1b}"

        let top = NSStackView(views: [heading, developerSwitch])
        top.orientation = .horizontal
        top.distribution = .fill
        heading.setContentHuggingPriority(.defaultLow, for: .horizontal)
        var views: [NSView] = [top, subtitle, hint, aiHeading]
        for row in rows where row.provider.kind == .ai {
            views.append(row.view)
        }
        views.append(gitHeading)
        for row in rows where row.provider.kind == .git {
            views.append(row.view)
        }
        views.append(closeButton)
        stack = NSStackView()
        stack.setViews(views, in: .top)
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 10
        stack.edgeInsets = NSEdgeInsets(top: Self.pad, left: Self.pad, bottom: Self.pad, right: Self.pad)
        stack.setCustomSpacing(18, after: hint)
        stack.setCustomSpacing(18, after: closeButton)
        stack.translatesAutoresizingMaskIntoConstraints = false
        top.translatesAutoresizingMaskIntoConstraints = false
        top.widthAnchor.constraint(equalToConstant: Self.width - Self.pad * 2).isActive = true

        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true
        scroll.drawsBackground = false
        scroll.documentView = stack
        let clip = scroll.contentView
        NSLayoutConstraint.activate([
            stack.topAnchor.constraint(equalTo: clip.topAnchor),
            stack.leadingAnchor.constraint(equalTo: clip.leadingAnchor),
            stack.widthAnchor.constraint(equalToConstant: Self.width),
        ])

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: Self.width, height: 600), styleMask: [.titled, .closable],
            backing: .buffered, defer: false)
        window.title = tr("Integrations_ForDevelopers", "For developers")
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.contentView = scroll
        window.center()
        return window
    }

    // The window fades in over the reveal time; with Reduce Motion it appears at once.
    private func reveal() {
        guard let window, !Motion.reduce else { return }
        window.alphaValue = 0
        NSAnimationContext.runAnimationGroup { context in
            context.duration = Motion.reveal
            window.animator().alphaValue = 1
        }
    }

    // ---- showing

    /// Reads every row again: the settings, the tools' states and the sign-in. Called after any change, and by the
    /// dashboard when a hook was heard or developer mode moved.
    func refresh() {
        guard window != nil else { return }
        let settings = hooks.settings
        let developerOn = settings.flag(IntegrationSettings.developer)
        developerSwitch.state = developerOn ? .on : .off
        hint.isHidden = developerOn
        for row in rows {
            show(row, developerOn: developerOn)
        }
        fit()
    }

    private func show(_ row: DevRowControl, developerOn: Bool) {
        let provider = row.provider
        let id = provider.id
        var input = DevRowInput()
        input.developerOn = developerOn
        input.connected = hooks.settings.flag(id)
        input.busy = busy.contains(id)
        if provider.kind == .ai {
            input.agent = problem[id] ?? hooks.status(id)
        }
        input.configured = GitHubApp.isConfigured(GitHubApp.clientId)
        let isGitLab = id == "gitlab"
        input.signingIn = isGitLab ? gitlabIn : signingIn
        input.codeShown = !(isGitLab ? gitlabCode : userCode).isEmpty
        input.saving = saving
        if id == IntegrationSettings.gitHub {
            input.signedIn = hooks.signIn.isSignedIn && hooks.settings.flag(id)
            input.account = hooks.settings.text(IntegrationSettings.gitHubLogin)
        } else {
            input.signedIn = hooks.hosts.isSignedIn(id)
            input.account = hooks.hosts.login(id)
        }
        let model = DevRowModel.view(provider, input)
        row.steps.stringValue = model.canHowTo && expanded.contains(id) ? Self.steps(id) : ""
        row.extra.stringValue = notes(id, busy: input.busy).joined(separator: "\n")
        row.show(
            model, text: lineText(model.line, provider), button: model.button.map(Self.title) ?? "",
            howTo: tr("Dev_HowTo", "How to connect"), open: expanded.contains(id), developerOn: developerOn)
        let commands = input.busy ? [] : manual[id] ?? []
        row.setCommands(commands, enabled: developerOn)
        if id == IntegrationSettings.gitHub {
            row.showCode(
                signingIn && !userCode.isEmpty ? tr("Integrations_Code", "Your code: %1$@", userCode) : nil,
                enabled: developerOn)
            row.chooseRepos.isHidden = !(input.signedIn && !signingIn)
            row.chooseRepos.isEnabled = developerOn
        }
        if isGitLab {
            row.showCode(
                gitlabIn && !gitlabCode.isEmpty ? tr("Integrations_Code", "Your code: %1$@", gitlabCode) : nil,
                enabled: developerOn)
            row.showSelfHost(!input.signedIn && !gitlabIn && editing != id, enabled: developerOn)
        }
        row.showForm(
            editing == id, error: saveNote.map(Self.saveText), saving: saving, enabled: developerOn)
    }

    // Sizes the window to its rows, up to the screen's height; past that it scrolls.
    private func fit() {
        guard let window else { return }
        stack.layoutSubtreeIfNeeded()
        let room = (window.screen ?? NSScreen.main)?.visibleFrame.height ?? 800
        let height = min(stack.fittingSize.height, room - 80)
        window.setContentSize(NSSize(width: Self.width, height: max(height, 200)))
    }

    private func lineText(_ line: DevLine, _ provider: DevProvider) -> String {
        switch line {
        case .working, .waiting: return tr("Integrations_Working", "Working…")
        case .notConfigured: return tr("Integrations_NoClientId", "Sign-in is not set up in this build.")
        case .notConnected:
            if provider.id == "gitlab", let gitlabProblem { return gitlabProblem }
            if provider.id == IntegrationSettings.gitHub {
                if let gitHubProblem { return gitHubProblem }
                if hooks.signIn.needsReSignIn {
                    return tr(
                        "Integrations_ReSignIn",
                        "Sign in again. Edgepad now uses read-only GitHub access. "
                            + "Revoke the old Edgepad at https://github.com/settings/applications.")
                }
            }
            return tr("Integrations_NotConnected", "Not connected")
        case .signedIn(let name):
            return tr("Integrations_SignedIn", "Signed in as %1$@", name.isEmpty ? provider.name : name)
        case .agent(let state): return agentText(state, provider)
        }
    }

    private func agentText(_ state: PluginState, _ provider: DevProvider) -> String {
        switch state {
        case .cliNotFound: return tr("Integrations_NotFound", "%1$@ was not found on this computer", provider.name)
        case .updateCodex: return tr("Integrations_UpdateCodex", "Update Codex to version 0.159 or newer.")
        case .manualSteps:
            return tr(
                "Integrations_RunYourself", "Edgepad could not run this for you. Run these commands in a terminal:")
        case .awaitingTrust: return tr("Integrations_WaitingTrust", "Waiting for Codex to trust the plugin…")
        case .connected, .restartSessions:
            return tr(
                "Integrations_Connected", "Connected · last heard %1$@", heard(hooks.lastHeard(provider.id)))
        default: return tr("Integrations_NotConnected", "Not connected")
        }
    }

    // What an AI row says under its status line: the Codex trust step, or a restart.
    private func notes(_ id: String, busy: Bool) -> [String] {
        guard !busy else { return [] }
        var notes: [String] = []
        if (problem[id] ?? hooks.status(id)) == .awaitingTrust {
            notes.append(tr("Integrations_TrustCodex", "In Codex, open /hooks and trust Edgepad."))
        }
        if restart.contains(id) && hooks.settings.flag(id) {
            notes.append(
                tr("Integrations_Restart", "Restart open sessions, or run /reload-plugins in Claude Code."))
        }
        if (problem[id] ?? hooks.status(id)) == .manualSteps, let commands = manual[id], !commands.isEmpty {
            notes.append(commands.joined(separator: "\n"))
        }
        return notes
    }

    static func title(_ action: DevAction) -> String {
        switch action {
        case .connect: return tr("Integrations_Connect", "Connect")
        case .disconnect: return tr("Integrations_Disconnect", "Disconnect")
        case .signIn: return tr("Dev_SignIn", "Sign in")
        case .signOut: return tr("Integrations_SignOut", "Sign out")
        case .cancel: return tr("Common_Cancel", "Cancel")
        case .add: return tr("Dev_Add", "Add…")
        }
    }

    /// The sentence for a failed token save.
    static func saveText(_ note: DevSaveNote) -> String {
        switch note {
        case .invalid:
            return tr("Dev_SaveInvalid", "Check the server address, the name and the token, then try again.")
        case .unauthorized: return tr("Dev_SaveUnauthorized", "The server did not accept this token.")
        case .forbidden:
            return tr("Dev_SaveForbidden", "This token has no permission to read. Create one with read access.")
        case .notFound: return tr("Dev_SaveNotFound", "No server found at that address.")
        case .network: return tr("Dev_SaveNetwork", "Could not reach the server. Check your connection.")
        case .failed: return tr("Dev_SaveFailed", "Could not save the token. Try again.")
        }
    }

    /// The setup steps a provider's "How to connect" opens; empty for GitLab, which has none.
    static func steps(_ id: String) -> String {
        switch id {
        case "claude":
            return tr(
                "Integrations_ClaudeSteps",
                "1. Install Claude Code on this computer if you have not: claude.com/claude-code\n"
                    + "2. Click Connect. Edgepad adds its plugin to Claude Code.\n"
                    + "3. Restart open Claude Code sessions, or type /reload-plugins in them.\n"
                    + "4. On your phone, open Controls > AI.")
        case "codex":
            return tr(
                "Integrations_CodexSteps",
                "1. Install Codex on this computer if you have not: developers.openai.com/codex\n"
                    + "2. Click Connect. Edgepad adds its plugin to Codex.\n"
                    + "3. In Codex, type /hooks and trust Edgepad. In the Codex app: Settings > Hooks > Review hooks.\n"
                    + "4. On your phone, open Controls > AI. It appears once Codex sends its first event.")
        case "gemini":
            return tr(
                "Dev_GeminiSteps",
                "1. Install Gemini CLI on this computer if you have not.\n"
                    + "2. Click Connect. Edgepad adds its hooks to your Gemini CLI settings.\n"
                    + "3. Restart open Gemini CLI sessions.")
        case "opencode":
            return tr(
                "Dev_OpenCodeSteps",
                "1. Install OpenCode on this computer if you have not.\n"
                    + "2. Click Connect. Edgepad adds its plugin to the OpenCode plugins folder.\n"
                    + "3. Restart open OpenCode sessions.")
        case "github":
            return tr(
                "Integrations_GitHubSteps",
                "1. Click Sign in. A github.com page opens.\n"
                    + "2. Enter the code shown here and click Authorize.\n"
                    + "3. On your phone, open Controls > GitHub.")
        case "gitea":
            return tr(
                "Dev_GiteaSteps",
                "1. On your server, open Settings > Applications and create an access token with read access.\n"
                    + "2. Click Add, check the server address (Codeberg is the default) and paste the token.")
        case "bitbucket":
            return tr(
                "Dev_BitbucketSteps",
                "1. In your Atlassian account, create an API token with read access to Bitbucket.\n"
                    + "2. Click Add, enter your Bitbucket email and paste the token.")
        case "gitee":
            return tr(
                "Dev_GiteeSteps",
                "1. On gitee.com, open Settings > Personal access tokens and create a token with read access.\n"
                    + "2. Click Add and paste the token.")
        default: return ""
        }
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

    // ---- actions, called by the rows

    @objc private func developerChanged() {
        hooks.setDeveloper(developerSwitch.state == .on)
        refresh()
    }

    @objc private func closePressed() {
        if editing != nil {
            closeForm()
        } else {
            window?.performClose(nil)
        }
    }

    func toggleSteps(_ id: String) {
        if expanded.contains(id) {
            expanded.remove(id)
        } else {
            expanded.insert(id)
        }
        refresh()
    }

    func pressed(_ provider: DevProvider) {
        let id = provider.id
        switch id {
        case IntegrationSettings.gitHub:
            if signingIn {
                hooks.signIn.cancel()
            } else if hooks.signIn.isSignedIn && hooks.settings.flag(id) {
                signOutGitHub()
            } else {
                startSignIn()
            }
        case "gitlab":
            if gitlabIn {
                hooks.gitlabSignIn.cancel()
            } else if hooks.hosts.isSignedIn(id) && hooks.settings.flag(id) {
                hooks.hosts.signOut(id)
                hooks.gitChanged()
                refresh()
            } else {
                startGitLabSignIn()
            }
        case "gitea", "bitbucket", "gitee":
            if hooks.hosts.isSignedIn(id) {
                hooks.hosts.signOut(id)
                hooks.gitChanged()
                refresh()
            } else {
                openForm(id)
            }
        default:
            if provider.kind == .ai {
                run(id)
            }
        }
    }

    func copy(_ text: String) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(text, forType: .string)
    }

    func copyCode(for id: String) {
        copy(id == "gitlab" ? gitlabCode : userCode)
    }

    /// Opens the page the code is typed on: GitHub's or GitLab's, each a constant.
    func openDevicePage(for id: String) {
        if let url = URL(string: id == "gitlab" ? GitLabApp.devicePage : GitHubApp.devicePage) {
            NSWorkspace.shared.open(url)
        }
    }

    /// Opens the page where the user chooses which repositories the read-only GitHub App may read.
    func openInstallPage() {
        if let url = URL(string: GitHubApp.installUrl) {
            NSWorkspace.shared.open(url)
        }
    }

    // ---- AI tools

    /// Connect or Disconnect, whichever the row offers; the row says "Working..." until it is done.
    private func run(_ id: String) {
        guard !busy.contains(id) else { return }
        let connecting = !hooks.settings.flag(id)
        busy.insert(id)
        refresh()
        let hooks = hooks
        Task { @MainActor [weak self] in
            let result: PluginResult
            if connecting {
                result = await hooks.connect(id)
            } else {
                result = await hooks.disconnect(id)
            }
            guard let self else { return }
            self.busy.remove(id)
            self.manual[id] = result.state == .manualSteps ? result.commands : []
            if connecting && result.state == .connected && id != AgentToken.codex.rawValue {
                self.restart.insert(id)
            } else {
                self.restart.remove(id)
            }
            switch result.state {
            case .connected, .awaitingTrust, .notConnected: self.problem[id] = nil
            default: self.problem[id] = result.state
            }
            hooks.switched(id, hooks.settings.flag(id))
            self.refresh()
        }
    }

    // ---- GitHub

    private func signOutGitHub() {
        signInRun += 1
        hooks.signIn.signOut()
        hooks.settings.setFlag(IntegrationSettings.gitHub, false)
        hooks.settings.set(IntegrationSettings.gitHubLogin, "")
        hooks.switched(IntegrationSettings.gitHub, false)
        refresh()
    }

    /// The device flow: ask for a code, show it with Copy and the page to type it on, and wait. A newer flow replaces
    /// an older one, whose continuation then does nothing.
    private func startSignIn() {
        if gitlabIn {
            gitlabRun += 1
            hooks.gitlabSignIn.cancel()
            gitlabIn = false
            gitlabCode = ""
        }
        signInRun += 1
        let run = signInRun
        gitHubProblem = nil
        signingIn = true
        userCode = ""
        refresh()
        let hooks = hooks
        Task { @MainActor [weak self] in
            let code = await hooks.signIn.start()
            guard let self, self.signInRun == run else { return }
            guard code.state == .started else {
                self.endSignIn(code.state)
                return
            }
            self.userCode = code.userCode
            self.refresh()
            let end = await hooks.signIn.wait()
            guard self.signInRun == run else { return }
            if end == .signedIn {
                let login = await hooks.login()
                hooks.settings.set(IntegrationSettings.gitHubLogin, login ?? "")
                hooks.settings.setFlag(IntegrationSettings.gitHub, true)
                hooks.switched(IntegrationSettings.gitHub, true)
            }
            guard self.signInRun == run else { return }
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

    // ---- GitLab

    /// GitLab's device flow, the same way as GitHub's: ask for a code, show it with Copy and the page to type it on,
    /// and wait. A newer flow (or a sign-out) replaces an older one, whose continuation then does nothing. The flow
    /// clears a saved server address.
    private func startGitLabSignIn() {
        if signingIn {
            signInRun += 1
            hooks.signIn.cancel()
            signingIn = false
            userCode = ""
        }
        gitlabRun += 1
        let run = gitlabRun
        gitlabProblem = nil
        gitlabIn = true
        gitlabCode = ""
        refresh()
        let hooks = hooks
        Task { @MainActor [weak self] in
            let code = await hooks.gitlabSignIn.start()
            guard let self, self.gitlabRun == run else { return }
            guard code.state == .started else {
                self.endGitLabSignIn(code.state)
                return
            }
            self.gitlabCode = code.userCode
            self.refresh()
            let end = await hooks.gitlabSignIn.wait()
            guard self.gitlabRun == run else { return }
            if end == .signedIn {
                await hooks.hosts.finishDeviceSignIn("gitlab")
                hooks.gitChanged()
            }
            guard self.gitlabRun == run else { return }
            self.endGitLabSignIn(end)
        }
    }

    private func endGitLabSignIn(_ state: SignInState) {
        gitlabIn = false
        gitlabCode = ""
        switch state {
        case .signedIn, .cancelled: gitlabProblem = nil
        default: gitlabProblem = tr("Integrations_SignInFailed", "Sign-in did not finish. Try again.")
        }
        refresh()
    }

    // ---- token hosts

    func openForm(_ id: String) {
        editing = id
        saveNote = nil
        let url = hooks.hosts.url(id)
        rows.first { $0.provider.id == id }?.prepareForm(
            url: url.isEmpty && id == "gitea" ? GiteaHost.defaultUrl : url, user: hooks.hosts.user(id))
        expanded.remove(id)
        refresh()
        rows.first { $0.provider.id == id }?.focusForm()
    }

    private func closeForm() {
        rows.first { $0.provider.id == editing }?.clearToken()
        editing = nil
        saveNote = nil
        refresh()
    }

    /// Checks the pasted token with its host and saves it; the field is emptied whatever the answer.
    func save() {
        guard let id = editing, !saving, let row = rows.first(where: { $0.provider.id == id }) else { return }
        let (token, url, user) = row.formValues()
        saving = true
        saveNote = nil
        refresh()
        let hooks = hooks
        Task { @MainActor [weak self] in
            let result = await hooks.hosts.save(id, token: token, url: url, user: user)
            guard let self else { return }
            self.saving = false
            row.clearToken()
            if let note = DevRowModel.note(result.status) {
                self.saveNote = note
            } else {
                self.editing = nil
                hooks.gitChanged()
            }
            self.refresh()
        }
    }

    func cancelForm() {
        closeForm()
    }
}

/// One provider's controls: logo, name, status line with its dot, the How to connect link, one button, then (under it,
/// indented) the steps, notes, Copy, GitHub's code panel and a token host's form.
final class DevRowControl: NSObject, NSTextFieldDelegate {
    let provider: DevProvider
    let view = NSStackView()
    let steps = NSTextField(wrappingLabelWithString: "")
    let extra = NSTextField(wrappingLabelWithString: "")
    let chooseRepos = NSButton(
        title: tr("Integrations_ChooseRepos", "Choose repos on GitHub"), target: nil, action: nil)
    let selfHost = NSButton(
        title: tr("Dev_GitLabServer", "Add a self-hosted server…"), target: nil, action: nil)

    private weak var owner: DevelopersWindow?
    private let dot = DotView()
    private let name = NSTextField(labelWithString: "")
    private let status = NSTextField(wrappingLabelWithString: "")
    private let howTo = NSButton(title: "", target: nil, action: nil)
    private let button = NSButton(title: "", target: nil, action: nil)
    private let copyCommands = NSButton(title: tr("Integrations_Copy", "Copy"), target: nil, action: nil)
    private var commands: [String] = []
    private let detail = NSStackView()

    // GitHub's code panel.
    private let codePanel = NSStackView()
    private let codeLabel = NSTextField(labelWithString: "")
    private let waiting = NSTextField(
        wrappingLabelWithString: tr("Integrations_Waiting", "Waiting for you to approve in the browser…"))
    private let openPage = NSButton(
        title: tr("Integrations_OpenPage", "Open github.com/login/device"), target: nil, action: nil)
    private let copyCode = NSButton(title: tr("Integrations_Copy", "Copy"), target: nil, action: nil)

    // A token host's form.
    private let form = NSStackView()
    private let urlLabel = NSTextField(labelWithString: tr("Dev_FieldUrl", "Server address"))
    private let urlField = NSTextField()
    private let userLabel = NSTextField(labelWithString: tr("Dev_FieldUser", "Username or email"))
    private let userField = NSTextField()
    private let tokenLabel = NSTextField(labelWithString: tr("Dev_FieldToken", "Access token"))
    private let tokenField = NSSecureTextField()
    private let error = NSTextField(wrappingLabelWithString: "")
    private let hint = NSTextField(
        wrappingLabelWithString: tr(
            "Dev_GitLabTokenHint",
            "On your server, create a personal access token with only the read_api scope, then paste it below."))
    private let saveButton = NSButton(title: tr("Common_Save", "Save"), target: nil, action: nil)
    private let cancelButton = NSButton(title: tr("Common_Cancel", "Cancel"), target: nil, action: nil)

    init(provider: DevProvider, owner: DevelopersWindow) {
        self.provider = provider
        self.owner = owner
        super.init()
        build()
    }

    private func build() {
        let wide: CGFloat = 480 - 48
        let logo = LogoTile(provider: provider)
        name.stringValue = provider.name
        name.font = .boldSystemFont(ofSize: 14)
        status.font = .systemFont(ofSize: 12)
        status.textColor = .secondaryLabelColor
        status.preferredMaxLayoutWidth = 230
        howTo.isBordered = false
        howTo.font = .systemFont(ofSize: 12)
        howTo.contentTintColor = .linkColor
        howTo.target = self
        howTo.action = #selector(howToPressed)
        button.bezelStyle = .rounded
        button.target = self
        button.action = #selector(buttonPressed)
        button.setAccessibilityHelp(provider.name)
        howTo.setAccessibilityHelp(provider.name)
        dot.setAccessibilityElement(false)

        let line = NSStackView(views: [dot, status, howTo])
        line.orientation = .horizontal
        line.spacing = 6
        line.alignment = .centerY
        let info = NSStackView(views: [name, line])
        info.orientation = .vertical
        info.alignment = .leading
        info.spacing = 2
        info.setContentHuggingPriority(.defaultLow, for: .horizontal)
        button.setContentHuggingPriority(.required, for: .horizontal)
        let head = NSStackView(views: [logo, info, button])
        head.orientation = .horizontal
        head.alignment = .centerY
        head.spacing = 12
        head.translatesAutoresizingMaskIntoConstraints = false
        head.widthAnchor.constraint(equalToConstant: wide).isActive = true

        let detailWidth = wide - 44
        steps.textColor = .secondaryLabelColor
        hint.textColor = .secondaryLabelColor
        hint.font = .systemFont(ofSize: 12)
        hint.preferredMaxLayoutWidth = 300
        for label in [steps, extra, waiting, error] {
            label.preferredMaxLayoutWidth = detailWidth
        }
        steps.stringValue = ""
        error.textColor = .systemRed
        error.font = .systemFont(ofSize: 12)
        copyCommands.target = self
        copyCommands.action = #selector(copyPressed)
        chooseRepos.isBordered = false
        chooseRepos.font = .systemFont(ofSize: 12)
        chooseRepos.contentTintColor = .linkColor
        chooseRepos.target = self
        chooseRepos.action = #selector(chooseReposPressed)
        selfHost.isBordered = false
        selfHost.font = .systemFont(ofSize: 12)
        selfHost.contentTintColor = .linkColor
        selfHost.target = self
        selfHost.action = #selector(selfHostPressed)
        if provider.id == "gitlab" {
            openPage.title = tr("Dev_GitLabOpenPage", "Open gitlab.com/oauth/device")
        }
        buildCodePanel()
        buildForm()
        detail.setViews([steps, extra, copyCommands, codePanel, chooseRepos, selfHost, form], in: .top)
        detail.orientation = .vertical
        detail.alignment = .leading
        detail.spacing = 8
        detail.edgeInsets = NSEdgeInsets(top: 0, left: 44, bottom: 0, right: 0)

        view.setViews([head, detail], in: .top)
        view.orientation = .vertical
        view.alignment = .leading
        view.spacing = 8
        view.setAccessibilityLabel(provider.name)
        view.setAccessibilityRole(.group)
        steps.isHidden = true
        extra.isHidden = true
        copyCommands.isHidden = true
        codePanel.isHidden = true
        chooseRepos.isHidden = true
        selfHost.isHidden = true
        form.isHidden = true
    }

    private func buildCodePanel() {
        codeLabel.font = .monospacedSystemFont(ofSize: 22, weight: .semibold)
        codeLabel.isSelectable = true
        openPage.target = self
        openPage.action = #selector(openPagePressed)
        copyCode.target = self
        copyCode.action = #selector(copyCodePressed)
        let buttons = NSStackView(views: [openPage, copyCode])
        buttons.spacing = 8
        codePanel.setViews([codeLabel, waiting, buttons], in: .top)
        codePanel.orientation = .vertical
        codePanel.alignment = .leading
        codePanel.spacing = 6
    }

    private func buildForm() {
        for field in [urlField, userField, tokenField] {
            field.delegate = self
            field.translatesAutoresizingMaskIntoConstraints = false
            field.widthAnchor.constraint(equalToConstant: 300).isActive = true
        }
        urlField.setAccessibilityLabel(urlLabel.stringValue)
        userField.setAccessibilityLabel(userLabel.stringValue)
        tokenField.setAccessibilityLabel(tokenLabel.stringValue)
        saveButton.bezelStyle = .rounded
        saveButton.target = self
        saveButton.action = #selector(savePressed)
        cancelButton.target = self
        cancelButton.action = #selector(cancelPressed)
        let buttons = NSStackView(views: [saveButton, cancelButton])
        buttons.spacing = 8
        var views: [NSView] = []
        if provider.id == "gitea" || provider.id == "gitlab" { views += [urlLabel, urlField] }
        if provider.id == "gitlab" { views.append(hint) }
        if provider.id == "bitbucket" { views += [userLabel, userField] }
        views += [tokenLabel, tokenField, error, buttons]
        form.setViews(views, in: .top)
        form.orientation = .vertical
        form.alignment = .leading
        form.spacing = 4
    }

    // ---- updating

    func show(
        _ row: DevRowView, text: String, button title: String, howTo howToTitle: String, open: Bool,
        developerOn: Bool
    ) {
        status.stringValue = text
        status.textColor = row.calm ? .secondaryLabelColor : .labelColor
        dot.isHidden = !row.dot
        button.isHidden = row.button == nil
        button.title = title
        button.isEnabled = row.buttonEnabled
        howTo.isHidden = !row.canHowTo
        let rtl = NSApp.userInterfaceLayoutDirection == .rightToLeft
        // The arrow: down when the steps are open, else pointing along the reading direction.
        howTo.title = howToTitle + " " + (open ? "\u{2304}" : rtl ? "\u{2039}" : "\u{203A}")
        howTo.setAccessibilityLabel(howToTitle)
        howTo.setAccessibilityExpanded(open)
        howTo.isEnabled = developerOn
        steps.isHidden = steps.stringValue.isEmpty
        extra.isHidden = extra.stringValue.isEmpty
        view.setAccessibilityValue(text)
    }

    func setCommands(_ commands: [String], enabled: Bool) {
        self.commands = commands
        copyCommands.isHidden = commands.isEmpty
        copyCommands.isEnabled = enabled
    }

    func showCode(_ text: String?, enabled: Bool) {
        codePanel.isHidden = text == nil
        codeLabel.stringValue = text ?? ""
        codeLabel.setAccessibilityLabel(text)
        openPage.isEnabled = enabled
        copyCode.isEnabled = enabled
    }

    func showSelfHost(_ visible: Bool, enabled: Bool) {
        selfHost.isHidden = !visible
        selfHost.isEnabled = enabled
    }

    func showForm(_ open: Bool, error message: String?, saving: Bool, enabled: Bool) {
        form.isHidden = !open
        error.stringValue = message ?? ""
        error.isHidden = message == nil
        saveButton.isEnabled = enabled && !saving
        for field in [urlField, userField, tokenField] {
            field.isEnabled = !saving
        }
    }

    func prepareForm(url: String, user: String) {
        urlField.stringValue = url
        userField.stringValue = user
        tokenField.stringValue = ""
    }

    func focusForm() {
        let first: NSTextField
        switch provider.id {
        case "gitea", "gitlab": first = urlField
        case "bitbucket": first = userField
        default: first = tokenField
        }
        first.window?.makeFirstResponder(first)
    }

    func formValues() -> (token: String, url: String, user: String) {
        (tokenField.stringValue, urlField.stringValue, userField.stringValue)
    }

    func clearToken() {
        tokenField.stringValue = ""
    }

    // ---- events

    func control(_ control: NSControl, textView: NSTextView, doCommandBy selector: Selector) -> Bool {
        guard selector == #selector(NSResponder.insertNewline(_:)) else { return false }
        owner?.save()
        return true
    }

    @objc private func buttonPressed() { owner?.pressed(provider) }
    @objc private func howToPressed() { owner?.toggleSteps(provider.id) }
    @objc private func copyPressed() { owner?.copy(commands.joined(separator: "\n")) }
    @objc private func openPagePressed() { owner?.openDevicePage(for: provider.id) }
    @objc private func copyCodePressed() { owner?.copyCode(for: provider.id) }
    @objc private func selfHostPressed() { owner?.openForm(provider.id) }
    @objc private func chooseReposPressed() { owner?.openInstallPage() }
    @objc private func savePressed() { owner?.save() }
    @objc private func cancelPressed() { owner?.cancelForm() }
}

/// The green dot before a connected status line.
private final class DotView: NSView {
    override var intrinsicContentSize: NSSize { NSSize(width: 8, height: 8) }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.systemGreen.setFill()
        NSBezierPath(ovalIn: bounds).fill()
    }
}

/// A provider's logo in a 32 pt rounded tile: GitHub's mark drawn from its path, otherwise a tile in the provider's
/// brand colour with its first letter.
private final class LogoTile: NSView {
    private let provider: DevProvider

    init(provider: DevProvider) {
        self.provider = provider
        super.init(frame: NSRect(x: 0, y: 0, width: 32, height: 32))
        setAccessibilityLabel(provider.name)
        setAccessibilityRole(.image)
        if provider.logoId == "github" {
            let mark = NSImageView(image: BrandMarks.image(path: BrandMarks.gitHub, grid: 16, size: 20))
            mark.contentTintColor = .labelColor
            mark.frame = NSRect(x: 6, y: 6, width: 20, height: 20)
            addSubview(mark)
        }
    }

    required init?(coder: NSCoder) {
        nil
    }

    override var intrinsicContentSize: NSSize { NSSize(width: 32, height: 32) }

    override func draw(_ dirtyRect: NSRect) {
        let tile = NSBezierPath(roundedRect: bounds, xRadius: 8, yRadius: 8)
        if provider.logoId == "github" {
            NSColor.quaternaryLabelColor.setFill()
            tile.fill()
            return
        }
        Self.brand(provider.logoId).setFill()
        tile.fill()
        let letter = NSAttributedString(
            string: String(provider.name.prefix(1)).uppercased(),
            attributes: [.font: NSFont.boldSystemFont(ofSize: 16), .foregroundColor: NSColor.white])
        let size = letter.size()
        letter.draw(at: NSPoint(x: (bounds.width - size.width) / 2, y: (bounds.height - size.height) / 2))
    }

    private static func brand(_ logoId: String) -> NSColor {
        func rgb(_ r: Int, _ g: Int, _ b: Int) -> NSColor {
            NSColor(srgbRed: CGFloat(r) / 255, green: CGFloat(g) / 255, blue: CGFloat(b) / 255, alpha: 1)
        }
        switch logoId {
        case "claude": return rgb(0xD9, 0x77, 0x57)
        case "openai": return rgb(0x10, 0xA3, 0x7F)
        case "gemini": return rgb(0x42, 0x85, 0xF4)
        case "opencode": return rgb(0x4B, 0x4B, 0x4B)
        case "gitlab": return rgb(0xFC, 0x6D, 0x26)
        case "gitea": return rgb(0x60, 0x99, 0x26)
        case "bitbucket": return rgb(0x26, 0x84, 0xFF)
        case "gitee": return rgb(0xC7, 0x1D, 0x23)
        default: return .controlAccentColor
        }
    }
}
