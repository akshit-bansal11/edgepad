package me.akshitbansal.edgepad.protocol

/** What a RUN_ACTION frame may ask for. Mirrors protocol/actions.txt; a test holds them equal. */
enum class ActionId(
    val id: Int,
) {
    MUTE_TOGGLE(1),
    PLAY_PAUSE(2),
    NEXT_TRACK(3),
    PREVIOUS_TRACK(4),
    MIC_MUTE_TOGGLE(5),
    LOCK(6),
    TASK_VIEW(20),
    SHOW_DESKTOP(21),
    SEARCH(22),
    NOTIFICATIONS(23),
    DESKTOP_LEFT(24),
    DESKTOP_RIGHT(25),
    APP_SWITCH_BEGIN(26),
    APP_SWITCH_NEXT(27),
    APP_SWITCH_PREVIOUS(28),
    APP_SWITCH_END(29),
    ZOOM_RESET(30),
    VOLUME_UP(31),
    VOLUME_DOWN(32),
    BRIGHTNESS_UP(33),
    BRIGHTNESS_DOWN(34),

    /**
     * Ask the laptop to plug in its virtual controller, and to unplug it again. Actions rather than frame
     * types of their own, because the pad is only ever asked for and given back, and an action costs no
     * protocol version — the laptop answers with [TextKind.PAD_STATUS], and a laptop too old to know these
     * ids drops them and counts them, which the phone reads as the answer never coming.
     */
    PAD_ATTACH(35),
    PAD_DETACH(36),

    /**
     * Annotating the screen and signing, which the laptop draws and the phone only steers: BEGIN shows the
     * laptop's overlay and END removes it, UNDO and CLEAR edit the strokes, and the six tools pick what the
     * next [Frame.Pen] stroke draws. [SIGN_BEGIN] opens a surface that shows nothing on the laptop and accepts
     * only the pen tool, and [SIGN_SAVE] asks for the export, answered with [TextKind.SIGN_STATUS]. A laptop
     * too old to know the ids drops and counts them, and the phone offers them only for one that sent
     * [Feature.ANNOTATE].
     */
    ANNOTATE_BEGIN(40),
    ANNOTATE_END(41),
    ANNOTATE_UNDO(42),
    ANNOTATE_CLEAR(43),
    TOOL_PEN(44),
    TOOL_HIGHLIGHTER(45),
    TOOL_LINE(46),
    TOOL_ARROW(47),
    TOOL_RECTANGLE(48),
    TOOL_ELLIPSE(49),

    /** The laser: the next [Frame.Pen] stroke is a dot that follows the finger and leaves nothing behind. */
    TOOL_LASER(37),

    /** The laser with a short red trail behind the dot. */
    TOOL_LASER_TRAIL(38),

    SIGN_BEGIN(50),
    SIGN_SAVE(51),

    /**
     * Screen capture, which is the laptop's own tool: a region (a drag), the whole screen, and a recording.
     * Actions because the phone only asks; what each one presses is the laptop's business. A laptop too old
     * to know them drops and counts the ids, and the phone shows the buttons only for one that sent
     * [Feature.CAPTURE].
     */
    CAPTURE_REGION(52),
    CAPTURE_SCREEN(53),
    CAPTURE_RECORD(54),

    /**
     * The phone left its capture screen: the laptop dismisses a capture overlay it opened, only if one may still
     * be open. Sent within protocol 5, which is unreleased, so it needed no version.
     */
    CAPTURE_CANCEL(55),

    /**
     * Adding a macro from the phone: APPS_LIST asks the laptop to send the apps it could add, as a [BlobKind.APP_LIST]
     * blob and then a [BlobKind.APP_ICON] blob each, and APPS_CANCEL stops that list and its icons. The phone offers
     * them only for a laptop that sent [Feature.APPS].
     */
    APPS_LIST(56),
    APPS_CANCEL(57),

    /**
     * The macro grid is open and wants to know which macro apps are running: MACROS_WATCH starts the laptop's survey, which
     * reports [TextKind.MACRO_OPEN] on change only, and MACROS_UNWATCH stops it. Forgotten when the session ends.
     */
    MACROS_WATCH(58),
    MACROS_UNWATCH(59),

    /**
     * Arm the laptop to sleep or shut down when the current media ends (END_SLEEP, END_SHUTDOWN), or disarm it (END_CANCEL).
     * The laptop reports the armed state as [TextKind.END_ACTION]. Offered only for a laptop that sent [Feature.END_ACTION].
     */
    END_SLEEP(60),
    END_SHUTDOWN(61),
    END_CANCEL(62),

    /**
     * The first of 32 macro slots, 64..95; slot n is `MACRO_BASE + n`. The phone sends the index and never
     * what it launches — the laptop's own list decides that, which is the whole security model here.
     * A laptop too old to know the block drops the id and counts it, so this needed no protocol version.
     */
    MACRO_BASE(64),

    /**
     * A reserved block of 32, 96..127: close the app macro slot n opens, as its own close button would, never a kill.
     * The phone sends the slot and nothing else; the laptop decides what that slot is.
     */
    MACRO_CLOSE_BASE(96),
    ;

    fun frame(): Frame = Frame.RunAction(id)
}

