package me.akshitbansal.edgepad.gamepad

import kotlin.math.roundToInt

/**
 * What a finger on a stick or a trigger is worth as a controller axis.
 *
 * Pure and free of any Android type, the way [me.akshitbansal.edgepad.surface.Shapes] is, so the arithmetic
 * most likely to be wrong is tested on a JVM rather than living unreachable inside a View's touch handler.
 */
object PadAxis {
    /** XInput's own stick range, which [me.akshitbansal.edgepad.protocol.Frame.PadState] carries unchanged. */
    const val MAX = 32_767
    const val MIN = -32_768

    /** A trigger is one byte of pull, 0 for released. */
    const val TRIGGER_MAX = 255

    /**
     * One axis of a stick. [offset] is that axis' part of the thumb's offset from the centre, [distance]
     * the whole offset's length, [travel] how far the thumb can be pushed at all, and [deadZone] how far
     * it may go while still meaning nothing.
     *
     * The dead zone is taken out of the magnitude and what is left is stretched back over the whole range,
     * so the first pixel past the dead zone is a nudge rather than a jump and the rim is exactly full
     * deflection. The magnitude is scaled rather than each axis on its own, which is what keeps a diagonal
     * pointing where the thumb does instead of squaring the circle the thumb moves in.
     *
     * A thumb that has not moved sits at distance 0, which is inside any dead zone, so the division below
     * is never by zero — and it must not be, since [roundToInt] throws on a NaN rather than rounding it.
     */
    fun stick(
        offset: Float,
        distance: Float,
        travel: Float,
        deadZone: Float,
    ): Int {
        if (distance <= deadZone || travel <= deadZone) return 0
        val magnitude = ((distance - deadZone) / (travel - deadZone)).coerceAtMost(1f)
        return (offset / distance * magnitude * MAX).roundToInt().coerceIn(MIN, MAX)
    }

    /**
     * How far a trigger is pulled. [offset] is how far below the control's centre the finger is and
     * [halfHeight] half the control's height, so the top edge is nothing and the bottom edge is all of it
     * and a thumb rolled down the button squeezes it the way a real trigger is squeezed.
     */
    fun trigger(
        offset: Float,
        halfHeight: Float,
    ): Int {
        if (halfHeight <= 0f) return 0
        return ((offset + halfHeight) / (halfHeight * 2f) * TRIGGER_MAX).roundToInt().coerceIn(0, TRIGGER_MAX)
    }
}
