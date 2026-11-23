import Foundation

/// How many phones are linked now, for the permission broker and the agent readers. Thread-safe.
public final class LinkedPhones {
    private let gate = NSLock()
    private var count = 0

    public init() {}

    public var any: Bool {
        gate.lock()
        defer { gate.unlock() }
        return count > 0
    }

    public func add() {
        gate.lock()
        defer { gate.unlock() }
        count += 1
    }

    public func remove() {
        gate.lock()
        defer { gate.unlock() }
        count = max(0, count - 1)
    }
}

/// The process-wide dashboard pieces the app holds while the matching integration is on; each is nil while it is off.
/// One set is shared by every `IntegrationHost`: it takes the boards' and the broker's callbacks and hands them to each
/// host made on it, so a host never sets a callback of its own.
public final class DashboardParts {
    /// Claude Code or Codex on.
    public let sessions: SessionBoard?
    public let limits: LimitsBoard?
    public let spend: SpendBoard?
    public let broker: PermissionBroker?
    /// Codex on.
    public let codex: CodexWatcher?
    /// Claude Code on.
    public let claude: ClaudeSpend?
    /// Weather on.
    public let weather: WeatherClient?
    /// GitHub on.
    public let gitHub: GitHubApi?
    /// Whether a GitHub token is saved (`GitHubSignIn.isSignedIn`).
    public let gitHubSignedIn: (() -> Bool)?
    /// The token-pasting git hosts that are set up (Gitea, Bitbucket, Gitee); each one is used only while its settings
    /// switch is on.
    public let gitHosts: [GitHost]?
    /// Opens an address a git host's page named, in the default browser. Without one, GIT_OPEN is dropped and counted.
    public let urlOpener: UrlOpening?
    /// Whether the agent's hook was heard since it was connected (`UsageStore.lastHeard` at least 0). Nil counts every
    /// agent as heard.
    public let agentHeard: ((String) -> Bool)?
    /// The link-open limit and the per-host refresh times; the app passes the one it holds so a host made again does
    /// not reset them.
    public let throttle: DashboardThrottle

    private let gate = NSLock()
    private var hosts: [WeakHost] = []

    public init(
        sessions: SessionBoard? = nil,
        limits: LimitsBoard? = nil,
        spend: SpendBoard? = nil,
        broker: PermissionBroker? = nil,
        codex: CodexWatcher? = nil,
        claude: ClaudeSpend? = nil,
        weather: WeatherClient? = nil,
        gitHub: GitHubApi? = nil,
        gitHubSignedIn: (() -> Bool)? = nil,
        gitHosts: [GitHost]? = nil,
        urlOpener: UrlOpening? = nil,
        agentHeard: ((String) -> Bool)? = nil,
        throttle: DashboardThrottle = DashboardThrottle()
    ) {
        self.throttle = throttle
        self.sessions = sessions
        self.limits = limits
        self.spend = spend
        self.broker = broker
        self.codex = codex
        self.claude = claude
        self.weather = weather
        self.gitHub = gitHub
        self.gitHubSignedIn = gitHubSignedIn
        self.gitHosts = gitHosts
        self.urlOpener = urlOpener
        self.agentHeard = agentHeard
        sessions?.changed = { [weak self] in self?.each { $0.sessionsChanged() } }
        limits?.changed = { [weak self] in self?.each { $0.limitsChanged() } }
        spend?.changed = { [weak self] in self?.each { $0.spendChanged() } }
        broker?.asked = { [weak self] prompt in self?.each { $0.ask(prompt) } }
        broker?.gone = { [weak self] id in self?.each { $0.gone(id) } }
    }

    func attach(_ host: IntegrationHost) {
        gate.lock()
        defer { gate.unlock() }
        hosts = hosts.filter { $0.host != nil } + [WeakHost(host: host)]
    }

    func detach(_ host: IntegrationHost) {
        gate.lock()
        defer { gate.unlock() }
        hosts = hosts.filter { $0.host != nil && $0.host !== host }
    }

    private func each(_ body: (IntegrationHost) -> Void) {
        gate.lock()
        let live = hosts.compactMap(\.host)
        gate.unlock()
        for host in live {
            body(host)
        }
    }

