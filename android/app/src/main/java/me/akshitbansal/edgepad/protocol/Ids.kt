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
