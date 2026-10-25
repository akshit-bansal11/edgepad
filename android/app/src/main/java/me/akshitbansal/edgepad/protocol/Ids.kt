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
     * The first of 32 macro slots, 64..95; slot n is `MACRO_BASE + n`. The phone sends the index and never
     * what it launches — the laptop's own list decides that, which is the whole security model here.
     * A laptop too old to know the block drops the id and counts it, so this needed no protocol version.
     */
    MACRO_BASE(64),
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
     * The value is an index into [PenColor], never an RGB: the phone cannot name a colour the laptop did not
     * agree to, and the laptop drops an index of 8 or more.
     */
    PEN_COLOR(5),

    /**
     * Where a capture ends up, as a [CaptureTarget] index: the laptop keeps it, or sends it here and deletes its copy
     * once this phone says it saved it. Sent on connect and whenever the setting changes.
     */
    CAPTURE_TARGET(6),
    ;

    fun set(value: Int): Frame = Frame.SetValue(id, value)

    companion object {
        fun of(id: Int): ControlId? = entries.firstOrNull { it.id == id }
    }
}

/** What a TEXT frame carries. Only kind 3 goes phone to laptop. Mirrors protocol/actions.txt. */
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
    ;

    companion object {
        /** Null for a token this build does not know: a feature it cannot offer is simply not there. */
        fun of(token: String): Feature? = entries.firstOrNull { it.token == token }
    }
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