    private struct WeakHost {
        weak var host: IntegrationHost?
    }
}

/// One phone link's side of the dashboard, made on one `DashboardParts`; `DashboardLink` makes it again whenever a
/// switch moves. Nothing runs until the phone asks: `onWatch` (DASH_WATCH) starts the stats reporter for bits 0 and 1,
/// the weather for bit 2 (asked again every 30 min while it stays set; the client caches), sends limit and spend
/// snapshots for bit 3 and then their changes, and refreshes GitHub for bit 4 (at most once a minute) and sends BLOB
/// GIT after each refresh that changed it, and opens the address a TEXT GIT_OPEN names (`openGit`). Sessions are sent on every change whatever the mask. Approvals go out as
/// BLOB PERM_ASK, their end as TEXT PERM_GONE. Every frame and blob is handed to the session on the main queue, which
/// is the only one a `Session` may be called on. The twin of the laptop core's IntegrationHost.
public final class IntegrationHost {
    public static let widgetsBit = StatsReporter.widgetsBit
    public static let screenBit = StatsReporter.screenBit
    public static let netBit = StatsReporter.netBit
    public static let weatherBit = 4
    public static let aiBit = CodexWatcher.aiBit
    public static let gitHubBit = 16
    public static let weatherEvery = WeatherClient.forecastFor

    private let send: (Frame) -> Void
    private let blobs: BlobSender?
    private let settings: IntegrationSettings
    private let parts: DashboardParts
    private let stats: StatsSource?
    private let net: NetInfoSource?
    private let speed: SpeedTest
    private let clock: () -> Date
    private let onMain: (@escaping () -> Void) -> Void
    private let startTimer: (@escaping () -> Void, TimeInterval) -> Stoppable
    private let reporterTimer: ((@escaping () -> Void) -> Stoppable)?
    private let gate = NSLock()
    private var reporter: StatsReporter?
    private var weatherTimer: Stoppable?
    private var gitHubTimer: Stoppable?
    private var weatherSent: String?
    private var weatherRetrying = false
    // What a phone that has been sent no git page holds is the empty body.
    private var gitHubSent = ""
    // What this phone was last sent for each token host (by id), so an unchanged page is not sent again.
    private var gitSent: [String: String] = [:]
    // Whether the phone was last told GitLab's sign-in expired, so it is told again only when that moves.
    private var gitLabWasExpired = false
    // The key of GitHub's last refresh in the process-wide throttle.
    private static let gitHubKey = "github"
    private let weatherRetryAfter: TimeInterval
    private var watch = 0
    private var disposed = false
    // The last `maxEnded` ask ids that ended, newest last, so a PERM_ASK blob still queued for one is never sent.
    private static let maxEnded = 64
    // A refresh this recent already answers a sign-in, so `signedInChanged` does not ask GitHub twice.
    private static let justRefreshed: TimeInterval = 5
    private var ended: Set<String> = []
    private var endedOrder: [String] = []

    /// - Parameters:
    ///   - send: Writes one frame to this phone; only ever called on `onMain`.
    ///   - blobs: This session's blob sender; only ever called on `onMain`. Nil sends no approval or GitHub blob.
    ///   - stats: The Mac's numbers, when this build has a source.
    ///   - onMain: Runs work on the session's queue, the main one. Tests run it inline.
    ///   - startTimer: Runs the action now and then every interval on another queue until stopped. Tests pass their own,
    ///     which the stats reporter then uses too.
    ///   - net: The Mac's connection facts, when this build has them; with it the phone gets NET_INFO and may run a
    ///     speed test.
    ///   - speedTest: The speed test runner; tests pass one with a fake transport.
    public init(
        send: @escaping (Frame) -> Void,
        blobs: BlobSender?,
        settings: IntegrationSettings,
        parts: DashboardParts,
        stats: StatsSource? = nil,
        clock: @escaping () -> Date = Date.init,
        onMain: @escaping (@escaping () -> Void) -> Void = { DispatchQueue.main.async(execute: $0) },
        startTimer: ((@escaping () -> Void, TimeInterval) -> Stoppable)? = nil,
        weatherRetryAfter: TimeInterval = WeatherClient.retryAfter,
        net: NetInfoSource? = nil,
        speedTest: SpeedTest? = nil
    ) {
        self.weatherRetryAfter = weatherRetryAfter
        self.net = net
        speed = speedTest ?? SpeedTest()
        self.send = send
        self.blobs = blobs
        self.settings = settings
        self.parts = parts
        self.stats = stats
        self.clock = clock
        self.onMain = onMain
        if let startTimer {
            self.startTimer = startTimer
            reporterTimer = { startTimer($0, 1) }
        } else {
            let queue = DispatchQueue(label: "edgepad.dashboard", qos: .utility)
            self.startTimer = { action, every in Stoppable.repeating(every: every, on: queue, action) }
            reporterTimer = nil
        }
        parts.attach(self)
    }