/** What a SET_VALUE frame may set and a STATE frame reports. Mirrors protocol/actions.txt. */
enum class ControlId(
    val id: Int,
) {
    VOLUME(0),
    BRIGHTNESS(1),
    MIC_LEVEL(2),
    MEDIA_POSITION(3),

    /**
     * The value is an index into [TextKind.REFRESH_RATES], never a rate in hertz. SET carries value u8 and the
     * laptop drops anything above 100, so 120 or 144 could not cross the wire at all; an index is always well
     * under 100. It also makes a rate the laptop does not have unrepresentable rather than merely rejected.
     */
    REFRESH_RATE(4),

    /**
     * The value is an index into [PenColor], the eight stock colours; the laptop drops an index of 8 or more. A colour
     * outside the palette is [TextKind.PEN_RGB].
     */
    PEN_COLOR(5),

    /**
     * Where a capture ends up, as a [CaptureTarget] index: the laptop keeps it, or sends it here and deletes its copy
     * once this phone says it saved it. Sent on connect and whenever the setting changes.
     */
    CAPTURE_TARGET(6),

    /**
     * Whether this link shares the clipboard, 1 or 0, phone to laptop. The laptop forgets it when the session ends and
     * drops any other value.
     */
    CLIPBOARD(7),

    /**
     * The width of the strokes that start after this, 1 to 100, as 0.02% of the laptop screen's height per step: 5 is
     * 0.1%, 20 is the default 0.4% and 100 is 2.0%. The laptop drops 0 and anything above 100, and forgets the value
     * when the session ends.
     */
    PEN_WIDTH(8),

    /** The opacity of the strokes that start after this, 10 to 100 percent. The laptop drops anything else. */
    PEN_OPACITY(9),

    /** Whether this link forwards the laptop's notifications, 1 or 0. The laptop forgets it when the session ends. */
    NOTIFY(10),

    /** Whether this link hears the app in front ([TextKind.FOREGROUND_APP]), 1 or 0. Forgotten at session end. */
    FOREGROUND_WATCH(11),

    /**
     * A bitmask of what this phone draws right now: bit 0 (1) system-number widgets, bit 1 (2) the System screen, bit 2 (4) the weather
     * widget, bit 3 (8) an AI widget or screen, bit 4 (16) the GitHub screen, bit 5 (32) the Network page. 0 to 63; the laptop drops more and forgets it at session end.
     */
    DASH_WATCH(12),

    /** Whether developer mode is on, 1 or 0, phone to laptop. The laptop keeps it; the phone shows it from FEATURES. */
    DEVELOPER(13),

    /** Phone to laptop: 1 starts one speed test, 0 cancels it. The laptop answers with [TextKind.SPEED_RESULT]. */
    SPEED_TEST(14),
    ;

    fun set(value: Int): Frame = Frame.SetValue(id, value)

    companion object {
        fun of(id: Int): ControlId? = entries.firstOrNull { it.id == id }
    }
}

