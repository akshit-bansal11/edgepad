package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.view.View

/** The few small marks the plain screens use, drawn rather than shipped as images: chevrons, finger dots, a corner ruler. */
class Glyph(
    context: Context,
    private val shape: Shape,
    color: Int,
    private val count: Int = 1,
) : View(context) {
    enum class Shape { CHEVRON_RIGHT, CHEVRON_LEFT, DOT, CORNER }

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

    private companion object {
        const val STROKE_DP = 1.5f
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
