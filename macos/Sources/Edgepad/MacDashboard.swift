import EdgepadCore
import Foundation

/// The Mac app's process-wide dashboard: the integration switches, the plugin installer, and the pieces each
/// integration needs, made only while it is on and let go when it goes off (for Claude Code and Codex: the boards, the
/// approval broker, hook.json and the hook socket, and the Codex and Claude readers; the weather and GitHub clients; the
/// stats source). One `DashboardLink` per live phone session draws on them. With every integration off nothing is made:
/// no socket, no hook.json, no timer, and FEATURES is what it always was. Main queue only.
final class MacDashboard {
    private static let agentKeys = [IntegrationSettings.claude, IntegrationSettings.codex]

    let settings: IntegrationSettings
    /// Live phones, counted by their `DashboardLink`s; the broker and the readers ask it from their own threads.
    let phones = LinkedPhones()
    /// Told after the pieces changed, so the phone in use is given a new host and FEATURES again.
    var changed: (() -> Void)?
    /// Told when a re-stamp changed Codex's hooks file, which Codex then skips until the owner trusts it again.
    var codexRetrust: (() -> Void)?

    private let folder: String
    private let installer: PluginInstaller
    private let secrets = KeychainSecretStore()
    private lazy var signIn = GitHubSignIn(store: secrets)
    private lazy var usage = UsageStore(path: folder + "/" + UsageStore.fileName, now: Date())
    private var agents: Agents?
    private var codex: CodexWatcher?
    private var claude: ClaudeSpend?
    private var weather: WeatherClient?
    private var gitHub: GitHubApi?
    private var stats: MacStats?
    private var parts = DashboardParts()
    private var window: IntegrationsWindow?

    /// - Parameter version: This app's version, stamped into the plugin copies.
    init(version: String) {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support"
        folder = support + "/Edgepad"
        settings = IntegrationSettings(path: folder + "/" + IntegrationSettings.fileName)
        // bundle.sh copies plugins/edgepad here; a build without it fails Connect with a logged reason, nothing more.
        let plugin = (Bundle.main.resourcePath ?? "") + "/plugins/edgepad"
        installer = PluginInstaller(
            bundle: DirectoryPluginSource(root: plugin), settings: settings, pluginsRoot: PluginInstaller.defaultRoot(),
            version: version)
    }

    /// At launch: makes what the saved switches ask for, and re-stamps a connected agent's copy when the app moved or
    /// was updated (the Integrations window says so when it opens).
    func start() {
        apply()
        guard Self.agentKeys.contains(where: settings.flag) else { return }
        Task { @MainActor [weak self] in _ = await self?.ensureCurrent() }
    }

    /// The installer's `ensureCurrent`, saying once, through `codexRetrust`, when Codex must trust Edgepad again.
    @MainActor func ensureCurrent() async -> [(agent: String, state: PluginState)] {
        let notices = await installer.ensureCurrent()
        if notices.contains(where: { $0.agent == AgentToken.codex.rawValue && $0.state == .awaitingTrust }) {
            codexRetrust?()
        }
        return notices
    }

    /// On quit: closes the hook socket and deletes hook.json, and stops the readers.
    func stop() {
        codex?.dispose()
        claude?.dispose()
        agents?.dispose()
        codex = nil
        claude = nil
        agents = nil
    }

    /// This phone's side of the dashboard, writing through `send` and `blobs`.
    func newLink(send: @escaping (Frame) -> Void, blobs: BlobSender) -> DashboardLink {
        DashboardLink(
            phones: phones,
            makeHost: { [unowned self] in
                IntegrationHost(
                    send: send, blobs: blobs, settings: self.settings, parts: self.parts, stats: self.stats)
            },
            unsupported: { [weak self] in self?.unsupported() ?? [] })
    }

    /// Opens the Integrations window, or brings it to the front.
    func showWindow() {
        if window == nil {
            let secrets = secrets
            let usage = usage
            let hooks = IntegrationsHooks(
                settings: settings, installer: installer, signIn: signIn,
                login: { await GitHubApi(store: secrets).login() },
                lastHeard: { usage.lastHeard($0) },
                switched: { [weak self] key, on in self?.switched(key, on) },
                ensureCurrent: { [weak self] in await self?.ensureCurrent() ?? [] })
            window = IntegrationsWindow(hooks: hooks)
        }
        window?.show()
    }