/** What a TEXT frame carries. Only kinds 3, 7, 14 and 17 go phone to laptop. Mirrors protocol/actions.txt. */
enum class TextKind(
    val id: Int,
) {
    NOW_PLAYING(0),
    APP(1),
    TIMELINE(2),
    TYPE(3),

    /** "60/120/144": the display's available rates, in the order [ControlId.REFRESH_RATE] indexes them. */
    REFRESH_RATES(4),

    /** "Chrome/Spotify/Notes": the laptop's macro names, in the order [ActionId.MACRO_BASE] indexes them. */
    MACROS(5),

    /**
     * One piece of one macro's icon: "slot/chunk/chunks/base64". An icon does not fit the 255 bytes a TEXT
     * payload holds, so it arrives in pieces and the phone joins them. A kind rather than a frame type of its
     * own, because an unknown kind is dropped and counted while an unknown type closes the connection — the
     * difference between a feature an older laptop ignores and one it cannot survive.
     */
    MACRO_ICON(6),

    /**
     * Phone to laptop: send the macro icons. Empty payload. Icons are answered rather than pushed, so they
     * cross the link when this screen opens and never while a finger is on the trackpad.
     */
    WANT_ICONS(7),

    /**
     * Laptop to phone: whether a virtual controller can be offered at all, as one [PadStatus] token. Sent in
     * answer to [ActionId.PAD_ATTACH] and [ActionId.PAD_DETACH], and once after the handshake, so the phone
     * can hide the pad instead of showing a button that quietly does nothing.
     */
    PAD_STATUS(8),

    /**
     * Laptop to phone: the optional features this laptop has, as [Feature] tokens joined by "/", sent once
     * after the handshake. A feature missing from the list is off, and a token this build does not know is
     * ignored, so a newer laptop can name more than an older phone understands.
     */
    FEATURES(9),

    /**
     * Laptop to phone: the primary display in pixels, "1920/1080", sent after the handshake and whenever the
     * display changes, so the phone can shape its drawing surface to the same ratio.
     */
    SCREEN_SIZE(10),

    /** Laptop to phone: how a [ActionId.SIGN_SAVE] ended, as one [SignStatus] token. */
    SIGN_STATUS(11),

    /** Laptop to phone: why a capture this phone asked for did not reach it, as one [CaptureStatus] token. */
    CAPTURE_STATUS(12),

    /**
     * Laptop to phone, once after the handshake: "id/os/name", the id 32 hex digits, the os a [DeviceOs] token and the
     * laptop's own name. The id lets the phone tell one laptop seen over two transports from two.
     */
    DEVICE(13),

    /** Phone to laptop: "epoch.index", an index into the app list the laptop sent for that epoch. No path is named. */
    APP_ADD(14),

    /** Laptop to phone: how an [APP_ADD] ended, as one [AppAddStatus] token. */
    APP_ADD_STATUS(15),

    /**
     * Laptop to phone: one character per macro slot, in [MACROS] order. '-' not trackable, '0' not running, '1' running,
     * '2' running and in front. Sent when it changes while watched; an unknown character reads as untracked.
     */
    MACRO_OPEN(16),

    /**
     * Phone to laptop: a stroke colour as exactly six hex digits, "RRGGBB". Safe to send because it is only a colour:
     * the laptop takes those six characters and nothing else, and what it makes of them is a number it can only draw with.
     */
    PEN_RGB(17),

    /**
     * Laptop to phone: exactly "blocked" or "ok", sent after the handshake and on every change. "blocked" means the
     * laptop's OS discards the input it is given (Mac without Accessibility, Linux without /dev/uinput). Windows never
     * sends it, and a phone that never heard it reads "ok".
     */
    INPUT_STATE(18),

    /**
     * Laptop to phone: a snapshot of the whole battery list, "kind:level:charging:name" entries joined by "|", the laptop
     * first. Empty means no devices. Never a delta; see docs/PROTOCOL.md for the size rule and cadence.
     */
    BATTERIES(19),

    /** Laptop to phone: the audio output, "kind/name" split on the first "/", kind an [OutputKind] token. Empty means none known. */
    OUTPUT(20),

    /**
     * Laptop to phone: the app in front, "token/name" split on the first "/". Empty means nothing known. Sent only
     * after [ControlId.FOREGROUND_WATCH] is 1, and never logged.
     */
    FOREGROUND_APP(21),

    /** Laptop to phone: how many macro slots the laptop keeps, in decimal such as "15". */
    MACRO_CAP(22),

    /** Laptop to phone: a [CaptureStateToken], "idle" or "recording". Best effort. */
    CAPTURE_STATE(23),

    /** Laptop to phone: "feature:reason" entries joined by "/", reason an [UnavailableReason] token. Empty means none. */
    UNAVAILABLE(24),

    /** Laptop to phone: what END_SLEEP armed, as one [EndActionToken]. Sent on change. */
    END_ACTION(25),

    /** Laptop to phone: the title line before it is cleaned for [NOW_PLAYING], sent only when it differs from that text. */
    NOW_PLAYING_FULL(26),

    /** Laptop to phone: "cpu/ram/gpu/vram/down/up/ping/loss/ctemp/gtemp/gpower", -1 unknown. Once a second while [ControlId.DASH_WATCH] has bit 0 or 1. */
    STATS(27),

    /** Laptop to phone: "cpuName|ramMiB|gpuName|vramMiB|netName". Sent when a watch starts and when it changes. */
    STATS_INFO(28),

    /** Laptop to phone: "label:usedPct:totalGiB:freeGiB" entries joined by "|", at most 6. */
    DISKS(29),

    /** Laptop to phone: "agent:state:title" entries joined by "|", newest first, at most 8. Empty means none. */
    SESSIONS(30),

    /** Phone to laptop: exactly "id/a" or "id/d", the id 12 lowercase hex digits. */
    PERM_ANSWER(31),

    /** Laptop to phone: "id" of an approval that was answered elsewhere, timed out or died. */
    PERM_GONE(32),

    /** Laptop to phone: "agent:window:pct:resetAtMin:measuredAtMin" entries joined by "|"; the window is minutes, [LimitWindow.CONTEXT] or "~name". */
    LIMITS(33),

    /** Laptop to phone: "agent/measuredAtMin/tokToday/centsToday/tokWeek/centsWeek/tokMonth/centsMonth" entries joined by "|". */
    SPEND(34),

    /** Laptop to phone: "tempTenthsC/wmoCode/city". Empty means none. */
    WEATHER(35),

    /** Laptop to phone: "kind|band|linkMbps|signalPct|localIp|gateway"; kind wifi, ethernet, cellular, other or none, band 2.4, 5, 6 or ?, -1 unknown. No network name. */
    NET_INFO(36),

    /** Laptop to phone: "state|downKbps|upKbps|latencyMs|jitterMs|atUnixMin|bytes"; state running, done, failed, cancelled or capped, -1 unknown. */
    SPEED_RESULT(37),
    ;

    fun frame(text: String): Frame = Frame.Text(id, text)

    companion object {
        fun of(id: Int): TextKind? = entries.firstOrNull { it.id == id }
    }
}

