package me.akshitbansal.edgepad.surface

import android.graphics.Canvas
import android.graphics.Paint
import android.text.TextPaint
import android.util.DisplayMetrics
import android.util.TypedValue
import me.akshitbansal.edgepad.Type
import kotlin.math.abs
import kotlin.math.ceil
import kotlin.math.floor
import kotlin.math.roundToInt

/**
 * Draws one corner ruler along a [Perimeter]: the notches, the fixed indicator, and the label and number
 * inside the bend. The control surface and the size preview in Settings share it, so what the preview
 * shows is what the surface draws. Allocation-free: the caller passes a scratch point array.
 *
 * Minor notches are dots and every fifth is a short rounded pill; the indicator is an accent pill over a
 * soft glow. On a level dial the notches between the indicator and the ruler's zero end, the amount the
 * user has, take the accent, so the dial reads as a bent progress bar. Both ends of the span feather out.
 */
class RulerPainter(
    private val metrics: DisplayMetrics,
    /** Half the ruler's length along the edge, in dp. */
    var halfLengthDp: Float,
    /** Mark length as a multiple of the base. */
    var height: Float,
) {
    // One paint for every mark: drawLine strokes it whatever its style, and drawCircle fills the dots.
    private val mark = Paint(Paint.ANTI_ALIAS_FLAG).apply { strokeCap = Paint.Cap.ROUND }
    private val labelPaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.bold
            textAlign = Paint.Align.CENTER
            letterSpacing = LABEL_TRACKING
        }
    private val valuePaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.black
            textAlign = Paint.Align.CENTER
            letterSpacing = Type.TRACKING_TIGHT
        }

    /**
     * Draws a ruler centred at path length [centre], reaching [after] pixels clockwise and [before]
     * anticlockwise. [rulerDp] is how far the ruler has slid clockwise; [rulerLengthDp] bounds a level's
     * ruler (null for a stepper, whose ruler has no ends). Notches are [ink], the indicator and the lit
     * notches [accent], the label [dim].
     */
    fun draw(
        canvas: Canvas,
        perimeter: Perimeter,
        centre: Float,
        after: Float,
        before: Float,
        rulerDp: Float,
        rulerLengthDp: Float?,
        armed: Boolean,
        ink: Int,
        accent: Int,
        dim: Int,
        label: String,
        number: String,
        pt: FloatArray,
    ) {
        val notch = dp(Dial.NOTCH_DP)
        val ruler = dp(rulerDp)
        var first = ceil((-before - ruler) / notch).toInt()
        var last = floor((after - ruler) / notch).toInt()
        if (rulerLengthDp != null) {
            first = maxOf(first, -floor(rulerLengthDp / Dial.NOTCH_DP).toInt())
            last = minOf(last, 0)
        }
        val inset = dp(RulerGeometry.INSET_DP)
        val majorEnd = dp(RulerGeometry.majorEnd(height, armed))
        val dot = dp(RulerGeometry.dotDiameter(height))
        val feather = dp(FEATHER_DP)
        for (n in first..last) {
            val s = ruler + n * notch
            val room = if (s >= 0) after - s else before + s
            // Feathered at both ends, and ducked as it slides under the indicator so the two never read
            // as one doubled stroke.
            val duck = DUCK_FLOOR + (1 - DUCK_FLOOR) * smooth((abs(s) / metrics.density - DUCK_START_DP) / DUCK_SPAN_DP)
            val fade = smooth(room / feather) * duck
            if (fade <= MIN_FADE) continue
            val lit = rulerLengthDp != null && s > dp(LIT_FROM_DP)
            perimeter.point(centre + s, pt)
            if (n % Dial.MAJOR_EVERY == 0) {
                tint(if (lit) accent else ink, (if (lit) 1f else MAJOR_OPACITY) * fade)
                mark.strokeWidth = dp(if (armed) ARMED_MAJOR_WIDTH_DP else MAJOR_WIDTH_DP)
                val limit = RulerGeometry.limit(perimeter.radius, perimeter.fromBend(centre + s))
                line(canvas, inset, minOf(majorEnd, limit), pt)
            } else {
                val opacity =
                    when {
                        lit -> LIT_DOT_OPACITY
                        armed -> ARMED_DOT_OPACITY
                        else -> DOT_OPACITY
                    }
                tint(if (lit) accent else ink, opacity * fade)
                val depth = inset + dot / 2
                canvas.drawCircle(pt[0] + pt[2] * depth, pt[1] + pt[3] * depth, dot / 2, mark)
            }
        }

        perimeter.point(centre, pt)
        val start = dp(RulerGeometry.INSET_DP - INDICATOR_LEAD_DP)
        val end =
            minOf(
                dp(RulerGeometry.indicatorEnd(height, armed)),
                RulerGeometry.limit(perimeter.radius, perimeter.fromBend(centre)),
            )
        tint(accent, if (armed) ARMED_GLOW_OPACITY else GLOW_OPACITY)
        mark.strokeWidth = dp(GLOW_WIDTH_DP)
        line(canvas, start, end, pt)
        tint(accent, 1f)
        mark.strokeWidth = dp(INDICATOR_WIDTH_DP)
        line(canvas, start, end, pt)

        // The label and the number sit inside the corner, on its normal, clear of the longest mark the
        // height allows, so they do not move when the dial arms and its marks grow.
        val depth = dp(RulerGeometry.reach(height) + LABEL_GAP_DP)
        val ax = pt[0] + pt[2] * depth
        val ay = pt[1] + pt[3] * depth
        val labelSize = sp(LABEL_SP)
        val valueSize = sp(if (armed) ARMED_VALUE_SP else VALUE_SP)
        val block = if (number.isEmpty()) labelSize else labelSize + dp(GAP_DP) + valueSize
        val top = ay - block / 2
        labelPaint.textSize = labelSize
        labelPaint.color = dim
        canvas.drawText(label, ax, top + labelSize * BASELINE, labelPaint)
        if (number.isNotEmpty()) {
            valuePaint.textSize = valueSize
            valuePaint.color = if (armed) accent else ink
            canvas.drawText(number, ax, top + labelSize + dp(GAP_DP) + valueSize * BASELINE, valuePaint)
        }
    }

    /** [color] at [opacity] of its own alpha. */
    private fun tint(
        color: Int,
        opacity: Float,
    ) {
        mark.color = color
        mark.alpha = ((color ushr ALPHA_SHIFT) * opacity).roundToInt()
    }

    /** A mark along the inward normal at [pt], from [from] to [to] pixels in from the edge. */
    private fun line(
        canvas: Canvas,
        from: Float,
        to: Float,
        pt: FloatArray,
    ) = canvas.drawLine(pt[0] + pt[2] * from, pt[1] + pt[3] * from, pt[0] + pt[2] * to, pt[1] + pt[3] * to, mark)

    private fun dp(value: Float): Float = value * metrics.density

    private fun sp(value: Float): Float = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, metrics)

    private companion object {
        const val MAJOR_WIDTH_DP = 3.2f
        const val ARMED_MAJOR_WIDTH_DP = 3.6f
        const val MAJOR_OPACITY = 0.85f
        const val DOT_OPACITY = 0.42f
        const val ARMED_DOT_OPACITY = 0.6f
        const val LIT_DOT_OPACITY = 0.95f

        /** A notch counts as held once it is this far clockwise of the indicator. */
        const val LIT_FROM_DP = 0.5f

        /** How far the ends fade over, and below what a notch is not worth drawing. */
        const val FEATHER_DP = 36f
        const val MIN_FADE = 0.02f

        /** A notch under the indicator keeps this much, and is whole again [DUCK_SPAN_DP] past [DUCK_START_DP]. */
        const val DUCK_FLOOR = 0.15f
        const val DUCK_START_DP = 3f
        const val DUCK_SPAN_DP = 9f

        /** The indicator starts this much nearer the edge than the notches, so it reads as the one on top. */
        const val INDICATOR_LEAD_DP = 1f
        const val INDICATOR_WIDTH_DP = 5f
        const val GLOW_WIDTH_DP = 12f
        const val GLOW_OPACITY = 0.18f
        const val ARMED_GLOW_OPACITY = 0.28f

        const val LABEL_GAP_DP = 40f
        const val LABEL_SP = 11f

        /** The one place 2.0 spaces letters: a three-letter abbreviation in capitals reads better opened up. */
        const val LABEL_TRACKING = 0.06f
        const val VALUE_SP = 26f
        const val ARMED_VALUE_SP = 32f
        const val GAP_DP = 4f
        const val BASELINE = 0.8f
        const val ALPHA_SHIFT = 24

        /** Smoothstep, clamped: 0 below 0, 1 above 1, an S between. */
        fun smooth(x: Float): Float {
            val t = x.coerceIn(0f, 1f)
            return t * t * (3 - 2 * t)
        }
    }
}

