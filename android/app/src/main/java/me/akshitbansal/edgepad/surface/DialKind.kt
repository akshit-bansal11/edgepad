package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame

/** The dials a user can place on the edges, each with where it sits until Settings moves it. */
enum class DialKind(
    val labelRes: Int,
    val defaultPlacement: Placement,
) {
    VOLUME(R.string.dial_volume, Placement(Edge.BOTTOM, 1f)),
    BRIGHTNESS(R.string.dial_brightness, Placement(Edge.BOTTOM, 0f)),
    MEDIA(R.string.dial_media, Placement(Edge.TOP, 1f)),
    APP_SWITCHER(R.string.dial_apps, Placement(Edge.TOP, 0f)),
    ZOOM(R.string.dial_zoom, Placement(Edge.OFF, 0.5f)),
    MIC(R.string.dial_mic, Placement(Edge.OFF, 0.5f)),
    ;

    /** Builds the dial with this kind's behaviour: what a drag sets, what a tap runs, what a step does. */
    fun dial(
        placement: Placement,
        label: String,
        send: (Frame) -> Unit,
        haptic: () -> Unit,
    ): Dial =
        when (this) {
            VOLUME -> {
                Dial(this, placement, label, ControlId.VOLUME, tap = ActionId.MUTE_TOGGLE, sink = send, haptic = haptic)
            }

            BRIGHTNESS -> {
                Dial(this, placement, label, ControlId.BRIGHTNESS, sink = send, haptic = haptic)
            }

            MEDIA -> {
                Dial(
                    this,
                    placement,
                    label,
                    ControlId.MEDIA_POSITION,
                    tap = ActionId.PLAY_PAUSE,
                    sink = send,
                    haptic = haptic,
                )
            }

            MIC -> {
                Dial(
                    this,
                    placement,
                    label,
                    ControlId.MIC_LEVEL,
                    tap = ActionId.MIC_MUTE_TOGGLE,
                    sink = send,
                    haptic = haptic,
                )
            }

            ZOOM -> {
                Dial(
                    this,
                    placement,
                    label,
                    tap = ActionId.ZOOM_RESET,
                    step = { direction -> send(Frame.Zoom(direction * TrackpadRecognizer.WHEEL_NOTCH)) },
                    sink = send,
                    haptic = haptic,
                )
            }

            APP_SWITCHER -> {
                Dial(
                    this,
                    placement,
                    label,
                    tap = ActionId.TASK_VIEW,
                    step = { direction -> send(appSwitchStep(direction).frame()) },
                    onArm = { send(ActionId.APP_SWITCH_BEGIN.frame()) },
                    onRelease = { send(ActionId.APP_SWITCH_END.frame()) },
                    sink = send,
                    haptic = haptic,
                )
            }
        }

    private fun appSwitchStep(direction: Int): ActionId =
        if (direction > 0) ActionId.APP_SWITCH_NEXT else ActionId.APP_SWITCH_PREVIOUS
}
