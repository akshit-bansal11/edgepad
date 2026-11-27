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

    /// <summary>
    /// Power, phone to laptop: the phone asks and this laptop decides. <see cref="Sleep"/> and <see cref="Hibernate"/> act at once;
    /// <see cref="Restart"/> and <see cref="ShutDown"/> start a 60 s cancellable countdown, reported as <see cref="TextKind.PowerState"/>,
    /// and <see cref="PowerCancel"/> stops it. A laptop that cannot do one drops the id and counts it.
    /// </summary>
    Sleep = 7,
    Hibernate = 8,
    Restart = 9,
    ShutDown = 10,
    PowerCancel = 11,
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
    /// The laser tool: the next <see cref="Pen"/> stroke is a dot that follows the finger and leaves nothing behind. Like the
    /// other tools, offered by the phone only for a laptop that sent <see cref="Feature.Annotate"/>.
    /// </summary>
    ToolLaser = 37,

    /// <summary>
    /// The trailing laser: <see cref="ToolLaser"/> plus a short red trail behind the dot. Both lasers take their size from
    /// the pen width.
    /// </summary>
    ToolLaserTrail = 38,

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
    /// The phone left its capture screen: dismiss a capture overlay this laptop opened, only if one may still be
    /// open. Within protocol 5, which is unreleased, so it needed no version.
    /// </summary>
    CaptureCancel = 55,

    /// <summary>
    /// Adding a macro from the phone: AppsList asks this laptop to send the apps it could add, as a <see cref="BlobKind.AppList"/>
    /// blob and then a <see cref="BlobKind.AppIcon"/> blob each, and AppsCancel stops that list and its icons. The phone offers
    /// them only for a laptop that sent <see cref="Feature.Apps"/>.
    /// </summary>
    AppsList = 56,
    AppsCancel = 57,

    /// <summary>
    /// The macro grid is open and wants to know which macro apps are running: MacrosWatch starts this laptop's survey, which
    /// reports <see cref="TextKind.MacroOpen"/> on change only, and MacrosUnwatch stops it. Forgotten when the session ends.
    /// </summary>
    MacrosWatch = 58,
    MacrosUnwatch = 59,

    /// <summary>
    /// Phone to laptop: arm this laptop to sleep or shut down when the current media ends (EndSleep, EndShutdown), or disarm it
    /// (EndCancel). This laptop reports the armed state as <see cref="TextKind.EndAction"/>. The phone offers them only for a
    /// laptop that sent <see cref="Feature.EndAction"/>, and a laptop too old to know the ids drops and counts them.
    /// </summary>
    EndSleep = 60,
    EndShutdown = 61,
    EndCancel = 62,

    /// <summary>
    /// The first of 32 macro slots, 64..95; slot n is <c>MacroBase + n</c>. The phone sends the index and
    /// never what it launches — this laptop's own list decides that, which is the whole security model.
    /// A laptop too old to know the block drops the id and counts it, so this needed no protocol version.
    /// </summary>
    MacroBase = 64,

    /// <summary>
    /// A reserved block of 32, 96..127: close the app macro slot n opens, as its own close button would, never a kill.
    /// The phone sends the slot and nothing else; this laptop decides what that slot is.
    /// </summary>
    MacroCloseBase = 96,
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
    /// Phone to laptop: send the macro icons. The payload is a decimal side in pixels, and empty means 128. Icons are answered rather than pushed, so they
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

    /// <summary>Laptop to phone: why a capture the phone asked for did not reach it, as one <see cref="CaptureStatus"/> token.</summary>
    CaptureStatus = 12,

    /// <summary>
    /// Laptop to phone, once after the handshake: "id/os/name", the id 32 hex digits, the os a <see cref="DeviceOs"/> token
    /// and this laptop's own name. The id lets the phone tell one laptop seen over two transports from two.
    /// </summary>
    Device = 13,

    /// <summary>Phone to laptop: "epoch.index", an index into the app list this laptop sent for that epoch. The phone names no path.</summary>
    AppAdd = 14,

    /// <summary>Laptop to phone: how an <see cref="AppAdd"/> ended, as one <see cref="AppAddStatus"/> token.</summary>
    AppAddStatus = 15,

    /// <summary>
    /// Laptop to phone: one character per macro slot, in <see cref="Macros"/> order. '-' not trackable, '0' not running,
    /// '1' running, '2' running and in front. Sent when it changes while watched; an unknown character reads as untracked.
    /// </summary>
    MacroOpen = 16,

    /// <summary>
    /// Phone to laptop: a stroke colour as exactly six hex digits, "RRGGBB". Safe to take from the phone, unlike a path
    /// or a command, because it is only a colour: <see cref="Edgepad.Annotation.PenStyle.TryParseRgb"/> accepts those six
    /// characters and nothing else, and what it returns is an int that can only be drawn with.
    /// </summary>
    PenRgb = 17,

    /// <summary>
    /// Laptop to phone: "blocked" while the OS discards synthetic input, "ok" otherwise. Every laptop app sends it (Linux:
    /// no uinput access; Windows: a refused input batch).
    /// </summary>
    InputState = 18,

    /// <summary>
    /// Laptop to phone: a snapshot of the whole battery list, "kind:level:charging:name" entries joined by "|", the
    /// laptop first. Empty means no devices. See docs/PROTOCOL.md for the size rule and cadence.
    /// </summary>
    Batteries = 19,

    /// <summary>Laptop to phone: the audio output, "kind/name" split on the first "/", kind an <see cref="OutputKind"/> token. Empty means none known.</summary>
    Output = 20,

    /// <summary>
    /// Laptop to phone: the app in front, "token/name" split on the first "/", token 1 to 64 of A-Z a-z 0-9 . _ - and name cut to
    /// 40 code points. Empty means nothing known. Sent only after <see cref="ControlId.ForegroundWatch"/> is 1, on change, and
    /// never logged.
    /// </summary>
    ForegroundApp = 21,

    /// <summary>Laptop to phone: how many macro slots this laptop keeps, in decimal such as "15". Empty or never sent means the phone's own default.</summary>
    MacroCap = 22,

    /// <summary>Laptop to phone: a <see cref="CaptureStateToken"/>, "idle" or "recording". Best effort: a laptop that cannot tell sends nothing.</summary>
    CaptureState = 23,

    /// <summary>
    /// Laptop to phone: the features this laptop cannot offer right now and why, "feature:reason" entries joined by "/", reason an
    /// <see cref="UnavailableReason"/> token. Empty means none.
    /// </summary>
    Unavailable = 24,

    /// <summary>Laptop to phone: what <see cref="ActionId.EndSleep"/> armed, as one <see cref="EndActionToken"/>. Sent on change.</summary>
    EndAction = 25,

    /// <summary>
    /// Laptop to phone: the title line of what is playing before it is cleaned for <see cref="NowPlaying"/>. Sent only when it
    /// differs from that text.
    /// </summary>
    NowPlayingFull = 26,

    /// <summary>
    /// Laptop to phone: "cpu/ram/gpu/vram/down/up/ping/loss/ctemp/gtemp/gpower", percents 0..100, down and up in KiB/s, ping in ms,
    /// temperatures in C, power in W, -1 unknown. Once a second while <see cref="ControlId.DashWatch"/> has bit 0 or 1, not re-sent when identical.
    /// </summary>
    Stats = 27,

    /// <summary>Laptop to phone: "cpuName|ramMiB|gpuName|vramMiB|netName", names cleaned to 30 code points. Sent when a watch starts and when it changes.</summary>
    StatsInfo = 28,

    /// <summary>Laptop to phone: "label:usedPct:totalGiB:freeGiB" entries joined by "|", at most 6. Sent while bit 1 of <see cref="ControlId.DashWatch"/> is set, on change.</summary>
    Disks = 29,

    /// <summary>Laptop to phone: "agent:state:title" entries joined by "|", newest first, at most 8, agent an <see cref="AgentToken"/> and state a <see cref="SessionStateToken"/>. Empty means none.</summary>
    Sessions = 30,

    /// <summary>Phone to laptop: exactly "id/a" or "id/d", the id 12 lowercase hex digits. Anything else, or an unknown or answered id, is dropped and counted.</summary>
    PermAnswer = 31,

    /// <summary>Laptop to phone: "id" of an approval that was answered elsewhere, timed out or died, so the phone dismisses its sheet.</summary>
    PermGone = 32,

    /// <summary>
    /// Laptop to phone: "agent:window:pct:resetAtMin:measuredAtMin" entries joined by "|". The window is decimal minutes (1..525600),
    /// <see cref="LimitWindow.Context"/>, or <see cref="LimitWindow.NamedPrefix"/> and a name for a limit with no duration. See protocol/actions.txt.
    /// </summary>
    Limits = 33,

    /// <summary>Laptop to phone: "agent/measuredAtMin/tokToday/centsToday/tokWeek/centsWeek/tokMonth/centsMonth" entries joined by "|". Cents are estimates at API list prices.</summary>
    Spend = 34,

    /// <summary>Laptop to phone: "tempTenthsC/wmoCode/city", city cleaned to 24 code points. Empty means none.</summary>
    Weather = 35,

    /// <summary>
    /// Laptop to phone: "kind|band|linkMbps|signalPct|localIp|gateway". Kind is <see cref="NetKind"/>, band <c>2.4</c>, <c>5</c>, <c>6</c> or
    /// <c>?</c>, the numbers -1 when unknown, the addresses text (empty when none). No network name. Sent while bit 5 of
    /// <see cref="ControlId.DashWatch"/> is set, on change.
    /// </summary>
    NetInfo = 36,

    /// <summary>
    /// Laptop to phone: "state|downKbps|upKbps|latencyMs|jitterMs|atUnixMin|bytes", state a <see cref="SpeedState"/>, -1 unknown. Sent while a
    /// <see cref="ControlId.SpeedTest"/> runs and when it ends, and again when the phone watches bit 5 and a result is held.
    /// </summary>
    SpeedResult = 37,

    /// <summary>
    /// Phone to laptop: exactly "host/listRevision/index": the host a git provider id, the revision from the first line of
    /// the <see cref="BlobKind.Git"/> blob, and the zero-based position among that blob's repository and item lines. The laptop opens only the
    /// https address it fetched itself for that revision and index, in the default browser; anything else is dropped and counted.
    /// </summary>
    GitOpen = 38,

    /// <summary>
    /// Laptop to phone: the power countdown, one <see cref="PowerStateToken"/> and the seconds left ("restart/60"), or empty for none.
    /// Sent on arm, cancel and expiry only, never per second: the phone counts down locally. The countdown keeps running if the phone
    /// disconnects, and a reconnecting phone is told once with the seconds left.
    /// </summary>
    PowerState = 39,

    /// <summary>
    /// Laptop to phone: what kind of app is in front, one <see cref="AppKindToken"/>, or empty for none. Sent on change, only while
    /// <see cref="ControlId.ForegroundWatch"/> is 1. Never a title. A laptop that cannot classify sends nothing.
    /// </summary>
    ForegroundKind = 40,
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

    /// <summary>This laptop's clipboard toggle is on, so <see cref="ControlId.Clipboard"/> is honoured.</summary>
    public const string Clipboard = "clipboard";

    /// <summary>This laptop can list its apps for the phone to add as macros.</summary>
    public const string Apps = "apps";

    /// <summary>This laptop can tell which macro apps are running, in <see cref="TextKind.MacroOpen"/>.</summary>
    public const string Running = "running";

    /// <summary>This laptop answers <see cref="ActionId.EndSleep"/>, <see cref="ActionId.EndShutdown"/> and <see cref="ActionId.EndCancel"/>.</summary>
    public const string EndAction = "endaction";

    /// <summary>This laptop can forward its notifications once <see cref="ControlId.Notify"/> is 1.</summary>
    public const string Notify = "notify";

    /// <summary>This laptop can say which app is in front, in <see cref="TextKind.ForegroundApp"/>, once <see cref="ControlId.ForegroundWatch"/> is 1.</summary>
    public const string Foreground = "foreground";

    /// <summary>This laptop can send <see cref="TextKind.Stats"/>, <see cref="TextKind.StatsInfo"/> and <see cref="TextKind.Disks"/>; named only while its stats switch is on and usable.</summary>
    public const string Stats = "stats";

    /// <summary>This laptop can send <see cref="TextKind.Weather"/>; named only while its weather switch is on.</summary>
    public const string Weather = "weather";

    /// <summary>This laptop's Claude Code integration is on and usable: <see cref="TextKind.Limits"/>, <see cref="TextKind.Spend"/>, <see cref="TextKind.Sessions"/> and approvals.</summary>
    public const string Claude = "claude";

    /// <summary>This laptop's Codex integration is on and usable, as <see cref="Claude"/> is for Claude Code.</summary>
    public const string Codex = "codex";

    /// <summary>This laptop is signed in to GitHub and can send <see cref="BlobKind.Git"/>.</summary>
    public const string GitHub = "github";

    /// <summary>Developer mode is on; <see cref="Claude"/>, <see cref="Codex"/> and <see cref="GitHub"/> are named only while it is.</summary>
    public const string Developer = "developer";

    /// <summary>This laptop can send <see cref="TextKind.NetInfo"/> and run a <see cref="ControlId.SpeedTest"/>; named only while its stats switch is on and it can read the network.</summary>
    public const string NetInfo = "netinfo";

    /// <summary>This laptop's Gemini CLI integration is on and usable, as <see cref="Claude"/> is for Claude Code.</summary>
    public const string Gemini = "gemini";

    /// <summary>This laptop's OpenCode integration is on and usable, as <see cref="Claude"/> is for Claude Code.</summary>
    public const string OpenCode = "opencode";

    /// <summary>This laptop is signed in to GitLab, as <see cref="GitHub"/> is for GitHub.</summary>
    public const string GitLab = "gitlab";

    /// <summary>This laptop has a Gitea, Forgejo or Codeberg token, as <see cref="GitHub"/> is for GitHub.</summary>
    public const string Gitea = "gitea";

    /// <summary>This laptop has a Bitbucket token, as <see cref="GitHub"/> is for GitHub.</summary>
    public const string Bitbucket = "bitbucket";

    /// <summary>This laptop has a Gitee token, as <see cref="GitHub"/> is for GitHub.</summary>
    public const string Gitee = "gitee";

    /// <summary>This laptop can sleep on request, <see cref="ActionId.Sleep"/>.</summary>
    public const string Sleep = "sleep";

    /// <summary>This laptop can hibernate on request, <see cref="ActionId.Hibernate"/>.</summary>
    public const string Hibernate = "hibernate";

    /// <summary>This laptop can restart after a countdown, <see cref="ActionId.Restart"/> and <see cref="ActionId.PowerCancel"/>.</summary>
    public const string Restart = "restart";

    /// <summary>This laptop can shut down after a countdown, <see cref="ActionId.ShutDown"/> and <see cref="ActionId.PowerCancel"/>.</summary>
    public const string ShutDown = "shutdown";

    /// <summary>This laptop can classify the app in front, in <see cref="TextKind.ForegroundKind"/>, once <see cref="ControlId.ForegroundWatch"/> is 1.</summary>
    public const string AppKind = "appkind";
}

