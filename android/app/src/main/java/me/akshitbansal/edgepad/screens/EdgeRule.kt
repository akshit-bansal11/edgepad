package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.view.View
import me.akshitbansal.edgepad.Palette

/**
 * The rulers down both edges of the guide and the device list, after the design: fine ticks on each
 * side, a longer tick every fifth on the left, and one mark resting on the left ruler. Decoration only;
 * it takes no touches and is hidden from screen readers.
 */
class EdgeRule(
    context: Context,
) : View(context) {
    private val density = resources.displayMetrics.density
    private val palette = Palette.of(context)
    private val tick =
        Paint().apply {
            color = palette.line
            strokeWidth = density
        }
    private val mark =
        Paint().apply {
            color = palette.dim
            strokeWidth = density
        }

    init {
        importantForAccessibility = IMPORTANT_FOR_ACCESSIBILITY_NO
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val right = width.toFloat()
        val minor = MINOR_LENGTH_DP * density
        var y = 0f
        var n = 0
        while (y < height) {
            val left = if (n % MAJOR_EVERY == 0) MAJOR_LENGTH_DP * density else minor
            canvas.drawLine(0f, y, left, y, tick)
            canvas.drawLine(right - minor, y, right, y, tick)
            y += STEP_DP * density
            n++
        }
        val at = height * MARK_AT
        canvas.drawLine(0f, at, MARK_LENGTH_DP * density, at, mark)
    }

    private companion object {
        const val STEP_DP = 13f
        const val MAJOR_EVERY = 5
        const val MINOR_LENGTH_DP = 7f
        const val MAJOR_LENGTH_DP = 15f
        const val MARK_LENGTH_DP = 23f
        const val MARK_AT = 0.14f
    }
}