    private func heard(_ agent: AgentToken) -> Bool {
        parts.agentHeard?(agent.rawValue) ?? true
    }

    private static let agentKeys = [
        AgentToken.claude.rawValue, AgentToken.codex.rawValue, AgentToken.gemini.rawValue,
        AgentToken.opencode.rawValue,
    ]

    private var agentsOn: Bool {
        Self.agentKeys.contains(where: settings.active)
    }

    // Whether any token-pasting git host (Gitea, Bitbucket, Gitee) is on and was handed to this host.
    private var tokenHostsOn: Bool {
        parts.gitHosts?.contains { settings.active($0.id) } ?? false
    }

    private var weatherOn: Bool {
        settings.flag(IntegrationSettings.weather) && parts.weather != nil
    }

    private var gitHubOn: Bool {
        settings.active(IntegrationSettings.gitHub) && parts.gitHub != nil
    }

    /// Call once the session has sent FEATURES: sends the current sessions and lets the agent readers see the phone.
    public func start() {
        parts.codex?.evaluate()
        parts.claude?.evaluate()
        if settings.active(IntegrationSettings.claude), let board = parts.sessions, let claude = parts.claude {
            // Off the session thread: the board's own change notice sends the result when it differs.
            DispatchQueue.global(qos: .utility).async { board.seed(claude.listSessions()) }
        }
        if agentsOn && parts.sessions != nil {
            sessionsChanged()
        }
    }

    /// The FEATURES tokens of the switches that are on and usable now.
    public func features() -> [String] {
        var features: [String] = []
        if settings.flag(IntegrationSettings.stats) && stats != nil {
            features.append(Feature.stats.rawValue)
            if net != nil {
                features.append(Feature.netinfo.rawValue)
            }
        }
        if weatherOn && WeatherClient.cityFrom(settings.text(IntegrationSettings.city)) != nil {
            features.append(Feature.weather.rawValue)
        }
        // An agent counts once it actually works with Edgepad (heard from, Codex trusted), not as soon as its switch is on.
        if settings.active(IntegrationSettings.claude) && parts.sessions != nil && heard(AgentToken.claude) {
            features.append(Feature.claude.rawValue)
        }
        if settings.active(IntegrationSettings.codex) && parts.sessions != nil && heard(AgentToken.codex)
            && settings.flag(IntegrationSettings.codexTrusted)
        {
            features.append(Feature.codex.rawValue)
        }
        if settings.active(AgentToken.gemini.rawValue) && parts.sessions != nil && heard(AgentToken.gemini) {
            features.append(Feature.gemini.rawValue)
        }
        if settings.active(AgentToken.opencode.rawValue) && parts.sessions != nil && heard(AgentToken.opencode) {
            features.append(Feature.opencode.rawValue)
        }
        if gitHubOn && parts.gitHubSignedIn?() == true {
            features.append(Feature.github.rawValue)
        }
        // A token host counts once its token is saved and its switch is on.
        for host in parts.gitHosts ?? [] where settings.active(host.id) {
            if (host as? TokenGitHost)?.isSignedIn == true, let provider = DevProviders.find(host.id) {
                features.append(provider.featureToken)
            }
        }
        if settings.flag(IntegrationSettings.developer) {
            features.append(Feature.developer.rawValue)
        }
        return features
    }

