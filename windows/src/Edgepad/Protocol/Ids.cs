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
    /// Plug in this laptop's virtual controller, and unplug it again. Actions rather than frame types of
    /// their own, because the pad is only ever asked for and given back, and an action costs no protocol
    /// version — this laptop answers with <see cref="TextKind.PadStatus"/>, and one too old to know these
    /// ids drops them and counts them, which the phone reads as the answer never coming.
    /// </summary>
    PadAttach = 35,
    PadDetach = 36,

    /// <summary>
    /// Annotating the screen and signing, which this laptop draws and the phone only steers: Begin shows the
    /// overlay and End removes it, Undo and Clear edit the strokes, and the six tools pick what the next
    /// <see cref="Pen"/> stroke draws. <see cref="SignBegin"/> opens a surface that shows nothing here and
    /// accepts only the pen tool, and <see cref="SignSave"/> asks for the export, answered with
    /// <see cref="TextKind.SignStatus"/>. A laptop too old to know the ids drops and counts them, and the phone
    /// offers them only for one that sent <see cref="Feature.Annotate"/>.
    /// </summary>
    AnnotateBegin = 40,
    AnnotateEnd = 41,
    AnnotateUndo = 42,
    AnnotateClear = 43,
    ToolPen = 44,
    ToolHighlighter = 45,
    ToolLine = 46,
    ToolArrow = 47,
    ToolRectangle = 48,
    ToolEllipse = 49,
    SignBegin = 50,
    SignSave = 51,

    /// <summary>
    /// Screen capture, which is this laptop's own tool: a region (a drag), the whole screen, and a recording.
    /// Actions because the phone only asks; what each one presses is decided here. A laptop too old to know
    /// them drops and counts the ids, and the phone shows the buttons only for one that sent
    /// <see cref="Feature.Capture"/>.
    /// </summary>
    CaptureRegion = 52,
    CaptureScreen = 53,
    CaptureRecord = 54,

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

    /// <summary>
    /// Laptop to phone: whether a virtual controller can be offered at all, as one <see cref="PadStatus"/>
    /// token. Sent in answer to <see cref="ActionId.PadAttach"/> and <see cref="ActionId.PadDetach"/>, and
    /// once after the handshake, so the phone can hide the pad instead of showing a button that quietly
    /// does nothing.
    /// </summary>
    PadStatus = 8,

    /// <summary>
    /// Laptop to phone: the optional features this laptop has, as <see cref="Feature"/> tokens joined by "/",
    /// sent once after the handshake. A feature missing from the list is off, and a token the phone does not
    /// know is ignored, so a newer laptop can name more than an older phone understands.
    /// </summary>
    Features = 9,

    /// <summary>
    /// Laptop to phone: the primary display in pixels, "1920/1080", sent after the handshake and whenever the
    /// display changes, so the phone can shape its drawing surface to the same ratio.
    /// </summary>
    ScreenSize = 10,

    /// <summary>Laptop to phone: how a <see cref="ActionId.SignSave"/> ended, as one <see cref="SignStatus"/> token.</summary>
    SignStatus = 11,
}

/// <summary>
/// The bits of a <see cref="PadState"/>'s <c>Buttons</c> field. These are XInput's own <c>wButtons</c>
/// values, unchanged, because the payload is copied straight into an <c>XINPUT_GAMEPAD</c>: a mapping of our
/// own would be a conversion on the hot path and a second table to keep in step with Microsoft's. Mirrors
/// protocol/actions.txt.
/// <para>
/// 0x0800, between <see cref="Guide"/> and <see cref="A"/>, is unused by XInput and stays unused here. A bit
/// this build does not know is dropped and counted like any other unknown id, never an error.
/// </para>
/// </summary>
[Flags]
internal enum PadButton : ushort
{
    /// <summary>Nothing held. A real state of the field, not padding: it is what arrives as a button comes up.</summary>
    None = 0x0000,

    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

/// <summary>
/// The whole vocabulary of <see cref="TextKind.PadStatus"/>. A token the phone matches on, never prose:
/// <see cref="Ready"/> is the only one that leaves the pad usable, and the other two are the two different
/// things the phone has to tell its user when it falls back to the keyboard. Matching on this laptop's
/// wording instead would make that wording unchangeable. Mirrors protocol/actions.txt.
/// <para>
/// Strings rather than an enum because the token is what crosses the wire, inside a TEXT payload, and a
/// constant that is literally the bytes cannot drift from them.
/// </para>
/// </summary>
internal static class PadStatus
{
    /// <summary>This laptop can plug in a virtual controller.</summary>
    public const string Ready = "ready";

    /// <summary>The virtual-controller driver is not installed here, so there is nothing to plug in.</summary>
    public const string NoDriver = "no-driver";

    /// <summary>The driver is there but plugging the pad in failed.</summary>
    public const string AttachFailed = "attach-failed";
}

/// <summary>
/// The whole vocabulary of <see cref="TextKind.Features"/>. Tokens the phone matches on, never prose, for the
/// reason <see cref="PadStatus"/> is, and strings for the same reason: the constant is literally the bytes.
/// Mirrors protocol/actions.txt.
/// </summary>
internal static class Feature
{
    /// <summary>This laptop can draw on its screen from the phone.</summary>
    public const string Annotate = "annotate";

    /// <summary>This laptop answers the capture actions.</summary>
    public const string Capture = "capture";
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

    /// <summary>
    /// The value is an index into <see cref="Palette"/>, never an RGB: the phone cannot name a colour this
    /// laptop did not agree to, and an index of 8 or more is dropped and counted.
    /// </summary>
    PenColor = 5,
}

/// <summary>
/// The whole vocabulary of <see cref="TextKind.SignStatus"/>. Tokens the phone matches on, never prose, and
/// strings, for the reason <see cref="PadStatus"/> is. Mirrors protocol/actions.txt.
/// </summary>
internal static class SignStatus
{
    /// <summary>The signature is on the clipboard and in the Pictures folder.</summary>
    public const string Saved = "saved";

    /// <summary>Nothing was drawn, so there was nothing to export.</summary>
    public const string Empty = "empty";

    /// <summary>The export or the save failed.</summary>
    public const string Failed = "failed";
}

/// <summary>
/// The colours <see cref="ControlId.PenColor"/> indexes, by position. Bytes 0xRRGGBB, a test holds them to
/// the PALETTE rows of protocol/actions.txt, and the phone keeps its own copy for its swatches. The value
/// that crosses the wire is only ever the index, so an RGB the phone invented cannot reach the overlay.
/// </summary>
internal static class Palette
{
    public const int Count = 8;

    private static readonly int[] Rgb =
    [
        0x000000, // BLACK
        0xFFFFFF, // WHITE
        0xFF3B30, // RED
        0xFF9500, // ORANGE
        0xFFCC00, // YELLOW
        0x34C759, // GREEN
        0x0068D6, // BLUE
        0xAF52DE, // PURPLE
    ];

    /// <summary>The 0xRRGGBB colour at <paramref name="index"/>, or null when the index is not in the palette.</summary>
    public static int? Colour(int index) => index is >= 0 and < Count ? Rgb[index] : null;
}
