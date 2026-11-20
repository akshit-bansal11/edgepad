package me.akshitbansal.edgepad.surface

import kotlin.math.abs

/** The finger-to-dial maths: where on the screen's edge a finger is, and how far along it the finger has moved. */
object DialSlide {
    /** A move along the path longer than this is the nearest edge flipping inside the screen, not a slide. */
    const val JUMP_DP = 64f

    /** The signed way along [perimeter] from [last] to the finger at ([x], [y]); the new position lands in hit[0]. */
    fun travel(
        perimeter: Perimeter,
        x: Float,
        y: Float,
        hit: FloatArray,
        last: Float,
    ): Float {
        perimeter.project(x, y, hit)
        return perimeter.delta(last, hit[0])
    }

    /** Whether [moved] pixels is a jump along the path rather than a slide. */
    fun isJump(
        moved: Float,
        density: Float,
    ) = abs(moved) > JUMP_DP * density

    /**
     * Slides [dial] by the finger's move from [last] to ([x], [y]) and returns the finger's new position along
     * [perimeter], which is the [last] of the next call. A jump moves nothing but is still the new position.
     */
    fun slide(
        dial: Dial,
        perimeter: Perimeter,
        x: Float,
        y: Float,
        hit: FloatArray,
        last: Float,
        density: Float,
        slop: Float,
    ): Float {
        val moved = travel(perimeter, x, y, hit, last)
        if (!isJump(moved, density)) dial.slide(moved / density, slop)
        return hit[0]
    }
}