    /// The TEXT UNAVAILABLE entries (`feature:reason`) for switches that are on but cannot work here.
    public func unavailable() -> [String] {
        var reasons: [String] = []
        if settings.flag(IntegrationSettings.stats) && stats == nil {
            reasons.append(Feature.stats.rawValue + ":" + UnavailableReason.unsupported.rawValue)
        }
        if settings.active(IntegrationSettings.gitHub) && parts.gitHubSignedIn?() != true {
            reasons.append(Feature.github.rawValue + ":" + UnavailableReason.noPermission.rawValue)
        }
        // A gitlab.com sign-in that GitLab stopped renewing: the phone says so instead of dropping the host.
        if gitLabExpired() {
            reasons.append(Feature.gitlab.rawValue + ":" + UnavailableReason.noPermission.rawValue)
        }
        return reasons
    }

    private func gitLabExpired() -> Bool {
        settings.active("gitlab") && (parts.gitHosts ?? []).contains { ($0 as? GitLabHost)?.signInExpired == true }
    }

    /// The phone's PERM_ANSWER. False (counted as dropped) when no ask waits for it.
    public func answer(_ wire: String) -> Bool {
        parts.broker?.answer(wire) ?? false
    }

    /// The phone's SPEED_TEST: true starts one speed test (ignored while one runs, and when this Mac does not name
    /// `Feature.netinfo`), false cancels the running one. The result is pushed as TEXT SPEED_RESULT. Never blocks the
    /// caller.
    public func onSpeedTest(_ start: Bool) {
        gate.lock()
        let live = !disposed
        gate.unlock()
        guard live else { return }
        if !start {
            speed.cancel()
        } else if net != nil, stats != nil, settings.flag(IntegrationSettings.stats) {
            speed.start { [weak self] result in
                self?.push(.text(kind: TextKind.speedResult.rawValue, value: SpeedWire.encode(result)))
            }
        }
    }

    /// The phone's DASH_WATCH, 0..63 (the Dispatcher drops anything larger).
    public func onWatch(_ mask: Int) {
        gate.lock()
        guard !disposed else {
            gate.unlock()
            return
        }
        let before = watch
        watch = mask
        let aiGained = before & Self.aiBit == 0 && mask & Self.aiBit != 0
        let gitHubGained = before & Self.gitHubBit == 0 && mask & Self.gitHubBit != 0
        let netGained = before & Self.netBit == 0 && mask & Self.netBit != 0
        if mask & (Self.widgetsBit | Self.screenBit | Self.netBit) != 0, reporter == nil, let stats,
            settings.flag(IntegrationSettings.stats)
        {
            reporter = StatsReporter(
                source: stats, send: { [weak self] frame in self?.push(frame) }, startTimer: reporterTimer, net: net)
        }
        let statsReporter = reporter
        if mask & Self.weatherBit == 0 {
            weatherTimer?.stop()
            weatherTimer = nil
            weatherSent = nil
        } else if weatherTimer == nil && weatherOn {
            weatherTimer = startTimer({ [weak self] in self?.refreshWeather() }, Self.weatherEvery)
        }
        if mask & Self.gitHubBit == 0 {
            gitHubTimer?.stop()
            gitHubTimer = nil
            gitSent = [:]
        } else if gitHubTimer == nil && (gitHubOn || tokenHostsOn) {
            gitHubTimer = startTimer({ [weak self] in self?.refreshGit() }, GitHubRefresh.every)
        }
        gate.unlock()

        statsReporter?.setWatch(mask)
        parts.codex?.setWatch(mask)
        parts.claude?.setWatch(mask)
        if aiGained && agentsOn {
            sendLimits()
            sendSpend()
        }
        if gitHubGained && gitHubOn && parts.gitHub?.page != nil {
            sendGitHub(force: true)
        }
        if gitHubGained && tokenHostsOn {
            sendGitHosts()
        }
        if netGained, let last = speed.last {
            push(.text(kind: TextKind.speedResult.rawValue, value: SpeedWire.encode(last)))
        }
    }