/// <summary>The first field of a <see cref="TextKind.NetInfo"/>. Tokens, never prose. Mirrors docs/PROTOCOL.md.</summary>
internal static class NetKind
{
    public const string Wifi = "wifi";
    public const string Ethernet = "ethernet";
    public const string Cellular = "cellular";
    public const string Other = "other";
    public const string None = "none";
}

/// <summary>The first field of a <see cref="TextKind.SpeedResult"/>. Tokens, never prose. Mirrors docs/PROTOCOL.md.</summary>
internal static class SpeedState
{
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Capped = "capped";
}

/// <summary>The agent of a <see cref="TextKind.Sessions"/>, <see cref="TextKind.Limits"/>, <see cref="TextKind.Spend"/> or <see cref="BlobKind.PermAsk"/> entry. Tokens, never prose. Mirrors protocol/actions.txt.</summary>
internal static class AgentToken
{
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string Gemini = "gemini";
    public const string OpenCode = "opencode";
}

/// <summary>The state in a <see cref="TextKind.Sessions"/> entry. Mirrors protocol/actions.txt.</summary>
internal static class SessionStateToken
{
    /// <summary>The agent is working.</summary>
    public const string Busy = "busy";

    /// <summary>The agent waits for the person (an approval or a question).</summary>
    public const string Wait = "wait";