/**
 * How far each mark of a corner ruler reaches in from the edge, in dp, and how far any mark may reach at a
 * given place for no two to cross. Pure, and apart from the painter, so that promise is tested on the JVM.
 *
 * A mark is drawn along the path's inward normal, and on a bend of radius R every normal converges on the
 * bend's centre, so two marks cross exactly when one reaches past R. The path follows the display's own
 * rounding exactly, so the dials sit in the phone's real corners, and a tall dial's marks are what give way:
 * [limit] holds a mark in the bend short of its centre and lets it grow back one for one as it moves out
 * along a straight edge, so the marks shorten smoothly into a corner instead of folding over each other.
 * (3.1.0 widened the bend instead, which left a gap between a small display rounding and the dial.)
 */
object RulerGeometry {
    /** Every mark starts this far in from the edge. */
    const val INSET_DP = 5f

    private const val MAJOR_DP = 20f
    private const val INDICATOR_DP = 32f
    private const val ARMED_GROWTH = 1.2f
    private const val DOT_DP = 3f
    private const val DOT_PER_HEIGHT_DP = 0.8f

    /** The share of the bend's radius a mark in the bend may reach, short of the centre where normals meet. */
    private const val BEND_REACH = 0.85f

    private fun grow(armed: Boolean): Float = if (armed) ARMED_GROWTH else 1f

    /** Where every fifth notch, the pill, ends. */
    fun majorEnd(
        height: Float,
        armed: Boolean,
    ): Float = INSET_DP + MAJOR_DP * height * grow(armed)

    /** Where the indicator ends. */
    fun indicatorEnd(
        height: Float,
        armed: Boolean,
    ): Float = INSET_DP + INDICATOR_DP * height * grow(armed)

    /** A minor notch's dot, which sits just inside [INSET_DP]. */
    fun dotDiameter(height: Float): Float = DOT_DP + DOT_PER_HEIGHT_DP * height

    /** The furthest any mark reaches at [height]: the indicator, armed. */
    fun reach(height: Float): Float = indicatorEnd(height, armed = true)

    /**
     * The furthest in any mark may end, for a path bending at [radius] and a mark [fromBend] along a straight
     * edge from the nearest bend (0 in the bend), in the path's own units. On one edge the marks are parallel;
     * across a corner, a mark on each edge crosses only when each is longer than the other's distance from the
     * corner, which this rules out; in the bend, no mark reaches the centre every normal meets at.
     */
    fun limit(
        radius: Float,
        fromBend: Float,
    ): Float = BEND_REACH * radius + fromBend
}