    /// The app's GitHub sign-in finished or its token went (call it after FEATURES has been sent again). While the phone
    /// watches bit 4 and GitHub is on: signed in refreshes at once, bypassing the 60 s interval one time (the timer is
    /// started when the host has none), then sends the blob; signed out says UNAVAILABLE no-permission. Does nothing when
    /// the phone is not watching, and never starts a timer then.
    public func signedInChanged() {
        gate.lock()
        guard !disposed, watch & Self.gitHubBit != 0, gitHubOn else {
            gate.unlock()
            return
        }
        if parts.gitHubSignedIn?() != true {
            gate.unlock()
            push(.text(kind: TextKind.unavailable.rawValue, value: unavailable().joined(separator: "/")))
            return
        }
        var refresh = false
        if gitHubTimer == nil {
            // The new timer runs the first refresh now.
            gitHubTimer = startTimer({ [weak self] in self?.refreshGit() }, GitHubRefresh.every)
        } else if parts.throttle.lastRefresh(Self.gitHubKey)
            .map({ clock().timeIntervalSince($0) >= Self.justRefreshed }) ?? true
        {
            parts.throttle.forgetRefresh(Self.gitHubKey)
            refresh = true
        }
        gate.unlock()
        if refresh {
            refreshGitHub()
        }
    }

    /// Stops every timer and lets go of the parts. The caller stops counting this phone first, so a waiting approval
    /// goes back to the agent's own prompt when no phone is left.
    public func dispose() {
        gate.lock()
        guard !disposed else {
            gate.unlock()
            return
        }
        disposed = true
        let statsReporter = reporter
        reporter = nil
        speed.dispose()
        weatherTimer?.stop()
        weatherTimer = nil
        gitHubTimer?.stop()
        gitHubTimer = nil
        gate.unlock()

        statsReporter?.dispose()
        parts.detach(self)
        parts.broker?.phonesChanged()
        parts.codex?.evaluate()
        parts.claude?.evaluate()
    }

    // ---- told by the parts

    func sessionsChanged() {
        guard let sessions = parts.sessions, !isDisposed else { return }
        push(.text(kind: TextKind.sessions.rawValue, value: DashWire.encodeSessions(sessions.snapshot())))
    }

    func limitsChanged() {
        if watching(Self.aiBit) {
            sendLimits()
        }
    }

    func spendChanged() {
        if watching(Self.aiBit) {
            sendSpend()
        }
    }

    // The broker announces an ask outside its lock, so its end can be queued first; the check right before the send
    // keeps a sheet that already ended off the phone. Urgent: a person waits on it.
    func ask(_ prompt: PermPrompt) {
        let label = DashWire.permLabel(prompt.id, complete: prompt.complete)
        let body = Array(prompt.body.utf8)
        let id = prompt.id
        onMain { [weak self] in
            guard let self, !self.isDisposed, !self.hasEnded(id) else { return }
            let over: () -> Bool = { [weak self] in self?.hasEnded(id) ?? true }
            self.blobs?.send(.permAsk, label: label, data: body, abandoned: over, urgent: true)
        }
    }

    func gone(_ id: String) {
        gate.lock()
        if !ended.contains(id) {
            ended.insert(id)
            endedOrder.append(id)
            if endedOrder.count > Self.maxEnded {
                ended.remove(endedOrder.removeFirst())
            }
        }
        gate.unlock()
        push(.text(kind: TextKind.permGone.rawValue, value: id))
    }

    private func hasEnded(_ id: String) -> Bool {
        gate.lock()
        defer { gate.unlock() }
        return ended.contains(id)
    }

    // ---- sending

    private var isDisposed: Bool {
        gate.lock()
        defer { gate.unlock() }
        return disposed
    }

    private func watching(_ bit: Int) -> Bool {
        gate.lock()
        defer { gate.unlock() }
        return !disposed && watch & bit != 0
    }

    private func sendLimits() {
        if let limits = parts.limits {
            push(.text(kind: TextKind.limits.rawValue, value: DashWire.encodeLimits(limits.snapshot())))
        }
    }

    private func sendSpend() {
        if let spend = parts.spend {
            push(.text(kind: TextKind.spend.rawValue, value: DashWire.encodeSpend(spend.snapshot())))
        }
    }