    /// <summary>The agent is idle.</summary>
    public const string Idle = "idle";
}

/// <summary>The CI field of a <see cref="BlobKind.Git"/> item line. Mirrors protocol/actions.txt.</summary>
internal static class CiToken
{
    public const string Ok = "ok";
    public const string Fail = "fail";
    public const string Pending = "pending";
    public const string None = "none";
}

/// <summary>
/// The window field of a <see cref="TextKind.Limits"/> entry is decimal minutes (1..525600, so 300 is five hours, 10080 a week and
/// 43200 thirty days), or one of these two forms. Mirrors the LIMIT_WINDOW rows of protocol/actions.txt.
/// </summary>
internal static class LimitWindow
{
    /// <summary>The context window of a session, not a time window.</summary>
    public const string Context = "ctx";

    /// <summary>A limit with no duration is this character and a name, such as "~spend_limit".</summary>
    public const char NamedPrefix = '~';
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
    /// The value is an index into <see cref="Palette"/>, the eight stock colours; an index of 8 or more is dropped and
    /// counted. A colour outside the palette is <see cref="TextKind.PenRgb"/>.
    /// </summary>
    PenColor = 5,

    /// <summary>
    /// Where a capture ends up, as a <see cref="CaptureTarget"/> value: this laptop keeps it, or sends it to the phone
    /// and deletes its copy once the phone says it saved it. Anything else is dropped and counted.
    /// </summary>
    CaptureTarget = 6,

