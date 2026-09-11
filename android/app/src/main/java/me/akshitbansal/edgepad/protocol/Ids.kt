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
    ;

    fun set(value: Int): Frame = Frame.SetValue(id, value)

    companion object {
        fun of(id: Int): ControlId? = entries.firstOrNull { it.id == id }
    }
}

/** What a TEXT frame carries. Kinds 0 to 2 go laptop to phone, 3 goes phone to laptop. Mirrors protocol/actions.txt. */
enum class TextKind(
    val id: Int,
) {
    NOW_PLAYING(0),
    APP(1),
    TIMELINE(2),
    TYPE(3),
    ;

    fun frame(text: String): Frame = Frame.Text(id, text)

    companion object {
        fun of(id: Int): TextKind? = entries.firstOrNull { it.id == id }
    }
}