    private func refreshWeather(isRetry: Bool = false) {
        guard let weather = parts.weather, watching(Self.weatherBit) else { return }
        let city = settings.text(IntegrationSettings.city)
        Task { [weak self] in
            let info = await weather.get(city)
            self?.weatherArrived(DashWire.encodeWeather(info))
            if info == nil && !isRetry {
                self?.retryWeatherOnce()
            }
        }
    }

    // A failed fetch is asked again once after the client's own back-off, not left for the 30 min timer.
    private func retryWeatherOnce() {
        gate.lock()
        guard !disposed, !weatherRetrying else {
            gate.unlock()
            return
        }
        weatherRetrying = true
        gate.unlock()
        let delay = weatherRetryAfter
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(max(0, delay) * 1_000_000_000))
            guard let self else { return }
            self.gate.withLock { self.weatherRetrying = false }
            self.refreshWeather(isRetry: true)
        }
    }

    private func weatherArrived(_ text: String) {
        gate.lock()
        guard !disposed, watch & Self.weatherBit != 0, text != weatherSent else {
            gate.unlock()
            return
        }
        weatherSent = text
        gate.unlock()
        push(.text(kind: TextKind.weather.rawValue, value: text))
    }

    // One tick of the git timer: GitHub, then every token host that is on.
    private func refreshGit() {
        refreshGitHub()
        refreshGitHosts()
    }

    private func refreshGitHosts() {
        let hosts = (parts.gitHosts ?? []).filter { settings.active($0.id) }
        guard !hosts.isEmpty else { return }
        Task { [weak self] in
            for host in hosts {
                _ = await host.refresh()
            }
            guard let self else { return }
            self.tellGitLabExpiry()
            guard self.watching(Self.gitHubBit) else { return }
            self.sendGitHosts()
        }
    }

    // GitLab's renewal was refused (or a sign-in since fixed it): tell the phone now, as GitHub's revoked token is.
    private func tellGitLabExpiry() {
        let expired = gitLabExpired()
        gate.lock()
        let moved = !disposed && expired != gitLabWasExpired
        gitLabWasExpired = expired
        gate.unlock()
        if moved {
            push(.text(kind: TextKind.unavailable.rawValue, value: unavailable().joined(separator: "/")))
        }
    }

    // Each token host that is on gets its page sent when it differs from what this phone holds; a host that went off or
    // signed out sends nothing.
    private func sendGitHosts() {
        for host in (parts.gitHosts ?? []) where settings.active(host.id) {
            let text = host.page?.body ?? ""
            gate.lock()
            let skip = disposed || text == (gitSent[host.id] ?? "")
            if !skip {
                gitSent[host.id] = text
            }
            gate.unlock()
            if skip { continue }
            let body = Array(text.utf8)
            onMain { [weak self] in
                guard let self, !self.isDisposed else { return }
                self.blobs?.send(.git, label: "git", data: body)
            }
        }
    }

    private func refreshGitHub() {
        guard let gitHub = parts.gitHub else { return }
        gate.lock()
        let now = clock()
        guard
            GitHubRefresh.shouldRun(
                last: parts.throttle.lastRefresh(Self.gitHubKey), now: now,
                watchBit4: !disposed && watch & Self.gitHubBit != 0)
        else {
            gate.unlock()
            return
        }
        parts.throttle.refreshed(Self.gitHubKey, at: now)
        gate.unlock()
        Task { [weak self] in
            let result = await gitHub.refresh()
            guard let self, self.watching(Self.gitHubBit) else { return }
            if result.result == .noPermission {
                // The token was refused and deleted: tell the phone now, not at the next connect.
                self.push(
                    .text(
                        kind: TextKind.unavailable.rawValue,
                        value: Feature.github.rawValue + ":" + UnavailableReason.noPermission.rawValue))
                return
            }
            self.sendGitHub()
        }
    }

    // What this host last sent is remembered, so a second phone still gets data the first already has, and an
    // unchanged list is not sent twice. `force` resends when the phone opens the screen again.
    private func sendGitHub(force: Bool = false) {
        guard let gitHub = parts.gitHub else { return }
        let text = gitHub.page?.body ?? ""
        gate.lock()
        let same = gitHubSent == text
        gitHubSent = text
        gate.unlock()
        if same && !force { return }
        let body = Array(text.utf8)
        onMain { [weak self] in
            guard let self, !self.isDisposed else { return }
            self.blobs?.send(.git, label: "git", data: body)
        }
    }

    /// The phone's TEXT GIT_OPEN (`host/listRevision/index`). Opens the address this laptop fetched for that revision
    /// and index in the default browser, and says whether it was accepted; anything else is refused and the caller
    /// drops and counts it (see `GitOpenGate`).
    public func openGit(_ wire: String) -> Bool {
        guard !isDisposed, let opener = parts.urlOpener, let url = GitOpenGate.resolve(wire, hosts: git),
            openAllowed()
        else {
            return false
        }
        DispatchQueue.global(qos: .utility).async { _ = opener.open(url) }
        return true
    }

    // At most `DashboardThrottle.maxOpens` opens per window for the whole process, however often a phone makes its link
    // again.
    private func openAllowed() -> Bool {
        parts.throttle.allowOpen(clock())
    }

    // A token host counts only while its own switch (the settings key named by its id) is on.
    private func git(_ id: String) -> GitHost? {
        if let gitHub = parts.gitHub, gitHubOn, gitHub.id == id {
            return gitHub
        }
        guard settings.active(id) else { return nil }
        return parts.gitHosts?.first(where: { $0.id == id })
    }

    // Only queued here; the dead-link check and the write happen on the session's queue.
    private func push(_ frame: Frame) {
        onMain { [weak self] in
            guard let self, !self.isDisposed else { return }
            self.send(frame)
        }
    }
}