    /// <summary>
    /// Whether this link shares the clipboard, 1 or 0, phone to laptop. This laptop forgets it when the session ends.
    /// Anything else is dropped and counted.
    /// </summary>
    Clipboard = 7,

    /// <summary>
    /// The width of the next pen-tool strokes, 1..100, phone to laptop: 0.02% of the screen's height per step, so 5 is
    /// 0.1%, 20 is the default 0.4% and 100 is 2.0%. 0 or more than 100 is dropped and counted.
    /// </summary>
    PenWidth = 8,

    /// <summary>The opacity of the next strokes in percent, 10..100, phone to laptop. Anything else is dropped and counted.</summary>
    PenOpacity = 9,

    /// <summary>Whether this link forwards the laptop's notifications, 1 or 0, phone to laptop. Forgotten when the session ends. Anything else is dropped and counted.</summary>
    Notify = 10,

    /// <summary>Whether this link hears which app is in front, 1 or 0, phone to laptop. Forgotten when the session ends. Anything else is dropped and counted.</summary>
    ForegroundWatch = 11,

    /// <summary>
    /// Phone to laptop: a bitmask of what the phone draws right now: bit 0 (1) widgets needing system numbers, bit 1 (2) the System
    /// screen, bit 2 (4) the weather widget, bit 3 (8) an AI widget or the AI screen, bit 4 (16) the GitHub screen, bit 5 (32) the Network page. 0..63; larger is
    /// dropped and counted. Forgotten when the session ends. The only thing that makes the laptop sample, read usage files or poll.
    /// </summary>
    DashWatch = 12,

