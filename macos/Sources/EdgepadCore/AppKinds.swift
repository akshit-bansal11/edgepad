import Foundation

/// Which app in front is which kind of app, for `TextKind.foregroundKind`. The twin of the shared core's `AppKinds`,
/// with only this Mac's rows: a bundle id as `WorkspaceForeground` reports it, matched ignoring case. The same rows as
/// `protocol/app-kinds.txt`, which a test compares. Only a kind token ever leaves this Mac, never a title.
public enum AppKinds {
    static let table: [(kind: AppKindToken, token: String)] = [
        (.media, "com.spotify.client"),
        (.media, "com.apple.Music"),
        (.media, "com.apple.iTunes"),
        (.media, "org.videolan.vlc"),
        (.timeline, "org.blenderfoundation.blender"),
        (.timeline, "com.blackmagic-design.DaVinciResolve"),
        (.timeline, "org.audacityteam.audacity"),
        (.timeline, "com.apple.FinalCut"),
        (.timeline, "com.apple.logic10"),
        (.timeline, "com.ableton.live"),
        (.browser, "com.google.Chrome"),
        (.browser, "com.microsoft.edgemac"),
        (.browser, "org.mozilla.firefox"),
        (.browser, "com.brave.Browser"),
        (.browser, "com.operasoftware.Opera"),
        (.browser, "com.vivaldi.Vivaldi"),
        (.browser, "com.apple.Safari"),
        (.browser, "company.thebrowser.Browser"),
    ]

    /// The table's kind for a foreground token, or nil when the app is not in it.
    static func of(token: String) -> AppKindToken? {
        table.first { $0.token.caseInsensitiveCompare(token) == .orderedSame }?.kind
    }

    /// The order of the check: the table says media or timeline and wins; else a playing session of that app is media;
    /// else a browser is a browser; else "" for none. macOS follows only Music and Spotify, so a browser in front is
    /// never media here.
    static func resolve(tableKind: AppKindToken?, playing: Bool) -> String {
        if tableKind == .media || tableKind == .timeline { return tableKind?.rawValue ?? "" }
        if playing { return AppKindToken.media.rawValue }
        return tableKind?.rawValue ?? ""
    }
}

/// Works out the kind of the app in front from two inputs, the foreground token and the app whose session is playing,
/// and hands `send` the new kind only when it differs from the last one sent. Nothing is sent before `start()` or after
/// `stop()`; the initial "" is never sent. No timers.
final class ForegroundKind {
    private let samePlayer: (String, String) -> Bool
    private let send: (String) -> Void
    private var running = false
    private var token = ""
    private var source: String?
    private var sent = ""

    /// `samePlayer(token, source)` says whether the playing session belongs to the app in front.
    init(samePlayer: @escaping (String, String) -> Bool, send: @escaping (String) -> Void) {
        self.samePlayer = samePlayer
        self.send = send
    }

    /// Forgets the foreground app and what was sent, then listens. The playing source is kept: it is not the watch's.
    func start() {
        token = ""
        sent = ""
        running = true
        update()
    }

    func stop() {
        running = false
    }

    func setForeground(_ token: String) {
        self.token = token
        update()
    }

    /// What the OS names the playing session's app, nil when nothing plays.
    func setPlaying(_ source: String?) {
        self.source = source
        update()
    }

    private func update() {
        guard running else { return }
        let playing = source.map { samePlayer(token, $0) } ?? false
        let kind = AppKinds.resolve(tableKind: AppKinds.of(token: token), playing: playing)
        guard kind != sent else { return }
        sent = kind
        send(kind)
    }
}
