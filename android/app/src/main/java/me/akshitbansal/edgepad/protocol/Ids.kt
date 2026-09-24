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