    /// <summary>Developer mode, 1 or 0, phone to laptop. The laptop keeps it across sessions; the phone shows it from FEATURES <see cref="Feature.Developer"/>. Anything else is dropped and counted.</summary>
    Developer = 13,

    /// <summary>Phone to laptop: 1 starts one speed test, 0 cancels the running one. Anything else is dropped and counted. See <see cref="TextKind.SpeedResult"/>.</summary>
    SpeedTest = 14,
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

/// <summary>The values of <see cref="ControlId.CaptureTarget"/>.</summary>
internal enum CaptureTarget : byte
{
    /// <summary>The OS tool saves where it saves, and nothing crosses the link.</summary>
    Laptop = 0,

    /// <summary>Each capture goes to the phone, and the copy here is deleted only once the phone says it saved it.</summary>
    Phone = 1,
}

/// <summary>
/// The whole vocabulary of <see cref="TextKind.CaptureStatus"/>: why a capture stayed on this laptop. Tokens the phone
/// matches on, never prose, and strings, for the reason <see cref="PadStatus"/> is. Mirrors protocol/actions.txt.
/// </summary>
internal static class CaptureStatus
{
    /// <summary>The file is over its kind's limit, so it stays here.</summary>
    public const string TooLarge = "too-large";

    /// <summary>No new file turned up where the OS tool saves, so nothing was sent and nothing deleted.</summary>
    public const string NotFound = "not-found";

