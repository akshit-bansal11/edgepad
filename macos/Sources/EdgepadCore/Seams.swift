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
    func close()
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

/// The picture a macro button shows, as PNG bytes, or nil when none can be had. Behind a seam so the store and the
/// chunker stay free of AppKit.
public protocol MacroIconSource: AnyObject {
    func png(for macro: Macro) -> [UInt8]?
}
