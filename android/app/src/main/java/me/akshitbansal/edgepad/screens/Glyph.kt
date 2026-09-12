package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.view.View
import kotlin.math.cos
import kotlin.math.sin

/** The small marks the plain screens use, drawn rather than shipped as images: chevrons, dots, a corner ruler, a gear, a refresh arrow. */
class Glyph(
    context: Context,
    private val shape: Shape,
    color: Int,
    private val count: Int = 1,
) : View(context) {
    /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
    constructor(context: Context) : this(context, Shape.DOT, 0)

    enum class Shape { CHEVRON_RIGHT, CHEVRON_LEFT, DOT, CORNER, GEAR, REFRESH }

    private val density = resources.displayMetrics.density
    private val paint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            this.color = color
            strokeCap = Paint.Cap.ROUND
            strokeJoin = Paint.Join.ROUND
            strokeWidth = STROKE_DP * density
        }
    private val corner = Path()
    private val bend = RectF()

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        val o = paint.strokeWidth / 2
        val length = minOf(w, h) - paint.strokeWidth
        val r = CORNER_RADIUS_DP * density
        corner.reset()
        corner.moveTo(o, o + length)
        corner.lineTo(o, o + r)
        bend.set(o, o, o + 2 * r, o + 2 * r)
        corner.arcTo(bend, HALF_TURN, QUARTER_TURN)
        corner.lineTo(o + length, o)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val cx = width / 2f
        val cy = height / 2f
        when (shape) {
            Shape.CHEVRON_RIGHT, Shape.CHEVRON_LEFT -> {
                paint.style = Paint.Style.STROKE
                val arm = CHEVRON_DP * density / 2
                val dir = if (shape == Shape.CHEVRON_RIGHT) 1 else -1
                canvas.drawLine(cx - dir * arm / 2, cy - arm, cx + dir * arm / 2, cy, paint)
                canvas.drawLine(cx + dir * arm / 2, cy, cx - dir * arm / 2, cy + arm, paint)
            }

            Shape.DOT -> {
                paint.style = Paint.Style.FILL
                val r = DOT_DP * density / 2
                val gap = DOT_GAP_DP * density
                val y = DOT_TOP_DP * density + r
                repeat(count) { i -> canvas.drawCircle(r + i * (2 * r + gap), y, r, paint) }
            }

            Shape.GEAR -> {
                paint.style = Paint.Style.STROKE
                val outer = GEAR_DP * density / 2
                gear(corner, cx, cy, outer)
                canvas.drawPath(corner, paint)
                canvas.drawCircle(cx, cy, outer * GEAR_HOLE, paint)
            }

            Shape.REFRESH -> {
                paint.style = Paint.Style.STROKE
                val r = REFRESH_DP * density / 2
                bend.set(cx - r, cy - r, cx + r, cy + r)
                canvas.drawArc(bend, REFRESH_START, REFRESH_SWEEP, false, paint)
                // The arrowhead at the arc's end, pointing on round the circle.
                val end = Math.toRadians((REFRESH_START + REFRESH_SWEEP).toDouble())
                val ex = cx + cos(end).toFloat() * r
                val ey = cy + sin(end).toFloat() * r
                val head = REFRESH_HEAD_DP * density
                canvas.drawLine(ex, ey, ex - head, ey - head * ARROW_SLANT, paint)
                canvas.drawLine(ex, ey, ex + head * ARROW_SLANT, ey - head, paint)
            }

            Shape.CORNER -> {
                paint.style = Paint.Style.STROKE
                canvas.drawPath(corner, paint)
                paint.alpha = TICK_ALPHA
                val step = TICK_STEP_DP * density
                val tick = TICK_DP * density
                val start = CORNER_RADIUS_DP * density + step
                var at = start
                while (at < minOf(width, height)) {
                    canvas.drawLine(at, 0f, at, tick, paint)
                    canvas.drawLine(0f, at, tick, at, paint)
                    at += step
                }
                paint.alpha = OPAQUE
            }
        }
    }

    companion object {
        /** Eight square teeth round a rim: the gear's outline, written into [path] round ([cx], [cy]). */
        fun gear(
            path: Path,
            cx: Float,
            cy: Float,
            outer: Float,
        ) {
            path.reset()
            val inner = outer * GEAR_RIM
            val step = FULL_TURN / (GEAR_TEETH * 2)
            for (i in 0 until GEAR_TEETH * 2) {
                val r = if (i % 2 == 0) outer else inner
                val a0 = Math.toRadians(i * step - step * GEAR_TOOTH)
                val a1 = Math.toRadians(i * step + step * GEAR_TOOTH)
                val x0 = cx + cos(a0).toFloat() * r
                val y0 = cy + sin(a0).toFloat() * r
                if (i == 0) path.moveTo(x0, y0) else path.lineTo(x0, y0)
                path.lineTo(cx + cos(a1).toFloat() * r, cy + sin(a1).toFloat() * r)
            }
            path.close()
        }

        const val GEAR_HOLE = 0.28f
        private const val GEAR_DP = 22f
        private const val GEAR_RIM = 0.72f
        private const val GEAR_TOOTH = 0.42f
        private const val GEAR_TEETH = 8
        private const val FULL_TURN = 360.0
        private const val REFRESH_DP = 20f
        private const val REFRESH_START = -60f
        private const val REFRESH_SWEEP = 270f
        private const val REFRESH_HEAD_DP = 4f
        private const val ARROW_SLANT = 0.4f
        private const val STROKE_DP = 1.5f
        const val CHEVRON_DP = 9f
        const val DOT_DP = 6f
        const val DOT_GAP_DP = 3f
        const val DOT_TOP_DP = 4f
        const val CORNER_RADIUS_DP = 11f
        const val TICK_STEP_DP = 5f
        const val TICK_DP = 6f
        const val TICK_ALPHA = 153
        const val OPAQUE = 255
        const val HALF_TURN = 180f
        const val QUARTER_TURN = 90f
    }
}
