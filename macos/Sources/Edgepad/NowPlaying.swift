import AppKit
import EdgepadCore
import Foundation

/// What Music and Spotify are playing, learned from the distributed notifications each posts on every change: no
/// polling. One for the whole app and always listening, so a phone that connects mid-song is told at once; each
/// connection holds a `MacMediaSource` that listens to it. AppleScript is asked only for the position of a player that
/// does not put it in its notification (Music) and to seek, each on demand and never on the main thread. A browser
/// in front is asked for its front tab every few seconds, only while it is in front and only when Music and Spotify
/// have nothing, to name a streaming service (never a social site, and never the tab's title or address). Everything here runs on the main thread, as `Session` requires, except the scripts,
/// whose answers are handed back to it. HW-unverified: the notifications' keys are read from memory of their userInfo.
final class NowPlayingHub {
    private struct Entry {
        var report: PlayerReport
        /// The position in seconds at `at`; moved on by the clock while the report says it is playing.
        var base: Int
        var at: TimeInterval
    }

    private static let scriptTimeout: TimeInterval = 5
    private static let tabEvery: TimeInterval = 3
    private static let tabBackOff: TimeInterval = 30

    private var entries: [PlayerApp: Entry] = [:]
    /// Most recently heard first.
    private var recent: [PlayerApp] = []
    private var listeners: [UUID: () -> Void] = [:]
    private var observers: [NSObjectProtocol] = []
    /// The service in the front browser's tab, with the browser it came from; nil when none is known.
    private var tab: (bundleId: String, service: String)?
    private var tabNextAsk: TimeInterval = 0
    private var tabAsking = false
    private let scripts = DispatchQueue(label: "me.akshitbansal.edgepad.nowplaying", qos: .utility)

    init() {
        for app in PlayerApp.allCases {
            let observer = DistributedNotificationCenter.default().addObserver(
                forName: Notification.Name(app.notification), object: nil, queue: .main
            ) { [weak self] note in
                self?.receive(app, note.userInfo ?? [:])
            }
            observers.append(observer)
        }
    }

    deinit {
        for observer in observers {
            DistributedNotificationCenter.default().removeObserver(observer)
        }
    }

    /// `handler` is told after every change; nil lets go of `id`.
    func listen(_ id: UUID, _ handler: (() -> Void)?) {
        listeners[id] = handler
    }

    /// The player that is playing, else the one most recently paused, else nothing. Free of I/O but for one
    /// is-it-still-running check, so a player that quit without saying so is not extrapolated for ever.
    func read() -> MediaInfo {
        let now = ProcessInfo.processInfo.systemUptime
        let pick = recent.compactMap { entries[$0] }.filter {
            $0.report.active && NowPlayingHub.isRunning($0.report.app)
        }
        let playing = pick.first { $0.report.playing }
        // Music or Spotify, playing or paused, always wins: a browser tab is asked about only with neither.
        if pick.isEmpty, let service = browserService(at: now) { return service }
        guard let entry = playing ?? pick.first else { return .none }
        let report = entry.report
        return MediaInfo(
            title: report.title,
            artist: report.artist,
            app: MediaServices.name(bundleId: report.app.bundleId) ?? report.app.rawValue,
            identity: NowPlayingHub.identity(report),
            playing: report.playing,
            positionSeconds: position(of: entry, at: now),
            durationSeconds: report.durationSeconds)
    }

    /// A service card for the browser in front, from the last answer; asks for a fresh one when it is due. Playing is
    /// not known to a browser's scripting, so a recognised service reads as playing. Never the tab's title or address:
    /// the service's name is the card's title too, so the phone shows no blank line.
    private func browserService(at now: TimeInterval) -> MediaInfo? {
        guard let bundleId = NSWorkspace.shared.frontmostApplication?.bundleIdentifier,
            BrowserTab.isScriptable(bundleId)
        else {
            tab = nil
            return nil
        }
        if now >= tabNextAsk, !tabAsking { askTab(bundleId, at: now) }
        guard let tab, tab.bundleId == bundleId else { return nil }
        return MediaInfo(
            title: tab.service, artist: "", app: tab.service, identity: "\(bundleId)|\(tab.service)",
            playing: true, positionSeconds: 0, durationSeconds: 0)
    }

    private func askTab(_ bundleId: String, at now: TimeInterval) {
        tabAsking = true
        tabNextAsk = now + NowPlayingHub.tabEvery
        scripts.async { [weak self] in
            let read = BrowserTab.read(bundleId)
            DispatchQueue.main.async {
                guard let self else { return }
                self.tabAsking = false
                if read == nil { self.tabNextAsk = ProcessInfo.processInfo.systemUptime + NowPlayingHub.tabBackOff }
                let service = read.flatMap { MediaServices.streamingName(host: $0.host, title: $0.title) }
                let next = service.map { (bundleId: bundleId, service: $0) }
                let changed = next?.service != self.tab?.service
                self.tab = next
                if changed { self.notify() }
            }
        }
    }

