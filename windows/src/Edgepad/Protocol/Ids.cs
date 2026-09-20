namespace Edgepad.Protocol;

/// <summary>What a RUN_ACTION frame may ask for. Mirrors protocol/actions.txt; a test holds them equal.</summary>
internal enum ActionId : byte
{
    MuteToggle = 1,
    PlayPause = 2,
    NextTrack = 3,
    PreviousTrack = 4,
    MicMuteToggle = 5,
    Lock = 6,
    TaskView = 20,
    ShowDesktop = 21,
    Search = 22,
    Notifications = 23,
    DesktopLeft = 24,
    DesktopRight = 25,
    AppSwitchBegin = 26,
    AppSwitchNext = 27,
    AppSwitchPrevious = 28,
    AppSwitchEnd = 29,
    ZoomReset = 30,
    VolumeUp = 31,
    VolumeDown = 32,
    BrightnessUp = 33,
    BrightnessDown = 34,

    /// <summary>
    /// The first of 32 macro slots, 64..95; slot n is <c>MacroBase + n</c>. The phone sends the index and
    /// never what it launches — this laptop's own list decides that, which is the whole security model.
    /// A laptop too old to know the block drops the id and counts it, so this needed no protocol version.
    /// </summary>
    MacroBase = 64,
}

/// <summary>What a TEXT frame carries. Only kind 3 goes phone to laptop; the rest go laptop to phone. Mirrors protocol/actions.txt.</summary>
internal enum TextKind : byte
{
    NowPlaying = 0,
    App = 1,
    Timeline = 2,
    Type = 3,

    /// <summary>"60/120/144": this display's available rates, in the order <see cref="ControlId.RefreshRate"/> indexes them.</summary>
    RefreshRates = 4,

    /// <summary>"Chrome/Spotify/Notes": this laptop's macro names, in the order MacroBase indexes them.</summary>
    Macros = 5,

    /// <summary>
    /// One piece of one macro's icon: "slot/chunk/chunks/base64". An icon does not fit the 255 bytes a TEXT
    /// payload holds, so it arrives in pieces and the phone joins them. A kind rather than a frame type of
    /// its own, because an unknown kind is dropped and counted while an unknown type closes the connection —
    /// which is the difference between a feature an older phone ignores and one it cannot survive.
    /// </summary>
    MacroIcon = 6,

    /// <summary>
    /// Phone to laptop: send the macro icons. Empty payload. Icons are answered rather than pushed, so they
    /// cross the link when the phone opens the macro grid and never while a finger is on the trackpad.
    /// </summary>
    WantIcons = 7,
}

/// <summary>What a SET_VALUE frame may set and a STATE frame reports. Mirrors protocol/actions.txt.</summary>
internal enum ControlId : byte
{
    Volume = 0,
    Brightness = 1,
    MicLevel = 2,
    MediaPosition = 3,

    /// <summary>
    /// The value is an index into the <see cref="TextKind.RefreshRates"/> list, never a rate in hertz. SET carries
    /// value u8 and the dispatcher drops anything above 100, so 120 or 144 could not cross the wire at all; an index
    /// is always well under 100. It also makes a rate this display does not have unrepresentable, not merely rejected.
    /// </summary>
    RefreshRate = 4,
}
