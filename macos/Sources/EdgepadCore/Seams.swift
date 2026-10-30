/// The seams between the pure half of the Mac app and the half that touches macOS. Everything in this
/// module decides; everything behind these protocols acts. That is what lets the dispatcher's drop paths
/// and the handshake's refusals run as unit tests on a build machine with no Bluetooth radio, no
/// Accessibility permission and no speakers.

/// A key on this Mac's keyboard, by macOS virtual key code (`kVK_*`). `KeyMap` names the ones the app uses.
public struct MacKey: Hashable {
    public let code: UInt16

    public init(_ code: UInt16) {
        self.code = code
    }
}

/// The keys macOS treats as system keys rather than keyboard keys, by their `NX_KEYTYPE_*` value. They draw
/// the system's own volume and brightness readout, which a level set through Core Audio does not.
public enum MediaKey: Int32 {
    case soundUp = 0
    case soundDown = 1
    case brightnessUp = 2
    case brightnessDown = 3
    case mute = 7
    case play = 16
    case next = 17
    case previous = 18
}

/// Input into the user's desktop. An implementation remembers everything it is holding down, so that
/// `releaseAll` can lift it when a session ends: a link dropped mid-gesture must never leave a key or a
/// mouse button stuck with nothing left alive to release it.
public protocol InputSink: AnyObject {
    func move(dx: Int, dy: Int)
    /// 0 left, 1 right, 2 middle. The dispatcher never passes any other id.
    func button(_ id: UInt8, down: Bool)
    /// Wheel units as the wire carries them: 120 is one notch, positive dy is wheel forward, positive dx is right.
    func scroll(dx: Int, dy: Int)
    /// Types text as the phone's keyboard produced it; "\u{8}" is backspace and "\n" is return.
    func type(_ text: String)
    /// One key the phone is pressing or releasing, remembered while it is down.
    func key(_ key: MacKey, down: Bool)
    /// Presses every key in order, then releases them in reverse.
    func chord(_ keys: [MacKey])
    func hold(_ key: MacKey)
    func release(_ key: MacKey)
    func isHeld(_ key: MacKey) -> Bool
    func media(_ key: MediaKey)
    func releaseAll()
}

public struct Level: Equatable {
    public let percent: Int
    public let muted: Bool

    public init(percent: Int, muted: Bool) {
        self.percent = percent
        self.muted = muted
    }
}

/// The default speakers, or the default microphone. Every answer may be "no": a Mac can have no
/// microphone, and a device can refuse a level or a mute.
public protocol LevelControl: AnyObject {
    func read() -> Level?
    func setLevel(_ percent: Int) -> Bool
    func toggleMute() -> Bool
}

/// The built-in panel's brightness, 0 to 100, or nil where this Mac will not say.
public protocol BrightnessControl: AnyObject {
    func read() -> Int?
    func set(_ percent: Int) -> Bool
}

/// The refresh rates the main display offers at its current resolution, in hertz, and which one is in force.
public protocol DisplayRates: AnyObject {
    var rates: [Int] { get }
    /// The index into `rates` of the rate in force, or nil when it is not one of them.
    var current: Int? { get }
    func refresh()
    func select(_ index: Int) -> Bool
}

/// The link to one phone, as the session sees it: bytes out, and a way to hang up.
public protocol Transport: AnyObject {
    /// False when the link is dead and the bytes went nowhere.
    func send(_ bytes: [UInt8]) -> Bool
    /// Bytes taken and not yet gone. Whatever sends many frames holds off at `CaptureToPhone.maxBacklog`.
    var backlog: Int { get }
    func close()
}

/// What carries the link, which sets how fast a blob may go.
public enum LinkKind {
    case bluetooth
    case network
}

/// What a clipboard holds. `sensitive` is the platform's verdict; a flag it could not read counts as set.
public struct ClipboardContent: Equatable {
    public let kind: BlobKind
    public let data: [UInt8]
    public let sensitive: Bool

    public init(kind: BlobKind, data: [UInt8], sensitive: Bool = false) {
        self.kind = kind
        self.data = data
        self.sensitive = sensitive
    }
}

/// This Mac's pasteboard. Main thread only, like the launcher; a denied read is nil, never an error.
public protocol ClipboardSurface: AnyObject {
    /// The clipboard now, or nil for nothing that can be shared.
    func read() -> ClipboardContent?
    /// False when it would not take the content.
    func write(_ content: ClipboardContent) -> Bool
}

