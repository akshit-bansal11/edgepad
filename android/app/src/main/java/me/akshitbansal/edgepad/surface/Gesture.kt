package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.protocol.ActionId

/**
 * A trackpad gesture the user can assign: three or four fingers doing one of these.
 *
 * Two fingers are deliberately absent. Drag to scroll, pinch to zoom and tap to right-click are fixed in
 * [TrackpadRecognizer] and cannot be reassigned, because they are the three a hand already expects from
 * any trackpad; a phone that answers them differently reads as broken rather than as configured.
 */
enum class Gesture(
    val fingers: Int,
    val kind: Kind,
    val nameRes: Int,
) {
    THREE_TAP(3, Kind.TAP, R.string.gesture_three_tap),
    THREE_LEFT(3, Kind.LEFT, R.string.gesture_three_left),
    THREE_RIGHT(3, Kind.RIGHT, R.string.gesture_three_right),
    THREE_UP(3, Kind.UP, R.string.gesture_three_up),
    THREE_DOWN(3, Kind.DOWN, R.string.gesture_three_down),
    FOUR_TAP(4, Kind.TAP, R.string.gesture_four_tap),
    FOUR_LEFT(4, Kind.LEFT, R.string.gesture_four_left),
    FOUR_RIGHT(4, Kind.RIGHT, R.string.gesture_four_right),
    FOUR_UP(4, Kind.UP, R.string.gesture_four_up),
    FOUR_DOWN(4, Kind.DOWN, R.string.gesture_four_down),
    ;

    enum class Kind { TAP, LEFT, RIGHT, UP, DOWN }

    /** Windows' own map, except three fingers sideways switch desktops and four walk the apps. */
    val default: GestureAction
        get() =
            when (this) {
                THREE_TAP -> GestureAction.SEARCH
                THREE_LEFT -> GestureAction.DESKTOP_LEFT
                THREE_RIGHT -> GestureAction.DESKTOP_RIGHT
                THREE_UP -> GestureAction.TASK_VIEW
                THREE_DOWN -> GestureAction.SHOW_DESKTOP
                FOUR_TAP -> GestureAction.NOTIFICATIONS
                FOUR_LEFT -> GestureAction.APP_SWITCHER
                FOUR_RIGHT -> GestureAction.APP_SWITCHER
                FOUR_UP -> GestureAction.TASK_VIEW
                FOUR_DOWN -> GestureAction.SHOW_DESKTOP
            }

    companion object {
        fun of(
            fingers: Int,
            kind: Kind,
        ): Gesture? = entries.firstOrNull { it.fingers == fingers && it.kind == kind }
    }
}

/**
 * What a gesture does. A one-shot action fires once per gesture. A continuous one fires a step for every
 * [TrackpadRecognizer.SWITCH_STEP_DP] of travel: forward along the swipe's own direction is +1, back is -1.
 * On a tap, a continuous action fires one step forward.
 */
enum class GestureAction(
    val nameRes: Int,
    val continuous: Boolean = false,
) {
    NOTHING(R.string.action_nothing),
    LEFT_CLICK(R.string.action_left_click),
    RIGHT_CLICK(R.string.action_right_click),
    MIDDLE_CLICK(R.string.action_middle_click),
    PLAY_PAUSE(R.string.action_play_pause),
    NEXT_TRACK(R.string.action_next_track),
    PREVIOUS_TRACK(R.string.action_previous_track),
    MUTE(R.string.action_mute),
    MIC_MUTE(R.string.action_mic_mute),
    VOLUME(R.string.action_volume, continuous = true),
    BRIGHTNESS(R.string.action_brightness, continuous = true),
    ZOOM(R.string.action_zoom, continuous = true),
    ZOOM_RESET(R.string.action_zoom_reset),
    APP_SWITCHER(R.string.action_app_switcher, continuous = true),
    DESKTOP_LEFT(R.string.action_desktop_left),
    DESKTOP_RIGHT(R.string.action_desktop_right),
    TASK_VIEW(R.string.action_task_view),
    SHOW_DESKTOP(R.string.action_show_desktop),
    SEARCH(R.string.action_search),
    NOTIFICATIONS(R.string.action_notifications),
    LOCK(R.string.action_lock),
    ;

    /** The laptop action for a one-shot, or null for the few that are not one. */
    val actionId: ActionId?
        get() =
            when (this) {
                PLAY_PAUSE -> ActionId.PLAY_PAUSE
                NEXT_TRACK -> ActionId.NEXT_TRACK
                PREVIOUS_TRACK -> ActionId.PREVIOUS_TRACK
                MUTE -> ActionId.MUTE_TOGGLE
                MIC_MUTE -> ActionId.MIC_MUTE_TOGGLE
                ZOOM_RESET -> ActionId.ZOOM_RESET
                DESKTOP_LEFT -> ActionId.DESKTOP_LEFT
                DESKTOP_RIGHT -> ActionId.DESKTOP_RIGHT
                TASK_VIEW -> ActionId.TASK_VIEW
                SHOW_DESKTOP -> ActionId.SHOW_DESKTOP
                SEARCH -> ActionId.SEARCH
                NOTIFICATIONS -> ActionId.NOTIFICATIONS
                LOCK -> ActionId.LOCK
                else -> null
            }
}
