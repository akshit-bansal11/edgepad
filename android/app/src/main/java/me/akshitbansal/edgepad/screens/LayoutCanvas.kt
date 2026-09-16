package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.text.TextPaint
import android.view.View
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * The full-size canvas both layout editors drag pieces on: a dot grid, a border, and two centre lines
 * that light up when a piece snaps to them. A subclass calls `super.onDraw` first, which draws this
 * background layer, then draws its own pieces on top.
 */
internal abstract class LayoutCanvas(
    context: Context,
) : View(context) {
    protected val density = resources.displayMetrics.density
    protected val palette = Palette.of(context)
    protected val box = RectF()
    protected val stroke =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = Space.HAIR * density
        }
    private val dots = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = palette.line }
    protected val lift =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = palette.faint
            setShadowLayer(LIFT_BLUR_DP * density, 0f, LIFT_DROP_DP * density, SHADOW)
        }
    protected val text: TextPaint = Type.pieceLabel(resources.displayMetrics)

    private var onCentreX = false
    private var onCentreY = false

    /** Marks the centre lines as lit, from a snapped x/y fraction. */
    protected fun setOnCentre(
        x: Float,
        y: Float,
    ) {
        onCentreX = abs(x - HALF) < EPSILON
        onCentreY = abs(y - HALF) < EPSILON
    }

    protected fun clearOnCentre() {
        onCentreX = false
        onCentreY = false
    }

    /** The grid point nearest [px] along an axis of [extent] pixels, or the centre when close to it, as a fraction. */
    protected fun snap(
        px: Float,
        extent: Int,
    ): Float {
        val centre = extent / 2f
        if (abs(px - centre) < CENTRE_SNAP_DP * density) return HALF
        val cell = GRID_DP * density
        val snapped = (px / cell).roundToInt() * cell
        return (snapped / extent).coerceIn(0f, 1f)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val cell = GRID_DP * density
        val r = DOT_DP * density / 2
        var y = cell
        while (y < height) {
            var x = cell
            while (x < width) {
                canvas.drawCircle(x, y, r, dots)
                x += cell
            }
            y += cell
        }
        stroke.pathEffect = null
        stroke.color = palette.line
        canvas.drawRect(
            stroke.strokeWidth / 2,
            stroke.strokeWidth / 2,
            width - stroke.strokeWidth / 2,
            height - stroke.strokeWidth / 2,
            stroke,
        )
        // The centre lines: faint always, ink while a piece sits on one.
        stroke.color = if (onCentreX) palette.ink else palette.line
        canvas.drawLine(width / 2f, 0f, width / 2f, height.toFloat(), stroke)
        stroke.color = if (onCentreY) palette.ink else palette.line
        canvas.drawLine(0f, height / 2f, width.toFloat(), height / 2f, stroke)
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    // Not protected: a companion object's members are members of the companion, so protected there
    // restricts them to subclasses of the companion rather than of this class. The class is internal,
    // so these reach no further than the module either way.
    companion object {
        const val CORNER_DP = 6f
        const val GRID_DP = 12f
        const val DOT_DP = 2f
        const val CENTRE_SNAP_DP = 10f
        const val HALF = 0.5f
        const val EPSILON = 1e-3f
        const val LIFT_BLUR_DP = 12f
        const val LIFT_DROP_DP = 6f
        const val SHADOW = 0x59000000
    }
}
