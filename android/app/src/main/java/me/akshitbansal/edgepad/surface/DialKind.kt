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

    // Off by default: there are more kinds than the four corners hold, and this one is the least often wanted.
    REFRESH(R.string.dial_refresh_short, R.string.dial_refresh, null),

    // Off by default too: the four corners keep the dials most wanted; these are one slide away in Settings.
    SCROLL(R.string.dial_scroll_short, R.string.dial_scroll, null),
    SCROLL_H(R.string.dial_scroll_h_short, R.string.dial_scroll_h, null),
    DESKTOPS(R.string.dial_desktops_short, R.string.dial_desktops, null),
    TABS(R.string.dial_tabs_short, R.string.dial_tabs, null),
    ;

    /** Builds the dial with this kind's behaviour: what a slide sets, what a tap runs, what a step does. */
    fun dial(
        corner: Int,
        label: String,
        unitsPerDp: Float,
        snap: Boolean,
        send: (Frame) -> Unit,
        haptic: () -> Unit,
        slot: Int = Dial.WHOLE,
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
                    slot = slot,
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
                    slot = slot,
                )
            }

            REFRESH -> {
                Dial(
                    this,
                    corner,
                    label,
                    // A level of 0-100 is 400 dp of slide at ×1; a handful of rate indices at that rate would
                    // be a whole range narrower than the touch slop. One index costs one stepper notch of
                    // travel instead, which is the only step-sized slide a finger here already knows.
                    unitsPerDp / Dial.UNITS_PER_STEP,
                    ControlId.REFRESH_RATE,
                    // No snap: an index is already whole, and rounding one to a multiple of five would land
                    // off the list entirely. No tap: there is no sensible thing for it to toggle.
                    sink = send,
                    haptic = haptic,
                    slot = slot,
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
                    slot = slot,
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
                    slot = slot,
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
                    slot = slot,
                )
            }

            SCROLL -> {
                Dial(
                    this,
                    corner,
                    label,
                    unitsPerDp,
                    // Wheel forward is positive, so a clockwise turn, which scrolls down, is negative.
                    step = { direction -> send(Frame.Scroll(0, -direction * TrackpadRecognizer.WHEEL_NOTCH)) },
                    sink = send,
                    haptic = haptic,
                    slot = slot,
                )
            }

            SCROLL_H -> {
                Dial(
                    this,
                    corner,
                    label,
                    unitsPerDp,
                    // A positive dx is right, so a clockwise turn scrolls right.
                    step = { direction -> send(Frame.Scroll(direction * TrackpadRecognizer.WHEEL_NOTCH, 0)) },
                    sink = send,
                    haptic = haptic,
                    slot = slot,
                )
            }

            DESKTOPS -> {
                Dial(
                    this,
                    corner,
                    label,
                    unitsPerDp,
                    tap = ActionId.TASK_VIEW,
                    step = { direction -> send(desktopStep(direction).frame()) },
                    sink = send,
                    haptic = haptic,
                    slot = slot,
                )
            }

            TABS -> {
                Dial(
                    this,
                    corner,
                    label,
                    unitsPerDp,
                    step = { direction -> tabStep(direction, send) },
                    // Ctrl is held for the whole slide, so each Tab is Ctrl+Tab; let go when it ends or is cancelled.
                    onArm = { send(Frame.Key(VK_CTRL, true)) },
                    onRelease = { send(Frame.Key(VK_CTRL, false)) },
                    sink = send,
                    haptic = haptic,
                    slot = slot,
                )
            }
        }
    }

    private fun desktopStep(direction: Int): ActionId =
        if (direction > 0) ActionId.DESKTOP_RIGHT else ActionId.DESKTOP_LEFT

    /** Ctrl+Tab forward, Ctrl+Shift+Tab back: the Ctrl is the slide's, so only Tab and, going back, Shift go here. */
    private fun tabStep(
        direction: Int,
        send: (Frame) -> Unit,
    ) {
        if (direction < 0) send(Frame.Key(VK_SHIFT, true))
        send(Frame.Key(VK_TAB, true))
        send(Frame.Key(VK_TAB, false))
        if (direction < 0) send(Frame.Key(VK_SHIFT, false))
    }

    private fun appSwitchStep(direction: Int): ActionId =
        if (direction > 0) ActionId.APP_SWITCH_NEXT else ActionId.APP_SWITCH_PREVIOUS

    private companion object {
        const val SNAP_STEP = 5

        // Windows virtual-key codes, as the on-screen keyboard sends them.
        const val VK_TAB = 0x09
        const val VK_SHIFT = 0x10
        const val VK_CTRL = 0x11
    }
}

/** What Settings stores for a corner or slot left empty. */
internal const val NO_DIAL = "-"

/**
 * The dial a stored [name] stands for: [whenNeverSet] when nothing was stored, null for [NO_DIAL], and null for a
 * name no kind has any more (a removed dial), so a corner never falls back to a default that may already be taken.
 */
internal fun resolveStoredDial(
    name: String?,
    whenNeverSet: () -> DialKind?,
): DialKind? =
    when (name) {
        null -> whenNeverSet()
        NO_DIAL -> null
        else -> DialKind.entries.firstOrNull { it.name == name }
    }