    /// <summary>The file was found and could not be read or sent.</summary>
    public const string Failed = "failed";
}

/// <summary>
/// What a <see cref="FileBegin"/> may carry. An id, never a MIME string, so the phone writes only these types; this
/// laptop sends only a file whose extension is listed. Mirrors the FILE_TYPE and FILE_LIMIT rows of
/// protocol/actions.txt; <see cref="FileTypes"/> holds the rest of each row.
/// </summary>
internal enum FileType : byte
{
    Png = 0,
    Jpeg = 1,
    Mp4 = 2,
    Mov = 3,
    Webm = 4,
}

/// <summary>The MIME type, extension and size limit of each <see cref="FileType"/>.</summary>
internal static class FileTypes
{
    public const long ImageLimit = 50L * 1024 * 1024;
    public const long VideoLimit = 200L * 1024 * 1024;

    public static string Mime(FileType type) => type switch
    {
        FileType.Png => "image/png",
        FileType.Jpeg => "image/jpeg",
        FileType.Mp4 => "video/mp4",
        FileType.Mov => "video/quicktime",
        _ => "video/webm",
    };

    public static string Extension(FileType type) => type switch
    {
        FileType.Png => "png",
        FileType.Jpeg => "jpg",
        FileType.Mp4 => "mp4",
        FileType.Mov => "mov",
        _ => "webm",
    };

    public static bool IsVideo(FileType type) => type is FileType.Mp4 or FileType.Mov or FileType.Webm;

    /// <summary>The largest file of this kind that may cross the link, in bytes.</summary>
    public static long Limit(FileType type) => IsVideo(type) ? VideoLimit : ImageLimit;