/**
 * The bits of a [Frame.PadState]'s `buttons` field. These are XInput's own `wButtons` values, unchanged,
 * because the laptop copies the field straight into an `XINPUT_GAMEPAD`: a mapping of our own would be a
 * conversion on the hot path and a second table to keep in step with Microsoft's. Mirrors protocol/actions.txt.
 *
 * 0x0800, between [GUIDE] and [A], is unused by XInput and stays unused here. A bit this build does not know
 * is dropped and counted like any other unknown id, never an error.
 */
enum class PadButton(
    val mask: Int,
) {
    /** Nothing held. A real state of the field, not padding: it is what arrives the moment a button comes up. */
    NONE(0x0000),

    DPAD_UP(0x0001),
    DPAD_DOWN(0x0002),
    DPAD_LEFT(0x0004),
    DPAD_RIGHT(0x0008),
    START(0x0010),
    BACK(0x0020),
    LEFT_THUMB(0x0040),
    RIGHT_THUMB(0x0080),
    LEFT_SHOULDER(0x0100),
    RIGHT_SHOULDER(0x0200),
    GUIDE(0x0400),
    A(0x1000),
    B(0x2000),
    X(0x4000),
    Y(0x8000),
}

/**
 * The whole vocabulary of [TextKind.PAD_STATUS]. A token the phone matches on, never prose: [READY] is the
 * only one that leaves the pad usable, and the other two are the two different things the phone has to say
 * when it falls back to the keyboard. Matching on the laptop's wording instead would make that wording
 * unchangeable. Mirrors protocol/actions.txt.
 */
