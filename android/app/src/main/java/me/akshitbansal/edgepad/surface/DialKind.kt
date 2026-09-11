package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame

/** The dials a corner can hold: their names, which corner each takes until Settings changes it, and what it does. */
enum class DialKind(
    /** The short label on the ruler. */
    val shortRes: Int,
    /** The name in Settings. */
    val nameRes: Int,
    /** The corner it takes by default (0 top-left, clockwise), or null for none. */
    val defaultCorner: Int?,
) {
    VOLUME(R.string.dial_volume_short, R.string.dial_volume, 0),
    BRIGHTNESS(R.string.dial_brightness_short, R.string.dial_brightness, 1),
    ZOOM(R.string.dial_zoom_short, R.string.dial_zoom, 2),
    MEDIA(R.string.dial_media_short, R.string.dial_media, 3),
    APP_SWITCHER(R.string.dial_apps_short, R.string.dial_apps, null),
    MIC(R.string.dial_mic_short, R.string.dial_mic, null),
    ;

    /** Builds the dial with this kind's behaviour: what a slide sets, what a tap runs, what a step does. */
    fun dial(
        corner: Int,
        label: String,
        unitsPerDp: Float,
        snap: Boolean,
        send: (Frame) -> Unit,
        haptic: () -> Unit,
    ): Dial {
        val snapTo = if (snap) SNAP_STEP else 0
        return when (this) {
            VOLUME -> {
                Dial(
                    this,
                    corner,
                    label,
                    unitsPerDp,
                    ControlId.VOLUME,
                    tap = ActionId.MUTE_TOGGLE,
                    snapTo = snapTo,
                    sink = send,
                    haptic = haptic,
                )
            }

            BRIGHTNESS -> {
                Dial(
                    this,
                    corner,
                    label,
                    unitsPerDp,
                    ControlId.BRIGHTNESS,
                    snapTo = snapTo,
                    sink = send,
                    haptic = haptic,
                )
            }

            MIC -> {
                Dial(
                    this,
                    corner,
                    label,
                    unitsPerDp,
                    ControlId.MIC_LEVEL,
                    tap = ActionId.MIC_MUTE_TOGGLE,
                    snapTo = snapTo,
                    sink = send,
                    haptic = haptic,
                )
            }

            MEDIA -> {
                Dial(
                    this,
                    corner,
                    label,
                    unitsPerDp,
                    ControlId.MEDIA_POSITION,
                    tap = ActionId.PLAY_PAUSE,
                    sink = send,
                    haptic = haptic,
                )
            }

            ZOOM -> {
                Dial(
                    this,
                    corner,
                    label,
                    unitsPerDp,
                    tap = ActionId.ZOOM_RESET,
                    step = { direction -> send(Frame.Zoom(direction * TrackpadRecognizer.WHEEL_NOTCH)) },
                    keepsSteps = true,
                    sink = send,
                    haptic = haptic,
                )
            }

            APP_SWITCHER -> {
                Dial(
                    this,
                    corner,
                    label,
                    unitsPerDp,
                    tap = ActionId.TASK_VIEW,
                    step = { direction -> send(appSwitchStep(direction).frame()) },
                    onArm = { send(ActionId.APP_SWITCH_BEGIN.frame()) },
                    onRelease = { send(ActionId.APP_SWITCH_END.frame()) },
                    sink = send,
                    haptic = haptic,
                )
            }
        }
    }

    private fun appSwitchStep(direction: Int): ActionId =
        if (direction > 0) ActionId.APP_SWITCH_NEXT else ActionId.APP_SWITCH_PREVIOUS

    private companion object {
        const val SNAP_STEP = 5
    }
}