    /// <summary>The type a file name's extension names, or null for one that is not in the table.</summary>
    public static FileType? FromPath(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.');
        foreach (var type in Enum.GetValues<FileType>())
        {
            if (string.Equals(Extension(type), extension, StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }

        return null;
    }
}

/// <summary>What a <see cref="FileResult"/> says. Only <see cref="Saved"/> lets this laptop delete its copy. Mirrors protocol/actions.txt.</summary>
internal enum FileStatus : byte
{
    /// <summary>The file is in the phone's gallery and its SHA-256 matched.</summary>
    Saved = 0,

    /// <summary>Every byte arrived and the SHA-256 did not match.</summary>
    BadHash = 1,

    /// <summary>The phone refused the file: a type, size, name or chunk it will not take.</summary>
    Rejected = 2,

    /// <summary>The phone could not write the file.</summary>
    WriteFailed = 3,
}

/// <summary>The OS token in <see cref="TextKind.Device"/>. Strings, for the reason <see cref="PadStatus"/> is. Mirrors protocol/actions.txt.</summary>
internal static class DeviceOs
{
    public const string Windows = "windows";
    public const string Linux = "linux";
    public const string Macos = "macos";
}

/// <summary>The kind token of a <see cref="TextKind.Batteries"/> entry. An unknown token reads as <see cref="Other"/>. Mirrors protocol/actions.txt.</summary>
internal static class BatteryKind
{
    public const string Laptop = "laptop";
    public const string Headphones = "headphones";
    public const string Keyboard = "keyboard";
    public const string Mouse = "mouse";
    public const string Speaker = "speaker";
    public const string Other = "other";

    public static string Of(string token) => token switch
    {
        Laptop or Headphones or Keyboard or Mouse or Speaker => token,
        _ => Other,
    };
}

/// <summary>The kind token of <see cref="TextKind.Output"/>. An unknown token reads as <see cref="Other"/>. Mirrors protocol/actions.txt.</summary>
internal static class OutputKind
{
    public const string Speaker = "speaker";
    public const string Headphones = "headphones";
    public const string Monitor = "monitor";
    public const string Other = "other";

    public static string Of(string token) => token switch
    {
        Speaker or Headphones or Monitor => token,
        _ => Other,
    };
}

/// <summary>
/// The whole vocabulary of <see cref="TextKind.AppAddStatus"/>. Only <see cref="Added"/> changed the macro list. Tokens the
/// phone matches on, never prose, and strings, for the reason <see cref="PadStatus"/> is. Mirrors protocol/actions.txt.
/// </summary>
internal static class AppAddStatus
{
    public const string Added = "added";
    public const string Full = "full";
    public const string Duplicate = "duplicate";
    public const string Stale = "stale";
    public const string Failed = "failed";
}

/// <summary>
/// What a <see cref="BlobBegin"/> carries. A <see cref="BlobBegin"/> holds the raw byte, since an id this build does not
/// know is read to its end and dropped. Mirrors the BLOB_KIND rows of protocol/actions.txt.
/// </summary>
internal enum BlobKind : byte
{
    ClipText = 0,
    ClipImage = 1,
    AppList = 2,
    AppIcon = 3,

    /// <summary>Laptop to phone: a 64 px PNG. The label is the app's name, or "artwork" for a browser's media artwork.</summary>
    MediaIcon = 4,

    /// <summary>Laptop to phone: "app\ntitle\nbody" in UTF-8. The label is the notification's id.</summary>
    Notification = 5,

    /// <summary>
    /// Laptop to phone: an approval request. The label is "id/f", id 12 lowercase hex and f 1 when the text is complete or 0 when it was over the
    /// 4096-byte cap. The body is UTF-8 "agent", "tool" and "text" joined by line feeds. Sent urgently. Answered by <see cref="TextKind.PermAnswer"/>.
    /// </summary>
    PermAsk = 6,

    /// <summary>
    /// Laptop to phone: one git host's account, repositories, pull requests and issues. The label is "git"; the body is tab-separated lines whose
    /// first line names the host and the list revision, see protocol/actions.txt. Answered, for a link, by <see cref="TextKind.GitOpen"/>.
    /// </summary>
    Git = 7,
}

/// <summary>
/// The reason token of <see cref="TextKind.Unavailable"/>. Tokens the phone matches on, never prose, for the reason
/// <see cref="PadStatus"/> is. Mirrors protocol/actions.txt.
/// </summary>
internal static class UnavailableReason
{
    public const string Unsupported = "unsupported";
    public const string Off = "off";
    public const string NoTool = "no-tool";
    public const string NoPermission = "no-permission";
}

/// <summary>The whole vocabulary of <see cref="TextKind.EndAction"/>. Mirrors protocol/actions.txt.</summary>
internal static class EndActionToken
{
    public const string None = "none";
    public const string Sleep = "sleep";
    public const string Shutdown = "shutdown";
}

/// <summary>The whole vocabulary of <see cref="TextKind.PowerState"/>'s first field. Mirrors protocol/actions.txt.</summary>
internal static class PowerStateToken
{
    public const string Restart = "restart";
    public const string ShutDown = "shutdown";
}

/// <summary>The whole vocabulary of <see cref="TextKind.ForegroundKind"/>. Mirrors protocol/actions.txt.</summary>
internal static class AppKindToken
{
    public const string Media = "media";
    public const string Timeline = "timeline";
    public const string Browser = "browser";
}

/// <summary>The whole vocabulary of <see cref="TextKind.CaptureState"/>. Mirrors protocol/actions.txt.</summary>
internal static class CaptureStateToken
{
    public const string Idle = "idle";
    public const string Recording = "recording";
}