enum class PadStatus(
    val token: String,
) {
    /** The laptop can plug in a virtual controller. */
    READY("ready"),

    /** The virtual-controller driver is not installed on the laptop, so there is nothing to plug in. */
    NO_DRIVER("no-driver"),

    /** The driver is there but plugging the pad in failed. */
    ATTACH_FAILED("attach-failed"),
    ;

    companion object {
        /** An unknown token reads as [NO_DRIVER]: the pad is unusable and the phone cannot say why. */
        fun of(token: String): PadStatus = entries.firstOrNull { it.token == token } ?: NO_DRIVER
    }
}

/**
 * The whole vocabulary of [TextKind.FEATURES]. A token the phone matches on, never prose, for the reason
 * [PadStatus] is. Mirrors protocol/actions.txt.
 */
enum class Feature(
    val token: String,
) {
    /** The laptop can draw on its screen from the phone. */
    ANNOTATE("annotate"),

    /** The laptop answers the capture actions. */
    CAPTURE("capture"),

    /** The laptop's clipboard toggle is on, so [ControlId.CLIPBOARD] is honoured. */
    CLIPBOARD("clipboard"),

    /** The laptop can list its apps for the phone to add as macros. */
    APPS("apps"),

    /** The laptop can tell which macro apps are running, in [TextKind.MACRO_OPEN]. */
    RUNNING("running"),

    /** The laptop answers [ActionId.END_SLEEP], [ActionId.END_SHUTDOWN] and [ActionId.END_CANCEL]. */
    END_ACTION("endaction"),

    /** The laptop can forward its notifications once [ControlId.NOTIFY] is 1. */
    NOTIFY("notify"),

    /** The laptop can say which app is in front, in [TextKind.FOREGROUND_APP], once watched. */
    FOREGROUND("foreground"),

    /** The laptop sends [TextKind.STATS], [TextKind.STATS_INFO] and [TextKind.DISKS]. */
    STATS("stats"),

    /** The laptop sends [TextKind.WEATHER]. */
    WEATHER("weather"),

    /** The laptop's Claude Code integration is on and usable. */
    CLAUDE("claude"),

    /** The laptop's Codex integration is on and usable. */
    CODEX("codex"),

    /** The laptop is signed in to GitHub and sends [BlobKind.GITHUB]. */
    GITHUB("github"),

    /** Developer mode is on on the laptop: it names claude, codex and github only while this is here. */
    DEVELOPER("developer"),

    /** The laptop sends [TextKind.NET_INFO] and answers [ControlId.SPEED_TEST]. */
    NETINFO("netinfo"),

    /** The laptop's Gemini CLI integration is on and usable. */
    GEMINI("gemini"),

    /** The laptop's OpenCode integration is on and usable. */
    OPENCODE("opencode"),

    /** The laptop is signed in to GitLab. */
    GITLAB("gitlab"),

    /** The laptop has a Gitea, Forgejo or Codeberg token. */
    GITEA("gitea"),

    /** The laptop has a Bitbucket token. */
    BITBUCKET("bitbucket"),

    /** The laptop has a Gitee token. */
    GITEE("gitee"),
    ;

    companion object {
        /** Null for a token this build does not know: a feature it cannot offer is simply not there. */
        fun of(token: String): Feature? = entries.firstOrNull { it.token == token }
    }
}

