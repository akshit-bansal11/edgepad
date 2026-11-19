/// What a RUN_ACTION frame may ask for. Mirrors `protocol/actions.txt`; a test holds them equal.
public enum ActionId: UInt8, CaseIterable {
    case muteToggle = 1
    case playPause = 2
    case nextTrack = 3
    case previousTrack = 4
    case micMuteToggle = 5
    case lock = 6
    case taskView = 20
    case showDesktop = 21
    case search = 22
    case notifications = 23
    case desktopLeft = 24
    case desktopRight = 25
    case appSwitchBegin = 26
    case appSwitchNext = 27
    case appSwitchPrevious = 28
    case appSwitchEnd = 29
    case zoomReset = 30
    case volumeUp = 31
    case volumeDown = 32
    case brightnessUp = 33
    case brightnessDown = 34
    /// Plug in the virtual controller and unplug it again. Actions rather than frame types, because an
    /// action costs no protocol version: a build too old to know them drops them and counts them.
    case padAttach = 35
    case padDetach = 36
    /// The laser tool: the next `pen` stroke is a dot that follows the finger and leaves nothing behind.
    case toolLaser = 37
    /// The trailing laser: `toolLaser` plus a short red trail behind the dot. Both lasers take their size from the pen width.
    case toolLaserTrail = 38
    /// Annotating the screen and signing, which the laptop draws and the phone only steers: begin and end
    /// show and remove the overlay, undo and clear edit the strokes, and the tools pick what the next `pen`
    /// stroke draws. `signBegin` opens a surface that shows nothing and takes only the pen tool, and `signSave`
    /// asks for the export. A build with no overlay drops and counts all of them.
    case annotateBegin = 40
    case annotateEnd = 41
    case annotateUndo = 42
    case annotateClear = 43
    case toolPen = 44
    case toolHighlighter = 45
    case toolLine = 46
    case toolArrow = 47
    case toolRectangle = 48
    case toolEllipse = 49
    case signBegin = 50
    case signSave = 51
    /// Screen capture through the system's own tool: a region, the whole screen, and the toolbar that records.
    /// Actions because the phone only asks; what each one presses is decided here.
    case captureRegion = 52
    case captureScreen = 53
    case captureRecord = 54
    /// The phone left its capture screen: dismiss a capture overlay this computer opened, only if one may still be open.
    case captureCancel = 55
    /// Adding a macro from the phone: `appsList` asks this computer to send the apps it could add, as a `BlobKind.appList`
    /// blob and then a `BlobKind.appIcon` blob each, and `appsCancel` stops that list and its icons.
    case appsList = 56
    case appsCancel = 57
    /// The macro grid is open and wants to know which macro apps are running: `macrosWatch` starts this computer's survey,
    /// which reports `TextKind.macroOpen` on change only, and `macrosUnwatch` stops it. Forgotten when the session ends.
    case macrosWatch = 58
    case macrosUnwatch = 59
    /// Arm this computer to sleep or shut down when the current media ends (`endSleep`, `endShutdown`), or disarm it
    /// (`endCancel`). It reports the armed state as `TextKind.endAction`. Offered only for a computer that sent `Feature.endAction`.
    case endSleep = 60
    case endShutdown = 61
    case endCancel = 62
    /// The first of 32 macro slots, 64 to 95; slot n is `macroBase + n`. The phone sends the index and never
    /// what it launches: this computer's own list decides that, which is the whole security model.
    case macroBase = 64
    /// A reserved block of 32, 96 to 127: close the app macro slot n opens, as its own close button would, never a kill.
    case macroCloseBase = 96

    public static let macroSlots = 32
}

/// What a SET_VALUE frame may set and a STATE frame reports. Mirrors `protocol/actions.txt`.
public enum ControlId: UInt8, CaseIterable {
    case volume = 0
    case brightness = 1
    case micLevel = 2
    case mediaPosition = 3
    /// The value is an index into the `TextKind.refreshRates` list, never a rate in hertz: SET carries a u8
    /// and anything above 100 is dropped, so 120 or 144 could not cross the wire at all.
    case refreshRate = 4
    /// The value is an index into the eight-colour palette in `protocol/actions.txt`. An index of 8 or more is
    /// dropped and counted. A colour outside the palette is `TextKind.penRgb`.
    case penColor = 5
    /// Where a capture ends up, as a `CaptureTarget` value: this Mac keeps it, or sends it to the phone and deletes its
    /// copy once the phone says it saved it.
    case captureTarget = 6
    /// Whether this link shares the clipboard, 1 or 0, phone to computer. Forgotten when the session ends.
    case clipboard = 7
    /// The width of the next pen-tool strokes, 1...100, phone to computer: 0.02% of the screen's height per step, so
    /// 5 is 0.1%, 20 is the default 0.4% and 100 is 2.0%. 0 or more than 100 is dropped and counted.
    case penWidth = 8
    /// The opacity of the next strokes in percent, 10...100, phone to computer. Anything else is dropped and counted.
    case penOpacity = 9
    /// Whether this link forwards the computer's notifications, 1 or 0, phone to computer. Forgotten when the session ends.
    case notify = 10
    /// Whether this link hears the app in front (`TextKind.foregroundApp`), 1 or 0, phone to computer. Anything above 1 is
    /// dropped and counted. Forgotten when the session ends.
    case foregroundWatch = 11
    /// Phone to computer: a bitmask of what the phone draws right now: bit 0 (1) system-number widgets, bit 1 (2) the System
    /// screen, bit 2 (4) the weather widget, bit 3 (8) an AI widget or screen, bit 4 (16) the GitHub screen. 0 to 31; more is
    /// dropped and counted. Forgotten when the session ends.
    case dashWatch = 12
}

