package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.util.TypedValue
import android.view.View
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/** The level colour a usage share takes: 0 green below 50, 1 yellow from 50, 2 orange from 75, 3 red from 90. */
object RingLevel {
    fun colourIndex(pct: Int): Int =
        when {
            pct >= 90 -> 3
            pct >= 75 -> 2
            pct >= 50 -> 1
            else -> 0
        }
}

/**
 * A 270-degree gauge for one usage window: a track, the used share on it, the value inside and a caption under it.
 * Arc and value take the [RingLevel] colour. An unknown value draws the track and a dash. Draws on the card the page
 * already gives it.
 */
class RingGauge(
    context: Context,
) : View(context) {
    private val metrics = resources.displayMetrics
    private val palette = Palette.of(context)
    private val none = context.getString(R.string.sys_none)
    private val arc = RectF()
    private val stroke = Space.S * metrics.density
    private val track =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = stroke
            strokeCap = Paint.Cap.ROUND
            color = palette.track
        }
    private val used =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = stroke
            strokeCap = Paint.Cap.ROUND
            color = palette.accent
        }
    private val valuePaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.black
            textSize = sp(Type.TITLE)
            color = palette.ink
            textAlign = Paint.Align.CENTER
        }
    private val captionPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
            textSize = sp(Type.SMALL)
            color = palette.dim
            textAlign = Paint.Align.CENTER
        }
    private val subPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
            textSize = sp(Type.MICRO)
            color = palette.dim
            textAlign = Paint.Align.CENTER
        }
    private val levels = intArrayOf(palette.levelGreen, palette.levelYellow, palette.levelOrange, palette.levelRed)
    private val captionMetrics = captionPaint.fontMetrics
    private val subMetrics = subPaint.fontMetrics

    private var pct = -1
    private var caption = ""
    private var sub = ""
    private var valueText = none
    private var diameter = 0f

    /**
     * Shows [pct] (0..100, or below 0 for unknown) under [caption] and the smaller [sub] line, such as when it
     * resets. Either may be empty.
     */
    fun set(
        pct: Int,
        caption: String,
        sub: String = "",
    ) {
        this.pct = if (pct < 0) -1 else minOf(pct, FULL)
        this.caption = caption
        this.sub = sub
        valueText = if (this.pct < 0) none else "${this.pct}%"
        val level = if (this.pct < 0) palette.ink else levels[RingLevel.colourIndex(this.pct)]
        used.color = level
        valuePaint.color = level
        contentDescription = listOf(caption, valueText, sub).filter { it.isNotEmpty() }.joinToString(", ")
        fitValue()
        invalidate()
    }

    override fun onMeasure(
        widthMeasureSpec: Int,
        heightMeasureSpec: Int,
    ) {
        val width = resolveSize((DEFAULT_DP * metrics.density).toInt(), widthMeasureSpec)
        val labels = captionMetrics.descent - captionMetrics.ascent + subMetrics.descent - subMetrics.ascent
        val ring = width - paddingLeft - paddingRight
        setMeasuredDimension(width, (ring + labels).toInt() + paddingTop + paddingBottom)
    }

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        diameter = (w - paddingLeft - paddingRight).toFloat().coerceAtLeast(0f)
        val half = stroke / 2f
        arc.set(
            paddingLeft + half,
            paddingTop + half,
            paddingLeft + diameter - half,
            paddingTop + diameter - half,
        )
        fitValue()
    }

    /** The value at the title size while it sits inside the ring, the value size when the ring is too small for it. */
    private fun fitValue() {
        valuePaint.textSize = sp(Type.TITLE)
        if (valuePaint.measureText(valueText) > diameter * INSIDE) valuePaint.textSize = sp(Type.VALUE)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawArc(arc, START, SWEEP, false, track)
        if (pct > 0) canvas.drawArc(arc, START, SWEEP * pct / FULL, false, used)
        val centreX = paddingLeft + diameter / 2f
        val centreY = paddingTop + diameter / 2f
        canvas.drawText(valueText, centreX, centreY + valuePaint.textSize * Type.CAP_CENTRE, valuePaint)
        var y = paddingTop + diameter - captionMetrics.ascent
        if (caption.isNotEmpty()) {
            canvas.drawText(caption, centreX, y, captionPaint)
            y += captionMetrics.descent
        }
        if (sub.isNotEmpty()) canvas.drawText(sub, centreX, y - subMetrics.ascent, subPaint)
    }

    private fun sp(value: Float): Float = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, metrics)

    private companion object {
        const val FULL = 100
        const val START = 135f
        const val SWEEP = 270f
        const val DEFAULT_DP = 88f

        /** The widest the value may be, as a share of the ring's diameter. */
        const val INSIDE = 0.6f
    }
}