/** The agent of a sessions, limits, spend or approval entry. Mirrors the AGENT rows of protocol/actions.txt. */
enum class AgentToken(
    val token: String,
) {
    CLAUDE("claude"),
    CODEX("codex"),
    GEMINI("gemini"),
    OPENCODE("opencode"),
    ;

    companion object {
        /** Null for a token this build does not know: the entry is dropped. */
        fun of(token: String): AgentToken? = entries.firstOrNull { it.token == token }
    }
}

/** The state in a [TextKind.SESSIONS] entry. Mirrors the SESSION_STATE rows of protocol/actions.txt. */
enum class SessionStateToken(
    val token: String,
) {
    BUSY("busy"),
    WAIT("wait"),
    IDLE("idle"),
    ;

    companion object {
        /** Null for a token this build does not know: the entry is dropped. */
        fun of(token: String): SessionStateToken? = entries.firstOrNull { it.token == token }
    }
}

/** The CI field of a [BlobKind.GITHUB] line. Mirrors the CI rows of protocol/actions.txt. */
enum class CiToken(
    val token: String,
) {
    OK("ok"),
    FAIL("fail"),
    PENDING("pending"),
    NONE("none"),
    ;

    companion object {
        /** An unknown token reads as [NONE]. */
        fun of(token: String): CiToken = entries.firstOrNull { it.token == token } ?: NONE
    }
}

/** The non-minute forms of a [TextKind.LIMITS] window. Mirrors the LIMIT_WINDOW rows of protocol/actions.txt. */
object LimitWindow {
    /** The context window of a session, not a time window. */
    const val CONTEXT = "ctx"

    /** A limit with no duration is this character and a name, such as "~spend_limit". */
    const val NAMED_PREFIX = "~"
}

/**
 * The whole vocabulary of [TextKind.SIGN_STATUS]. A token the phone matches on, never prose, for the reason
 * [PadStatus] is. Mirrors protocol/actions.txt.
 */
enum class SignStatus(
    val token: String,
) {
    /** The signature is on the clipboard and in the laptop's Pictures folder. */
    SAVED("saved"),

    /** Nothing was drawn, so there was nothing to export. */
    EMPTY("empty"),

    /** The export or the save failed. */
    FAILED("failed"),
    ;

    companion object {
        /** An unknown token reads as [FAILED]: a save the phone cannot confirm must not look like a success. */
        fun of(token: String): SignStatus = entries.firstOrNull { it.token == token } ?: FAILED
    }
}

/**
 * The colours [ControlId.PEN_COLOR] indexes, by [index]. [rgb] is only for the phone's swatches and its
 * preview: the laptop draws in its own copy of the same list, and a test holds both to the PALETTE rows of
 * protocol/actions.txt. Mirrors that file.
 */
enum class PenColor(
    val index: Int,
    val rgb: Int,
) {
    BLACK(0, 0x000000),
    WHITE(1, 0xFFFFFF),
    RED(2, 0xFF3B30),
    ORANGE(3, 0xFF9500),
    YELLOW(4, 0xFFCC00),
    GREEN(5, 0x34C759),
    BLUE(6, 0x0068D6),
    PURPLE(7, 0xAF52DE),
}

