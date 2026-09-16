package me.akshitbansal.edgepad.screens

import kotlin.math.roundToInt

/**
 * A continuous setting shown as a slider with fixed steps. `Ui.slider` only knows whole step
 * indices, so every caller needs the same conversion: how many steps fit between the bounds, what
 * value a step maps to, and which step a stored value starts on. One object per setting keeps
 * those three numbers from drifting apart the way three hand-written formulas can.
 */
class StepRange(
    private val min: Float,
    max: Float,
    private val step: Float,
) {
    /** What to pass to Ui.slider as its maximum. */
    val steps: Int = ((max - min) / step).roundToInt()

    fun valueAt(step: Int): Float = min + step * this.step

    fun stepOf(value: Float): Int = ((value - min) / step).roundToInt()
}