/// What a TEXT frame carries. Only `type` goes phone to computer; the rest go the other way.
/// Mirrors `protocol/actions.txt`.
public enum TextKind: UInt8, CaseIterable {
    case nowPlaying = 0
    case app = 1
    case timeline = 2
    case type = 3
    /// "60/120/144": the display's available rates, in the order `ControlId.refreshRate` indexes them.
    case refreshRates = 4
    /// "Chrome/Spotify/Notes": this computer's macro names, in the order `ActionId.macroBase` indexes them.
    case macros = 5
    /// One piece of one macro's icon, "slot/chunk/chunks/base64". A kind rather than a frame type, because an
    /// unknown kind is dropped while an unknown type closes the connection.
    case macroIcon = 6
    /// Phone to computer: send the macro icons. Empty payload.
    case wantIcons = 7
    /// Computer to phone: whether a virtual controller can be offered, as one `PadStatus` token.
    case padStatus = 8
    /// Computer to phone: the optional features this Mac has, as `Feature` tokens joined by "/".
    case features = 9
    /// Computer to phone: the primary display in pixels, "1920/1080".
    case screenSize = 10
    /// Computer to phone: how a signature save ended, as one token.
    case signStatus = 11
    /// Computer to phone: why a capture the phone asked for did not reach it, as one `CaptureStatus` token.
    case captureStatus = 12
    /// Computer to phone, once after the handshake: "id/os/name", the id 32 hex digits, the os a `DeviceOs` token.
    case device = 13
    /// Phone to computer: "epoch.index", an index into the app list this computer sent for that epoch.
    case appAdd = 14
    /// Computer to phone: how an `appAdd` ended, as one `AppAddStatus` token.
    case appAddStatus = 15
    /// Computer to phone: one character per macro slot, in `macros` order. "-" not trackable, "0" not running, "1" running,
    /// "2" running and in front. Sent when it changes while watched; an unknown character reads as untracked.
    case macroOpen = 16
    /// Phone to computer: a stroke colour as exactly six hex digits, "RRGGBB". Safe to take from the phone, unlike a
    /// path or a command, because it is only a colour: `PenStyle.parseRgb` accepts those six bytes and nothing else,
    /// and what it returns is a number that can only be drawn with.
    case penRgb = 17
    /// Computer to phone: `blocked` or `ok`. `blocked` means the OS discards this Mac's synthetic input (no
    /// Accessibility trust). Sent after the device text and again on every change; never received reads as `ok`.
    case inputState = 18
    /// Computer to phone: a snapshot of the whole battery list, "kind:level:charging:name" entries joined by "|", the
    /// computer first. Empty means no devices. Never a delta; see docs/PROTOCOL.md for the size rule and cadence.
    case batteries = 19
    /// Computer to phone: the audio output, "kind/name" split on the first "/", kind an `OutputKind` token. Empty means none known.
    case output = 20
    /// Computer to phone: the app in front, "token/name" split on the first "/". Sent only after `ControlId.foregroundWatch` is 1,
    /// on change, and never logged. Empty means nothing known.
    case foregroundApp = 21
    /// Computer to phone: how many macro slots it keeps, in decimal such as "15".
    case macroCap = 22
    /// Computer to phone: a `CaptureStateToken`, `idle` or `recording`. Best effort.
    case captureState = 23
    /// Computer to phone: "feature:reason" entries joined by "/", reason an `UnavailableReason` token. Empty means none.
    case unavailable = 24
    /// Computer to phone: what `endSleep` armed, as one `EndActionToken`. Sent on change.
    case endAction = 25
    /// Computer to phone: the title line before it is cleaned for `nowPlaying`, sent only when it differs from that text.
    case nowPlayingFull = 26
    /// Computer to phone: "cpu/ram/gpu/vram/down/up/ping/loss/ctemp/gtemp/gpower", -1 unknown. Once a second while `ControlId.dashWatch` has bit 0 or 1.
    case stats = 27
    /// Computer to phone: "cpuName|ramMiB|gpuName|vramMiB|netName". Sent when a watch starts and when it changes.
    case statsInfo = 28
    /// Computer to phone: "label:usedPct:totalGiB:freeGiB" entries joined by "|", at most 6.
    case disks = 29
    /// Computer to phone: "agent:state:title" entries joined by "|", newest first, at most 8. Empty means none.
    case sessions = 30
    /// Phone to computer: exactly "id/a" or "id/d", the id 12 lowercase hex digits.
    case permAnswer = 31
    /// Computer to phone: "id" of an approval answered elsewhere, timed out or dead.
    case permGone = 32
    /// Computer to phone: "agent:window:pct:resetAtMin:measuredAtMin" entries joined by "|"; the window is minutes, `LimitWindow.context` or "~name".
    case limits = 33
    /// Computer to phone: "agent/measuredAtMin/tokToday/centsToday/tokWeek/centsWeek/tokMonth/centsMonth" entries joined by "|".
    case spend = 34
    /// Computer to phone: "tempTenthsC/wmoCode/city". Empty means none.
    case weather = 35
}

