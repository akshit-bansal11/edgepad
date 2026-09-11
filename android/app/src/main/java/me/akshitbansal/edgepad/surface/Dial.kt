package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import kotlin.math.abs
import kotlin.math.floor
import kotlin.math.roundToInt

/**
 * One ruler on the screen's edge. The finger slides the ruler along the edge under a fixed indicator:
 * clockwise raises the value, like turning a knob. A tap without a slide runs [tap]. A [control] dial holds
 * a 0-100 level and sends SET frames; a dial with none is a stepper that calls [step] with +1 or -1 once
 * per step of travel, with [onArm] and [onRelease] bracketing the slide (the app switcher holds Alt that way).
 *
 * Distances are in dp, and [unitsPerDp] is the sensitivity: how many value units one dp of slide is worth.
 * Pure state, tested on the JVM; the View measures the finger along the edge and draws the ruler.
 */
class Dial(
    val kind: DialKind,
    val placement: Placement,
    val label: String,
    private val unitsPerDp: Float,
    val control: ControlId? = null,
    private val tap: ActionId? = null,
    private val step: ((Int) -> Unit)? = null,
    private val onArm: (() -> Unit)? = null,
    private val onRelease: (() -> Unit)? = null,
    /** A stepper that keeps its count across slides until a tap resets it (zoom); others count one slide. */
    private val keepsSteps: Boolean = false,
    /** Round the level to a multiple of this when the finger lifts; 0 leaves it where it is. */
    private val snapTo: Int = 0,
    private val sink: (Frame) -> Unit,
    private val haptic: () -> Unit,
) {
    /** The level under the indicator, 0-100, fractional while sliding. */
    var level: Float = 0f
        private set

    /** Muted for audio controls, playing for media: the STATE frame's flag bit. */
    var flag: Boolean = false
        private set

    /** True once a slide has started; a touch that never gets here was a tap. */
    var armed: Boolean = false
        private set

    var pressed: Boolean = false
        private set

    /** A stepper's net steps: zoom notches since the last reset, or app-switcher moves in this slide. */
    var steps: Int = 0
        private set

    /** True once the laptop has reported this level or the finger has set it; until then there is no number to show. */
    var known: Boolean = false
        private set

    private var travel = 0f
    private var moved = 0f
    private var stepRemainder = 0f
    private var lastSent = -1

    val value: Int get() = level.roundToInt()

    /** How far the ruler has slid, in dp, clockwise: tied to the level for a control, accumulated for a stepper. */
    val rulerDp: Float get() = if (control != null) level / unitsPerDp else travel

    /** The ruler's length for a control, from level 0 to 100; a stepper's ruler has no ends. */
    val rulerLengthDp: Float get() = MAX_LEVEL / unitsPerDp

    /** What the laptop reports. Ignored mid-slide so the finger, not a late STATE, owns the value. */
    fun fromLaptop(
        value: Int,
        flag: Boolean,
    ) {
        this.flag = flag
        if (!armed) level = value.toFloat().coerceIn(0f, MAX_LEVEL)
        known = true
    }

    fun down() {
        pressed = true
        armed = false
        moved = 0f
        stepRemainder = 0f
        lastSent = -1
    }

    /** The finger moved [dp] along the edge, clockwise positive. */
    fun slide(
        dp: Float,
        slop: Float,
    ) {
        if (!pressed) return
        if (!armed) {
            moved += dp
            if (abs(moved) < slop) return
            arm()
            return
        }
        val notch = floor(rulerDp / NOTCH_DP)
        if (control != null) turnLevel(dp * unitsPerDp) else turnStepper(dp)
        if (floor(rulerDp / NOTCH_DP) != notch) haptic()
    }

    /** Finger lifted. Returns true when this was a tap, which has already been sent. */
    fun up(): Boolean {
        val wasTap = pressed && !armed
        val wasArmed = armed
        pressed = false
        armed = false
        if (wasTap) {
            tap?.let { sink(it.frame()) }
            if (keepsSteps) steps = 0
        }
        if (wasArmed) {
            if (snapTo > 0) snap()
            onRelease?.invoke()
        }
        return wasTap
    }

    fun cancel() {
        val wasArmed = armed
        pressed = false
        armed = false
        if (wasArmed) onRelease?.invoke()
    }

    private fun arm() {
        armed = true
        if (!keepsSteps) steps = 0
        haptic()
        onArm?.invoke()
    }

    private fun turnLevel(units: Float) {
        level = (level + units).coerceIn(0f, MAX_LEVEL)
        known = true
        send()
    }

    private fun snap() {
        level = ((level / snapTo).roundToInt() * snapTo).toFloat().coerceIn(0f, MAX_LEVEL)
        send()
    }

    private fun send() {
        if (value != lastSent) {
            lastSent = value
            control?.let { sink(it.set(value)) }
        }
    }

    private fun turnStepper(dp: Float) {
        travel += dp
        stepRemainder += dp * unitsPerDp / UNITS_PER_STEP
        val n = stepRemainder.toInt()
        stepRemainder -= n
        if (n == 0) return
        steps += n
        val fire = step ?: return
        repeat(abs(n)) { fire(if (n > 0) 1 else -1) }
    }

    companion object {
        const val MAX_LEVEL = 100f

        /** Value units per dp at sensitivity ×1: the full 0-100 range is 400 dp of slide. */
        const val BASE_UNITS_PER_DP = 0.25f

        /** A stepper fires once per this many units of slide. */
        const val UNITS_PER_STEP = 10f

        /** Ruler notch spacing, in dp; one haptic tick per notch that passes the indicator. */
        const val NOTCH_DP = 11f
    }
}
