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
    ;

    fun frame(text: String): Frame = Frame.Text(id, text)

    companion object {
        fun of(id: Int): TextKind? = entries.firstOrNull { it.id == id }
    }
}