/// The whole vocabulary of `TextKind.features`: tokens the phone matches on, never prose. The raw value is
/// literally the bytes that cross the wire.
public enum Feature: String, CaseIterable {
    case annotate = "annotate"
    case capture = "capture"
    case clipboard = "clipboard"
    case apps = "apps"
    case running = "running"
    case endAction = "endaction"
    case notify = "notify"
    case foreground = "foreground"
    case stats = "stats"
    case weather = "weather"
    case claude = "claude"
    case codex = "codex"
    case github = "github"
}

/// The whole vocabulary of `TextKind.padStatus`: a token the phone matches on, never prose, so the wording
/// of the app stays changeable. The raw value is literally the bytes that cross the wire.
public enum PadStatus: String, CaseIterable {
    case ready = "ready"
    case noDriver = "no-driver"
    case attachFailed = "attach-failed"
}

/// The whole vocabulary of `TextKind.signStatus`: a token the phone matches on, never prose. The raw value is
/// literally the bytes that cross the wire.
public enum SignStatus: String, CaseIterable {
    /// The signature is on the pasteboard and in the Pictures folder.
    case saved = "saved"
    /// Nothing was drawn, so there was nothing to export.
    case empty = "empty"
    /// The export or the save failed.
    case failed = "failed"
}

/// The values of `ControlId.captureTarget`.
public enum CaptureTarget: UInt8 {
    /// The system's tool saves where it saves, and nothing crosses the link.
    case laptop = 0
    /// Each capture goes to the phone, and the copy here is deleted only once the phone says it saved it.
    case phone = 1
}

/// The whole vocabulary of `TextKind.captureStatus`: why a capture stayed on this Mac. A token the phone matches on,
/// never prose. The raw value is literally the bytes that cross the wire.
public enum CaptureStatus: String, CaseIterable {
    /// The file is over its kind's limit, so it stays here.
    case tooLarge = "too-large"
    /// No new file turned up where the system's tool saves, so nothing was sent and nothing deleted.
    case notFound = "not-found"
    /// The file was found and could not be read or sent.
    case failed = "failed"
}

/// What a FILE_BEGIN may carry. An id, never a MIME string, so the phone writes only these types; this Mac sends only
/// a file whose extension is listed. Mirrors the FILE_TYPE and FILE_LIMIT rows of `protocol/actions.txt`.
public enum FileType: UInt8, CaseIterable {
    case png = 0
    case jpeg = 1
    case mp4 = 2
    case mov = 3
    case webm = 4

    public static let imageLimit = 50 * 1024 * 1024
    public static let videoLimit = 200 * 1024 * 1024

    public var mime: String {
        switch self {
        case .png:
            return "image/png"
        case .jpeg:
            return "image/jpeg"
        case .mp4:
            return "video/mp4"
        case .mov:
            return "video/quicktime"
        case .webm:
            return "video/webm"
        }
    }

    public var fileExtension: String {
        switch self {
        case .png:
            return "png"
        case .jpeg:
            return "jpg"
        case .mp4:
            return "mp4"
        case .mov:
            return "mov"
        case .webm:
            return "webm"
        }
    }

    public var isVideo: Bool { mime.hasPrefix("video/") }

    /// The largest file of this kind that may cross the link, in bytes.
    public var limit: Int { isVideo ? FileType.videoLimit : FileType.imageLimit }

