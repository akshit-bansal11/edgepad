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
    /// The first of 32 macro slots, 64 to 95; slot n is `macroBase + n`. The phone sends the index and never
    /// what it launches: this computer's own list decides that, which is the whole security model.
    case macroBase = 64

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
}

/// The bits of a PAD_STATE frame's buttons field: XInput's own `wButtons` values, unchanged, because the
/// payload is laid out like `XINPUT_GAMEPAD`. 0x0800 is unused by XInput and stays unused here.
public enum PadButton: UInt16, CaseIterable {
    case none = 0
    case dpadUp = 1
    case dpadDown = 2
    case dpadLeft = 4
    case dpadRight = 8
    case start = 16
    case back = 32
    case leftThumb = 64
    case rightThumb = 128
    case leftShoulder = 256
    case rightShoulder = 512
    case guide = 1024
    case a = 4096
    case b = 8192
    case x = 16384
    case y = 32768
}

/// The whole vocabulary of `TextKind.padStatus`: a token the phone matches on, never prose, so the wording
/// of the app stays changeable. The raw value is literally the bytes that cross the wire.
public enum PadStatus: String, CaseIterable {
    case ready = "ready"
    case noDriver = "no-driver"
    case attachFailed = "attach-failed"
}
