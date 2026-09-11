package me.akshitbansal.edgepad.surface

import android.graphics.Canvas
import android.graphics.Paint
import android.text.TextPaint
import android.util.DisplayMetrics
import android.util.TypedValue
import kotlin.math.ceil
import kotlin.math.floor

/**
 * Draws one corner ruler along a [Perimeter]: the ticks, the fixed indicator, and the label and number
 * inside the bend. The control surface and the size preview in Settings share it, so what the preview
 * shows is what the surface draws. Allocation-free: the caller passes a scratch point array.
 */
class RulerPainter(
    private val metrics: DisplayMetrics,
    /** Half the ruler's length along the edge, in dp. */
    var halfLengthDp: Float,
    /** Tick height as a multiple of the base. */
    var height: Float,
) {
    private val tick = Paint(Paint.ANTI_ALIAS_FLAG)
    private val text =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = me.akshitbansal.edgepad.Type.mono
            textAlign = Paint.Align.CENTER
        }

    /**
     * Draws a ruler centred at path length [centre]. [rulerDp] is how far the ruler has slid clockwise;
     * [rulerLengthDp] bounds a level's ruler (null for a stepper, whose ruler has no ends).
     */
    fun draw(
        canvas: Canvas,
        perimeter: Perimeter,
        centre: Float,
        rulerDp: Float,
        rulerLengthDp: Float?,
        armed: Boolean,
        color: Int,
        dimColor: Int,
        label: String,
        number: String,
        pt: FloatArray,
    ) {
        val notch = dp(Dial.NOTCH_DP)
        val half = dp(halfLengthDp)
        val ruler = dp(rulerDp)
        val grow = if (armed) ARMED_GROWTH else 1f
        var first = ceil((-half - ruler) / notch).toInt()
        var last = floor((half - ruler) / notch).toInt()
        if (rulerLengthDp != null) {
            first = maxOf(first, -floor(rulerLengthDp / Dial.NOTCH_DP).toInt())
            last = minOf(last, 0)
        }
        tick.color = color
        for (n in first..last) {
            val major = n % Dial.MAJOR_EVERY == 0
            perimeter.point(centre + ruler + n * notch, pt)
            val depth = dp(if (major) MAJOR_TICK_DP else MINOR_TICK_DP) * grow * height
            tick.strokeWidth = dp(if (major) MAJOR_STROKE_DP else MINOR_STROKE_DP)
            tick.alpha =
                when {
                    major -> MAJOR_ALPHA
                    armed -> ARMED_MINOR_ALPHA
                    else -> MINOR_ALPHA
                }
            canvas.drawLine(pt[0], pt[1], pt[0] + pt[2] * depth, pt[1] + pt[3] * depth, tick)
        }

        perimeter.point(centre, pt)
        tick.alpha = OPAQUE
        tick.strokeWidth = dp(INDICATOR_STROKE_DP)
        val reach = dp(INDICATOR_DP) * grow * height
        canvas.drawLine(pt[0], pt[1], pt[0] + pt[2] * reach, pt[1] + pt[3] * reach, tick)

        // The label and the number sit inside the corner, on the diagonal, further in as the ticks grow.
        val depth = dp(LABEL_DP) * (1f + (height - 1f) / 2)
        val ax = pt[0] + pt[2] * depth
        val ay = pt[1] + pt[3] * depth
        val labelSize = sp(LABEL_SP)
        val valueSize = sp(if (armed) ARMED_VALUE_SP else VALUE_SP)
        val block = if (number.isEmpty()) labelSize else labelSize + dp(GAP_DP) + valueSize
        val top = ay - block / 2
        text.letterSpacing = LABEL_TRACKING
        text.textSize = labelSize
        text.color = dimColor
        canvas.drawText(label, ax, top + labelSize * BASELINE, text)
        if (number.isNotEmpty()) {
            text.letterSpacing = 0f
            text.textSize = valueSize
            text.color = color
            canvas.drawText(number, ax, top + labelSize + dp(GAP_DP) + valueSize * BASELINE, text)
        }
    }

    private fun dp(value: Float): Float = value * metrics.density

    private fun sp(value: Float): Float = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, metrics)

    companion object {
        const val MAJOR_TICK_DP = 34f
        private const val MINOR_TICK_DP = 22f
        private const val MAJOR_STROKE_DP = 2f
        private const val MINOR_STROKE_DP = 1.2f
        private const val MAJOR_ALPHA = 230
        private const val MINOR_ALPHA = 128
        private const val ARMED_MINOR_ALPHA = 190
        private const val OPAQUE = 255
        private const val ARMED_GROWTH = 1.25f
        private const val INDICATOR_DP = 48f
        private const val INDICATOR_STROKE_DP = 2.5f
        private const val LABEL_DP = 96f
        private const val LABEL_SP = 11f
        private const val LABEL_TRACKING = 0.1f
        private const val VALUE_SP = 26f
        private const val ARMED_VALUE_SP = 32f
        private const val GAP_DP = 4f
        private const val BASELINE = 0.8f
    }
}