/** The values of [ControlId.CAPTURE_TARGET], by [value]. */
enum class CaptureTarget(
    val value: Int,
) {
    /** The laptop's own tool saves where it saves, and nothing crosses the link. */
    LAPTOP(0),

    /** The laptop sends each capture here and deletes its copy only once this phone says it saved it. */
    PHONE(1),
    ;

    fun frame(): Frame = ControlId.CAPTURE_TARGET.set(value)
}

/**
 * The whole vocabulary of [TextKind.CAPTURE_STATUS]: why a capture stayed on the laptop. A token the phone matches
 * on, never prose, for the reason [PadStatus] is. Mirrors protocol/actions.txt.
 */
enum class CaptureStatus(
    val token: String,
) {
    /** The file is over [FileType.limit] for its kind, so it stays on the laptop. */
    TOO_LARGE("too-large"),

    /** No new file turned up where the laptop's tool saves, so nothing was sent and nothing deleted. */
    NOT_FOUND("not-found"),

    /** The laptop found the file and could not read or send it. */
    FAILED("failed"),
    ;

    companion object {
        /** An unknown token reads as [FAILED]: the phone cannot say why, only that nothing arrived. */
        fun of(token: String): CaptureStatus = entries.firstOrNull { it.token == token } ?: FAILED
    }
}

/**
 * What a [Frame.FileBegin] may carry, by [id]. The laptop sends an id and never a MIME string, so the phone writes
 * only these types, and an id it does not know is refused. Mirrors the FILE_TYPE and FILE_LIMIT rows of
 * protocol/actions.txt.
 */
enum class FileType(
    val id: Int,
    val mime: String,
    val extension: String,
) {
    PNG(0, "image/png", "png"),
    JPEG(1, "image/jpeg", "jpg"),
    MP4(2, "video/mp4", "mp4"),
    MOV(3, "video/quicktime", "mov"),
    WEBM(4, "video/webm", "webm"),
    ;

    val video: Boolean get() = mime.startsWith("video/")

    /** The largest file of this kind that may cross the link, in bytes. */
    val limit: Long get() = if (video) VIDEO_LIMIT else IMAGE_LIMIT

    companion object {
        const val IMAGE_LIMIT = 50L * 1024 * 1024
        const val VIDEO_LIMIT = 200L * 1024 * 1024

        fun of(id: Int): FileType? = entries.firstOrNull { it.id == id }
    }
}

/**
 * What a [Frame.FileResult] says, by [id]. Only [SAVED] lets the laptop delete its copy. Mirrors
 * protocol/actions.txt.
 */
enum class FileStatus(
    val id: Int,
) {
    /** The file is in this phone's gallery and its SHA-256 matched. */
    SAVED(0),

    /** Every byte arrived and the SHA-256 did not match, so the file was thrown away. */
    BAD_HASH(1),

    /** The file was refused before or while it arrived: a type, size, name or chunk this phone will not take. */
    REJECTED(2),

    /** This phone could not write the file. */
    WRITE_FAILED(3),
    ;

    fun frame(): Frame = Frame.FileResult(id)
}

/**
 * The OS token in [TextKind.DEVICE]. A token the phone matches on, never prose. Mirrors protocol/actions.txt.
 */
enum class DeviceOs(
    val token: String,
) {
    WINDOWS("windows"),
    LINUX("linux"),
    MACOS("macos"),
    ;

    companion object {
        /** Null for a token this build does not know: a hint that depends on the OS is simply not shown. */
        fun of(token: String): DeviceOs? = entries.firstOrNull { it.token == token }
    }
}