    /// The type a path's extension names, or nil for one that is not in the table.
    public init?(path: String) {
        guard let dot = path.lastIndex(of: "."), !path[dot...].contains("/") else { return nil }
        let ext = path[path.index(after: dot)...].lowercased()
        guard let type = FileType.allCases.first(where: { $0.fileExtension == ext }) else { return nil }
        self = type
    }
}

/// What a FILE_RESULT says. Only `saved` lets this Mac delete its copy. Mirrors `protocol/actions.txt`.
public enum FileStatus: UInt8, CaseIterable {
    /// The file is in the phone's gallery and its SHA-256 matched.
    case saved = 0
    /// Every byte arrived and the SHA-256 did not match.
    case badHash = 1
    /// The phone refused the file: a type, size, name or chunk it will not take.
    case rejected = 2
    /// The phone could not write the file.
    case writeFailed = 3
}

/// The OS token in `TextKind.device`. The raw value is literally the bytes that cross the wire.
public enum DeviceOs: String, CaseIterable {
    case windows = "windows"
    case linux = "linux"
    case macos = "macos"
}

/// The kind token of a `TextKind.batteries` entry. The raw value is literally the bytes that cross the wire.
public enum BatteryKind: String, CaseIterable {
    case laptop = "laptop"
    case headphones = "headphones"
    case keyboard = "keyboard"
    case mouse = "mouse"
    case speaker = "speaker"
    case other = "other"

    /// A token this build does not know reads as `other`.
    public static func of(_ token: String) -> BatteryKind { BatteryKind(rawValue: token) ?? .other }
}

/// The kind token of `TextKind.output`. The raw value is literally the bytes that cross the wire.
public enum OutputKind: String, CaseIterable {
    case speaker = "speaker"
    case headphones = "headphones"
    case monitor = "monitor"
    case other = "other"

    /// A token this build does not know reads as `other`.
    public static func of(_ token: String) -> OutputKind { OutputKind(rawValue: token) ?? .other }
}

/// The whole vocabulary of `TextKind.appAddStatus`: a token the phone matches on, never prose. Only `added` changed
/// the macro list. The raw value is literally the bytes that cross the wire.
public enum AppAddStatus: String, CaseIterable {
    case added = "added"
    case full = "full"
    case duplicate = "duplicate"
    case stale = "stale"
    case failed = "failed"
}

/// What a BLOB_BEGIN carries. The frame holds the raw id, since one this build does not know is read to its end and
/// dropped. Mirrors the BLOB_KIND rows of `protocol/actions.txt`.
public enum BlobKind: UInt8, CaseIterable {
    case clipText = 0
    case clipImage = 1
    case appList = 2
    case appIcon = 3
    case mediaIcon = 4
    case notification = 5
    /// Computer to phone: an approval request. The label is "id/f"; the body is UTF-8 agent, tool and text joined by line feeds.
    case permAsk = 6
    /// Computer to phone: GitHub pull requests and the notification count. The label is "gh"; the body is tab-separated lines.
    case github = 7
}

/// The reason token of `TextKind.unavailable`: a token the phone matches on, never prose. The raw value is literally
/// the bytes that cross the wire.
public enum UnavailableReason: String, CaseIterable {
    case unsupported = "unsupported"
    case off = "off"
    case noTool = "no-tool"
    case noPermission = "no-permission"
}

/// The whole vocabulary of `TextKind.endAction`. The raw value is literally the bytes that cross the wire.
public enum EndActionToken: String, CaseIterable {
    case none = "none"
    case sleep = "sleep"
    case shutdown = "shutdown"
}

/// The whole vocabulary of `TextKind.captureState`. The raw value is literally the bytes that cross the wire.
public enum CaptureStateToken: String, CaseIterable {
    case idle = "idle"
    case recording = "recording"
}

/// The agent of a sessions, limits, spend or approval entry. The raw value is literally the bytes that cross the wire.
public enum AgentToken: String, CaseIterable {
    case claude = "claude"
    case codex = "codex"
}

/// The state in a `TextKind.sessions` entry. The raw value is literally the bytes that cross the wire.
public enum SessionStateToken: String, CaseIterable {
    case busy = "busy"
    case wait = "wait"
    case idle = "idle"
}

/// The CI field of a `BlobKind.github` line. The raw value is literally the bytes that cross the wire.
public enum CiToken: String, CaseIterable {
    case ok = "ok"
    case fail = "fail"
    case pending = "pending"
    case none = "none"
}

/// The non-minute forms of a `TextKind.limits` window. Mirrors the LIMIT_WINDOW rows of `protocol/actions.txt`.
public enum LimitWindow {
    /// The context window of a session, not a time window.
    public static let context = "ctx"
    /// A limit with no duration is this character and a name, such as "~spend_limit".
    public static let namedPrefix = "~"
}
