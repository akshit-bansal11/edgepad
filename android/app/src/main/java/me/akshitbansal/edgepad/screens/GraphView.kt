package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.util.TypedValue
import android.view.View
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.StatsRing

/**
 * One series of [StatsRing] as a line with a soft fill, the current value and its caption on the left. It draws on
 * the card the page already gives it, so it has no background of its own; the screen sets its height (88 dp upright,
 * 72 dp sideways). Reads the ring live: call [set] again, or [invalidate], when a frame arrived.
 */
class GraphView(
    context: Context,
) : View(context) {
    private val metrics = resources.displayMetrics
    private val palette = Palette.of(context)
    private val grid =
        Paint().apply {
            style = Paint.Style.STROKE
            strokeWidth = Space.HAIR * metrics.density
            color = palette.line
        }
    private val line =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = LINE_DP * metrics.density
            strokeJoin = Paint.Join.ROUND
            strokeCap = Paint.Cap.ROUND
            color = palette.accent
        }
    private val fill =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.FILL
            color = palette.accent
            alpha = FILL_ALPHA
        }
    private val valuePaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.black
            textSize = sp(Type.TITLE)
            color = palette.ink
        }
    private val captionPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
            textSize = sp(Type.SMALL)
            color = palette.dim
        }
    private val limitPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
            textSize = sp(Type.MICRO)
            color = palette.dim
            textAlign = Paint.Align.RIGHT
        }
    private val valueMetrics = valuePaint.fontMetrics
    private val captionMetrics = captionPaint.fontMetrics
    private val limitMetrics = limitPaint.fontMetrics
    private val linePath = Path()
    private val fillPath = Path()

    private var ring: StatsRing? = null
    private var series = 0
    private var caption = ""
    private var valueText = ""
    private var scaleMax = 1
    private var maxLabel = ""
    private var minLabel = ""
    private var textWidth = 0f

    /**
     * Shows [series] of [ring]. [caption] is the small label, [valueText] the current value as the screen formats it
     * (units and unknown included). [fixedMax] is the top of the scale (100 for a percent), or 0 for the running
     * maximum of what is held, as a rate needs; [unit] is appended to the min and max labels, which are drawn only
     * when [limits] is set.
     */
    fun set(
        ring: StatsRing,
        series: Int,
        caption: String,
        valueText: String,
        fixedMax: Int,
        unit: String = "",
        limits: Boolean = false,
    ) {
        this.ring = ring
        this.series = series
        this.caption = caption
        this.valueText = valueText
        var lo = Int.MAX_VALUE
        var hi = 0
        for (i in 0 until ring.size) {
            val v = ring.value(series, i)
            if (v < 0) continue
            lo = minOf(lo, v)
            hi = maxOf(hi, v)
        }
        scaleMax = if (fixedMax > 0) fixedMax else maxOf(hi, 1)
        minLabel = if (limits && lo != Int.MAX_VALUE) "$lo$unit" else ""
        maxLabel = if (limits && lo != Int.MAX_VALUE) "$hi$unit" else ""
        textWidth = maxOf(valuePaint.measureText(valueText), captionPaint.measureText(caption))
        contentDescription = "$caption $valueText"
        invalidate()
    }

    override fun onMeasure(
        widthMeasureSpec: Int,
        heightMeasureSpec: Int,
    ) {
        val height = resolveSize((DEFAULT_HEIGHT_DP * metrics.density).toInt(), heightMeasureSpec)
        setMeasuredDimension(resolveSize(0, widthMeasureSpec), height)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val gap = Space.M * metrics.density
        val inset = Space.XS * metrics.density
        val left = paddingLeft + textWidth + gap
        val right = (width - paddingRight).toFloat()
        val upper = paddingTop + inset
        val lower = height - paddingBottom - inset
        val textX = paddingLeft.toFloat()
        val valueHeight = valueMetrics.descent - valueMetrics.ascent
        val block = valueHeight + captionMetrics.descent - captionMetrics.ascent
        val blockTop = (height - block) / 2f
        canvas.drawText(valueText, textX, blockTop - valueMetrics.ascent, valuePaint)
        canvas.drawText(caption, textX, blockTop + valueHeight - captionMetrics.ascent, captionPaint)
        if (right - left <= 0f) return
        for (i in 0..2) {
            val y = lower - (lower - upper) * i / 2f
            canvas.drawLine(left, y, right, y, grid)
        }
        if (maxLabel.isNotEmpty()) {
            canvas.drawText(maxLabel, right, upper + limitPaint.textSize, limitPaint)
            canvas.drawText(minLabel, right, lower - limitMetrics.descent, limitPaint)
        }
        plot(canvas, left, right, upper, lower)
    }

    private fun plot(
        canvas: Canvas,
        left: Float,
        right: Float,
        upper: Float,
        lower: Float,
    ) {
        val ring = ring ?: return
        val step = (right - left) / (StatsRing.CAPACITY - 1)
        linePath.reset()
        fillPath.reset()
        var first = true
        var lastX = right
        var firstX = right
        for (i in 0 until ring.size) {
            val v = ring.value(series, i)
            if (v < 0) continue
            val x = right - (ring.size - 1 - i) * step
            val y = lower - (lower - upper) * minOf(v, scaleMax) / scaleMax
            if (first) {
                linePath.moveTo(x, y)
                fillPath.moveTo(x, lower)
                firstX = x
                first = false
            } else {
                linePath.lineTo(x, y)
            }
            fillPath.lineTo(x, y)
            lastX = x
        }
        if (first) return
        fillPath.lineTo(lastX, lower)
        fillPath.lineTo(firstX, lower)
        fillPath.close()
        canvas.drawPath(fillPath, fill)
        canvas.drawPath(linePath, line)
    }

    private fun sp(value: Float): Float = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, metrics)

    private companion object {
        const val LINE_DP = 2f
        const val FILL_ALPHA = 40
        const val DEFAULT_HEIGHT_DP = 88f
    }
}