/// The annotation overlay and signature export, which draw on this Mac's screen. The dispatcher hands it the pen
/// and the annotate actions; a Mac without one (and a test) leaves it nil, and those frames are dropped and counted.
/// Every answer is false for a frame it did not take.
public protocol AnnotationSink: AnyObject {
    /// The primary display in pixels, or nil when there is none.
    var screenSize: (width: Int, height: Int)? { get }
    /// Where the answer to SIGN_SAVE goes. Set by the session, which owns the link, and cleared by `end()`.
    var signStatusReply: ((String) -> Void)? { get set }
    /// Called when the display changes, so the session can send the phone the new size.
    var screenSizeChanged: (() -> Void)? { get set }
    func pen(x: UInt16, y: UInt16, state: UInt8) -> Bool
    func setColour(_ index: Int) -> Bool
    /// One of the annotate and sign actions, 40..51.
    func run(_ action: ActionId) -> Bool
    /// Takes the overlay down and discards the strokes. Idempotent.
    func end()
}

/// Opens what a macro points at: an application, a document, a folder or a URL. The only thing in the app that
/// starts a process on the phone's behalf, and it is handed a target the owner wrote into the list, never one the
/// phone sent. False when nothing was started.
public protocol MacroLauncher: AnyObject {
    func launch(target: String, arguments: String?) -> Bool
}

/// What a macro's app is doing on this Mac right now. The raw value is the character the phone reads.
public enum OpenState: Character {
    /// Nothing this Mac can follow: a URL, a folder, a document, anything that is not an application bundle, or a macro
    /// with arguments, which always launches.
    case untracked = "-"
    case closed = "0"
    case open = "1"
    case front = "2"
}

/// The running apps macros open: the OS-specific part of the toggle and the close cross. Every call takes a `Macro`
/// out of this Mac's own list: the phone names a slot, and no string of its ever reaches a match, a raise or a close.
/// Main thread only, like the launcher; the store's default scheduler is what puts it there.
public protocol MacroWindows: AnyObject {
    /// False where this desktop cannot be surveyed at all: no crosses on the phone, and a press just launches.
    var available: Bool { get }
    /// One state per macro, in order, from one pass over the desktop.
    func survey(_ macros: [Macro]) -> [OpenState]
    /// Hides the macro's app. False when it did not act.
    func minimize(_ macro: Macro) -> Bool
    /// Brings the macro's app to the front, unhiding it if need be. False when it did not act.
    func raise(_ macro: Macro) -> Bool
    /// Asks the macro's app to quit, so it may ask to save. Never a kill. False when it did not act.
    func close(_ macro: Macro) -> Bool
}

/// The picture a macro button shows, as PNG bytes at about `size` pixels square (falling back to smaller tiers if it
/// is too heavy), or nil when none can be had. Behind a seam so the store and the chunker stay free of AppKit.
public protocol MacroIconSource: AnyObject {
    func png(for macro: Macro, size: Int) -> [UInt8]?
}

/// One app on offer: what its macro would be. `iconPath` becomes `Macro.icon`; nil takes the picture from the target.
/// Everything in it comes from this Mac, never from the phone: see `AppPicker`.
public struct AppEntry: Equatable {
    public let name: String
    public let target: String
    public let arguments: String?
    public let iconPath: String?

    public init(name: String, target: String, arguments: String? = nil, iconPath: String? = nil) {
        self.name = name
        self.target = target
        self.arguments = arguments
        self.iconPath = iconPath
    }
}

/// The apps this Mac would offer the phone as macros. Everything an entry says comes from this machine, which is what
/// lets the phone add a macro at all. Main thread only, like the launcher: the icons are drawn with AppKit.
public protocol AppCatalog: AnyObject {
    /// What is installed now. May be slow and may throw; `AppPicker` runs it on a turn of its own, never inline with
    /// the frame that asked.
    func scan() throws -> [AppEntry]
    /// A PNG of this app, or nil for none. Asked per entry, after the list has gone, because it is the slow part.
    func icon(for app: AppEntry) throws -> [UInt8]?
}

/// One thing directly inside a folder.
public struct AppFolderEntry: Equatable {
    public let name: String
    /// A folder, or a link to one. An `.app` bundle is one.
    public let isFolder: Bool

    public init(name: String, isFolder: Bool) {
        self.name = name
        self.isFolder = isFolder
    }
}

/// The folders `AppBundles` walks, behind a seam so the walk is a unit test over literal listings.
public protocol AppFolders: AnyObject {
    /// What is directly inside `path`, or nil when it is not there or cannot be read.
    func entries(in path: String) -> [AppFolderEntry]?
}