    /// Seeks the player `read()` names to `percent` of its length. False with no timeline or nothing playing.
    func seek(percent: Int) -> Bool {
        let info = read()
        guard info.durationSeconds > 0, let app = PlayerApp(sent: info.app), var entry = entries[app] else {
            return false
        }
        let target = info.durationSeconds * Percent.clamp(percent) / 100
        entry.base = target
        entry.at = ProcessInfo.processInfo.systemUptime
        entries[app] = entry
        notify()
        scripts.async {
            _ = NowPlayingHub.osascript(
                [
                    "on run argv", "set p to (item 1 of argv) as integer",
                    "tell application \"\(app.rawValue)\" to set player position to p", "end run",
                ], arguments: [String(target)])
        }
        return true
    }

    private func receive(_ app: PlayerApp, _ userInfo: [AnyHashable: Any]) {
        guard let report = NowPlayingParse.report(app: app, userInfo: userInfo) else { return }
        let now = ProcessInfo.processInfo.systemUptime
        var base = report.positionSeconds ?? 0
        if report.positionSeconds == nil, let before = entries[app],
            NowPlayingHub.identity(before.report) == NowPlayingHub.identity(report)
        {
            // The same track, so it carries on from where it was until the script says where that is.
            base = position(of: before, at: now)
        }
        entries[app] = Entry(report: report, base: base, at: now)
        recent.removeAll { $0 == app }
        recent.insert(app, at: 0)
        notify()
        if report.positionSeconds == nil && report.active {
            askPosition(app, for: NowPlayingHub.identity(report))
        }
    }

    /// One `player position` call off the main thread. Skipped when the player is not running, because `tell` would
    /// launch it.
    private func askPosition(_ app: PlayerApp, for identity: String) {
        guard NowPlayingHub.isRunning(app) else { return }
        scripts.async { [weak self] in
            let output = NowPlayingHub.osascript(["tell application \"\(app.rawValue)\" to return player position"])
            guard let seconds = output.flatMap(NowPlayingParse.position(fromScript:)) else { return }
            DispatchQueue.main.async {
                guard let self, var entry = self.entries[app], NowPlayingHub.identity(entry.report) == identity else {
                    return
                }
                entry.base = seconds
                entry.at = ProcessInfo.processInfo.systemUptime
                self.entries[app] = entry
                self.notify()
            }
        }
    }

    private func position(of entry: Entry, at now: TimeInterval) -> Int {
        let moved = entry.report.playing ? Int(max(0, now - entry.at)) : 0
        let seconds = entry.base + moved
        return entry.report.durationSeconds > 0 ? min(seconds, entry.report.durationSeconds) : seconds
    }

    private func notify() {
        for handler in listeners.values {
            handler()
        }
    }

    private static func identity(_ report: PlayerReport) -> String {
        "\(report.app.rawValue)|\(report.title) - \(report.artist)"
    }

    private static func isRunning(_ app: PlayerApp) -> Bool {
        !NSRunningApplication.runningApplications(withBundleIdentifier: app.bundleId).isEmpty
    }

    /// Runs a fixed script (one `-e` per line) with `arguments` as its `argv`: the phone's number is the only thing
    /// that reaches it, as an integer, and never as script text. The output on success, nil on failure or timeout.
    static func osascript(_ lines: [String], arguments: [String] = []) -> String? {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        process.arguments = lines.flatMap { ["-e", $0] } + arguments
        let output = Pipe()
        process.standardOutput = output
        process.standardError = FileHandle.nullDevice
        do {
            try process.run()
        } catch {
            Log.write("osascript did not start: \(error)")
            return nil
        }
        DispatchQueue.global().asyncAfter(deadline: .now() + scriptTimeout) {
            if process.isRunning { process.terminate() }
        }
        let data = output.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        return process.terminationStatus == 0 ? String(decoding: data, as: UTF8.self) : nil
    }
}

/// One connection's view of the hub: it listens while the session wants to hear and lets go when `Session.end()`
/// clears `changed`, so a candidate that ends does not silence the phone in use.
final class MacMediaSource: MediaSource {
    private let hub: NowPlayingHub
    private let id = UUID()

    init(hub: NowPlayingHub) {
        self.hub = hub
    }

    deinit {
        hub.listen(id, nil)
    }

    var changed: (() -> Void)? {
        didSet { hub.listen(id, changed) }
    }

    func read() -> MediaInfo {
        hub.read()
    }

    func seek(percent: Int) -> Bool {
        hub.seek(percent: percent)
    }

    func icon(forApp app: String) -> [UInt8]? {
        MediaIcon.png(forApp: app)
    }
}