    /// Makes the pieces the switches now ask for and lets go of the rest, then tells the phone in use.
    private func apply() {
        let agentsOn = Self.agentKeys.contains(where: settings.flag)
        var oldAgents: Agents?
        if agentsOn && agents == nil {
            agents = openAgents()
        } else if !agentsOn {
            oldAgents = agents
            agents = nil
        }

        let settings = settings
        let phones = phones
        var oldCodex: CodexWatcher?
        if settings.flag(IntegrationSettings.codex), let made = agents {
            codex =
                codex
                ?? CodexWatcher(
                    settingsOn: { settings.flag(IntegrationSettings.codex) }, phoneLinked: { phones.any },
                    sessions: made.sessions, limits: made.limits, spend: made.spend, root: CodexWatcher.defaultRoot(),
                    cachePath: folder + "/" + CodexWatcher.cacheName)
        } else {
            oldCodex = codex
            codex = nil
        }
        var oldClaude: ClaudeSpend?
        if settings.flag(IntegrationSettings.claude), let made = agents {
            claude =
                claude
                ?? ClaudeSpend(
                    settingsOn: { settings.flag(IntegrationSettings.claude) }, phoneLinked: { phones.any },
                    spend: made.spend, root: ClaudeSpend.defaultRoot(), cachePath: folder + "/" + ClaudeSpend.cacheName)
        } else {
            oldClaude = claude
            claude = nil
        }
        weather = settings.flag(IntegrationSettings.weather) ? weather ?? WeatherClient() : nil
        gitHub = settings.flag(IntegrationSettings.gitHub) ? gitHub ?? GitHubApi(store: secrets) : nil
        // Kept until quit once made: a reporter of a host being replaced may still be reading it.
        if settings.flag(IntegrationSettings.stats) && stats == nil {
            stats = MacStats()
        }
        let signIn = signIn
        parts = DashboardParts(
            sessions: agents?.sessions, limits: agents?.limits, spend: agents?.spend, broker: agents?.broker,
            codex: codex, claude: claude, weather: weather, gitHub: gitHub,
            gitHubSignedIn: gitHub == nil ? nil : { signIn.isSignedIn })

        // The phone moves to the new pieces before the old ones close under it.
        changed?()
        oldCodex?.dispose()
        oldClaude?.dispose()
        oldAgents?.dispose()
    }

    // An integration was switched on or off, or its settings changed. A new token whenever something is switched on.
    private func switched(_ key: String, _ on: Bool) {
        apply()
        if on {
            agents?.endpoint.rotate()
        }
    }

    // TEXT 24 entries for agents switched on whose hook socket could not be opened.
    private func unsupported() -> [String] {
        guard parts.sessions == nil else { return [] }
        return Self.agentKeys.filter(settings.flag).map { $0 + ":" + UnavailableReason.unsupported.rawValue }
    }

    // The hook socket, hook.json and what they feed. Nil (logged) when the socket or the file cannot be made.
    private func openAgents() -> Agents? {
        do {
            let listener = try UnixHookListener(directory: HookEndpoint.socketDirectory())
            let endpoint: HookEndpoint
            do {
                endpoint = try HookEndpoint(file: HookEndpoint.defaultFile(), endpoint: listener.endpoint)
            } catch {
                listener.close()
                throw error
            }
            let phones = phones
            let sessions = SessionBoard()
            let limits = LimitsBoard(store: usage)
            let spend = SpendBoard(store: usage)
            let broker = PermissionBroker(board: sessions, phoneLinked: { phones.any })
            let server = HookServer(
                listener: listener, token: { endpoint.token }, broker: broker, sessions: sessions, limits: limits,
                spend: spend, usage: usage, settings: settings)
            // The person trusted the hooks in Codex: an open Integrations window drops its "trust the hooks" step.
            server.codexTrusted = { [weak self] in
                DispatchQueue.main.async { self?.window?.refresh() }
            }
            server.start()
            return Agents(
                sessions: sessions, limits: limits, spend: spend, broker: broker, endpoint: endpoint, server: server)
        } catch {
            let reason = (error as? HookSocketError)?.reason ?? "unknown"
            Log.write("The Claude Code and Codex integrations are not offered: \(reason)")
            return nil
        }
    }

    // Claude Code's and Codex's shared pieces; the server goes first, then hook.json.
    private struct Agents {
        let sessions: SessionBoard
        let limits: LimitsBoard
        let spend: SpendBoard
        let broker: PermissionBroker
        let endpoint: HookEndpoint
        let server: HookServer

        func dispose() {
            server.dispose()
            endpoint.dispose()
        }
    }
}