/** The kind token of a [TextKind.BATTERIES] entry. Mirrors protocol/actions.txt. */
enum class BatteryKind(
    val token: String,
) {
    LAPTOP("laptop"),
    HEADPHONES("headphones"),
    KEYBOARD("keyboard"),
    MOUSE("mouse"),
    SPEAKER("speaker"),
    OTHER("other"),
    ;

    companion object {
        /** A token this build does not know reads as [OTHER]. */
        fun of(token: String): BatteryKind = entries.firstOrNull { it.token == token } ?: OTHER
    }
}

/** The kind token of [TextKind.OUTPUT]. Mirrors protocol/actions.txt. */
enum class OutputKind(
    val token: String,
) {
    SPEAKER("speaker"),
    HEADPHONES("headphones"),
    MONITOR("monitor"),
    OTHER("other"),
    ;

    companion object {
        /** A token this build does not know reads as [OTHER]. */
        fun of(token: String): OutputKind = entries.firstOrNull { it.token == token } ?: OTHER
    }
}

/**
 * The whole vocabulary of [TextKind.APP_ADD_STATUS]. Only [ADDED] changed the laptop's macro list. A token the phone
 * matches on, never prose, for the reason [PadStatus] is. Mirrors protocol/actions.txt.
 */
enum class AppAddStatus(
    val token: String,
) {
    ADDED("added"),
    FULL("full"),
    DUPLICATE("duplicate"),
    STALE("stale"),
    FAILED("failed"),
    ;

    companion object {
        /** An unknown token reads as [FAILED]: an add the phone cannot confirm must not look like a success. */
        fun of(token: String): AppAddStatus = entries.firstOrNull { it.token == token } ?: FAILED
    }
}

/**
 * What a [Frame.BlobBegin] carries, by [id]. The frame holds the raw id, since one this build does not know is read to
 * its end and dropped. Mirrors the BLOB_KIND rows of protocol/actions.txt.
 */
enum class BlobKind(
    val id: Int,
) {
    CLIP_TEXT(0),
    CLIP_IMAGE(1),
    APP_LIST(2),
    APP_ICON(3),
    MEDIA_ICON(4),
    NOTIFICATION(5),

    /** Laptop to phone: an approval request. The label is "id/f"; the body is UTF-8 agent, tool and text joined by line feeds. */
    PERM_ASK(6),

    /** Laptop to phone: GitHub pull requests and the notification count. The label is "gh"; the body is tab-separated lines. */
    GITHUB(7),
    ;

    companion object {
        fun of(id: Int): BlobKind? = entries.firstOrNull { it.id == id }
    }
}

/**
 * The reason token of [TextKind.UNAVAILABLE]. A token the phone matches on, never prose, for the reason [PadStatus] is.
 * Mirrors protocol/actions.txt.
 */
enum class UnavailableReason(
    val token: String,
) {
    UNSUPPORTED("unsupported"),
    OFF("off"),
    NO_TOOL("no-tool"),
    NO_PERMISSION("no-permission"),
    ;

    companion object {
        /** An unknown token reads as [UNSUPPORTED]: the feature is off either way. */
        fun of(token: String): UnavailableReason = entries.firstOrNull { it.token == token } ?: UNSUPPORTED
    }
}

/** The whole vocabulary of [TextKind.END_ACTION]. Mirrors protocol/actions.txt. */
enum class EndActionToken(
    val token: String,
) {
    NONE("none"),
    SLEEP("sleep"),
    SHUTDOWN("shutdown"),
    ;

    companion object {
        /** An unknown token reads as [NONE]: nothing is shown as armed that the phone cannot name. */
        fun of(token: String): EndActionToken = entries.firstOrNull { it.token == token } ?: NONE
    }
}

/** The whole vocabulary of [TextKind.CAPTURE_STATE]. Mirrors protocol/actions.txt. */
enum class CaptureStateToken(
    val token: String,
) {
    IDLE("idle"),
    RECORDING("recording"),
    ;

    companion object {
        /** An unknown token reads as [IDLE]. */
        fun of(token: String): CaptureStateToken = entries.firstOrNull { it.token == token } ?: IDLE
    }
}
