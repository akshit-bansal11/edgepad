package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import kotlin.math.abs
import kotlin.math.atan2
import kotlin.math.hypot
import kotlin.math.roundToInt

/**
 * One ruler dial on the screen's edge, like a camera's zoom dial. Press and hold arms it; then dragging
 * around its centre drags the ruler under a fixed indicator, and the value under the indicator is what
 * gets sent. A tap without a hold runs [tap]. A [control] dial holds a 0-100 level and sends SET frames;
 * a dial with none is a stepper that calls [step] with +1 or -1 once per ruler step, and [onArm] and
 * [onRelease] bracket the drag (the app switcher holds Alt that way).
 *
 * Pure geometry and state, so the value mapping is tested on the JVM; the View draws it.
 */
class Dial(
    val kind: DialKind,
    val placement: Placement,
    val label: String,
    val control: ControlId? = null,
    private val tap: ActionId? = null,
    private val step: ((Int) -> Unit)? = null,
    private val onArm: (() -> Unit)? = null,
    private val onRelease: (() -> Unit)? = null,
    private val sink: (Frame) -> Unit,
    private val haptic: () -> Unit,
) {
    /** The level shown under the indicator, 0-100, as a fraction while dragging. */
    var level: Float = 0f
        private set

    var muted: Boolean = false
        private set

    var armed: Boolean = false
        private set

    var pressed: Boolean = false
        private set

    /** For a stepper: how far the ruler has been dragged, in degrees, so it can be drawn scrolling. */
    var rulerOffsetDeg: Float = 0f
        private set

    private var lastAngle = 0f
    private var downX = 0f
    private var downY = 0f
    private var stepRemainder = 0f
    private var lastSent = -1

    val value: Int get() = level.roundToInt()

    /** What the laptop reports. Ignored mid-drag so the finger, not a late STATE, owns the value. */
    fun fromLaptop(
        value: Int,
        muted: Boolean,
    ) {
        this.muted = muted
        if (!armed) level = value.toFloat().coerceIn(0f, MAX_LEVEL)
    }

    /** Finger down at ([x], [y]) relative to the dial's centre. */
    fun down(
        x: Float,
        y: Float,
    ) {
        pressed = true
        armed = false
        downX = x
        downY = y
        lastAngle = angle(x, y)
        stepRemainder = 0f
        lastSent = -1
    }

    /** The hold timer elapsed with the finger still down. */
    fun hold() {
        if (pressed && !armed) arm()
    }

    fun move(
        x: Float,
        y: Float,
        slop: Float,
    ) {
        if (!pressed) return
        if (!armed) {
            if (hypot(x - downX, y - downY) < slop) return
            arm()
        }
        val now = angle(x, y)
        var delta = now - lastAngle
        while (delta > HALF_TURN) delta -= FULL_TURN
        while (delta < -HALF_TURN) delta += FULL_TURN
        lastAngle = now
        // The ruler moves with the finger, and the value is whatever tick that leaves under the indicator.
        val units = placement.direction * delta / DEGREES_PER_UNIT
        if (control != null) turnLevel(units) else turnStepper(units, delta)
    }

    /** Finger lifted. Returns true when this was a tap, which has already been sent. */
    fun up(): Boolean {
        val wasTap = pressed && !armed
        val wasArmed = armed
        pressed = false
        armed = false
        if (wasTap) tap?.let { sink(it.frame()) }
        if (wasArmed) onRelease?.invoke()
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
        haptic()
        onArm?.invoke()
    }

    private fun turnLevel(units: Float) {
        val before = value
        level = (level + units).coerceIn(0f, MAX_LEVEL)
        if (value != before) {
            haptic()
            if (value != lastSent) {
                lastSent = value
                control?.let { sink(it.set(value)) }
            }
        }
    }

    private fun turnStepper(
        units: Float,
        delta: Float,
    ) {
        rulerOffsetDeg += delta
        stepRemainder += units / UNITS_PER_STEP
        val steps = stepRemainder.toInt()
        stepRemainder -= steps
        val fire = step ?: return
        repeat(abs(steps)) {
            haptic()
            fire(if (steps > 0) 1 else -1)
        }
    }

    private fun angle(
        x: Float,
        y: Float,
    ): Float = Math.toDegrees(atan2(y, x).toDouble()).toFloat()

    companion object {
        const val MAX_LEVEL = 100f

        /** One value unit per this many degrees, so 0-100 spans more than the visible arc. */
        const val DEGREES_PER_UNIT = 3f

        /** A stepper fires once per this many units of ruler travel. */
        const val UNITS_PER_STEP = 5f

        private const val HALF_TURN = 180f
        private const val FULL_TURN = 360f
    }
}