/// One phone session's side of the dashboard, from the moment the session goes live to its end: the phone is counted
/// for as long as it lives, and its `IntegrationHost` is made again on the newest parts whenever a switch moves
/// (`rebuild`). Main queue only.
public final class DashboardLink {
    private let phones: LinkedPhones
    private let makeHost: () -> IntegrationHost
    private let unsupported: () -> [String]
    private var host: IntegrationHost
    private var watch = 0
    private var disposed = false

    /// - Parameters:
    ///   - makeHost: A host on the app's current parts, writing to this session.
    ///   - unsupported: TEXT UNAVAILABLE entries the app adds (an agent on whose hook socket could not open).
    public init(
        phones: LinkedPhones, makeHost: @escaping () -> IntegrationHost,
        unsupported: @escaping () -> [String] = { [] }
    ) {
        self.phones = phones
        self.makeHost = makeHost
        self.unsupported = unsupported
        phones.add()
        host = makeHost()
    }

    public func features() -> [String] {
        host.features()
    }

    public func unavailable() -> [String] {
        host.unavailable() + unsupported()
    }

    /// Once FEATURES has gone out.
    public func start() {
        host.start()
    }

    /// The phone's DASH_WATCH.
    public func onWatch(_ mask: Int) {
        watch = mask
        host.onWatch(mask)
    }

    /// The phone's SPEED_TEST.
    public func onSpeedTest(_ start: Bool) {
        host.onSpeedTest(start)
    }

    /// The phone's PERM_ANSWER.
    public func answer(_ wire: String) -> Bool {
        host.answer(wire)
    }

    /// The phone's GIT_OPEN.
    public func openGit(_ wire: String) -> Bool {
        host.openGit(wire)
    }

    /// A switch moved: the host is made again on the newest parts and given the phone's watch.
    public func rebuild() {
        guard !disposed else { return }
        let old = host
        host = makeHost()
        old.dispose()
        host.start()
        host.onWatch(watch)
        // A GitHub sign-in or sign-out reaches a phone on its GitHub page now; any other switch finds a refresh just made.
        host.signedInChanged()
    }

    /// The session ended. The phone stops being counted first, so the host's dispose sees it gone. Runs once, here or
    /// when the link is let go of without it, so a lost link never leaves a phone counted.
    public func dispose() {
        guard !disposed else { return }
        disposed = true
        phones.remove()
        host.dispose()
    }

    deinit {
        dispose()
    }
}
