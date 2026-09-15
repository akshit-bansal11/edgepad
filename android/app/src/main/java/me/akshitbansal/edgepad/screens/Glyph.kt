package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.drawable.Drawable
import android.view.View
import me.akshitbansal.edgepad.R

/** The small marks the plain screens use: Lucide icons for chevrons, the gear, refresh and Bluetooth; a drawn dot row and corner ruler. */
class Glyph(
    context: Context,
    private val shape: Shape,
    color: Int,
    private val count: Int = 1,
) : View(context) {
    /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
    constructor(context: Context) : this(context, Shape.DOT, 0)

    enum class Shape(
        val icon: Int?,
    ) {
        CHEVRON_RIGHT(R.drawable.ic_chevron_right),
        CHEVRON_LEFT(R.drawable.ic_chevron_left),
        DOT(null),
        CORNER(null),
        GEAR(R.drawable.ic_settings),
        REFRESH(R.drawable.ic_refresh_cw),
        BLUETOOTH(R.drawable.ic_bluetooth),
    }

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
    private val icon: Drawable? = shape.icon?.let { context.getDrawable(it)?.mutate()?.apply { setTint(color) } }

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        val half = (ICON_DP * density / 2).toInt()
        icon?.setBounds(w / 2 - half, h / 2 - half, w / 2 + half, h / 2 + half)
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
        val drawn = icon
        if (drawn != null) {
            drawn.draw(canvas)
            return
        }
        when (shape) {
            Shape.CHEVRON_RIGHT, Shape.CHEVRON_LEFT, Shape.GEAR, Shape.REFRESH, Shape.BLUETOOTH -> {
                Unit
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
        const val ICON_DP = 20f
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
