package me.akshitbansal.edgepad.surface

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Typeface
import android.text.TextPaint
import android.util.TypedValue
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.StatsRing

/** The four metrics drawn as a card: its name, its series in the ring and its hue. All four are percents. */
enum class StatMetric(
    val label: Int,
    val series: Int,
    val hue: Int,
) {
    CPU(R.string.sys_cpu, StatsRing.CPU, R.color.stat_cpu),
    GPU(R.string.sys_gpu, StatsRing.GPU, R.color.stat_gpu),
    RAM(R.string.stat_ram, StatsRing.RAM, R.color.stat_ram),
    VRAM(R.string.sys_vram, StatsRing.VRAM, R.color.stat_vram),
    ;

    /** Whether the laptop reports it: the processor and memory always, the graphics card once a sample carried it. */
    fun reported(ring: StatsRing): Boolean = this == CPU || this == RAM || ring.latest(series) >= 0
}

/**
 * Draws one metric as a rounded-square card, and the network rate as an arrow in a small box with its speed. The
 * surface widgets and the System page both draw through this, so the two look the same. A card is an outline in the
 * metric's hue, the name at the top left and the current percent at the top right in that hue, and the recent history
 * as a line with a soft fill under it. Where the name and the percent do not both fit on a line, the percent drops
 * under the name. With fewer than two samples there is no graph, only the value.
 *
 * Nothing is made while drawing: the paints and the one [Path] are allocated once. Callers draw when the data changed,
 * not every frame. [alpha] (0..255) fades everything drawn, as the widgets' half strength does.
 *
 * @param surface Fills each card when it is not 0; the System page gives its card colour, a widget gives 0.
 */
class StatCards(
    context: Context,
    private val surface: Int = 0,
) {
    private val density = context.resources.displayMetrics.density
    private val metrics = context.resources.displayMetrics
    private val hues = IntArray(StatMetric.entries.size) { context.getColor(StatMetric.entries[it].hue) }
    private val netHue = context.getColor(R.color.stat_net)

    private val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
    private val stroke =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeCap = Paint.Cap.ROUND
            strokeJoin = Paint.Join.ROUND
            strokeWidth = dp(STROKE_DP)
        }
    private val nameInk = text(Type.bold)
    private val valueInk = text(Type.face)
    private val rateInk = text(Type.bold).apply { textSize = sp(Type.SMALL) }
    private val line = Path()
    private val area = Path()
    private val box = RectF()

    /** The hue of [metric], as it is on a [surface]-coloured card. */
    fun hue(metric: StatMetric): Int = hues[metric.ordinal]

    /** Draws [metric] in [at]; [label] and [value] are already formatted, [ring] is the history, or null for none. */
    fun card(
        canvas: Canvas,
        at: RectF,
        metric: StatMetric,
        label: String,
        value: String,
        ring: StatsRing?,
        alpha: Int = OPAQUE,
    ) {
        val hue = faded(hue(metric), alpha)
        val short = minOf(at.width(), at.height())
        val radius = dp(if (short < dp(ROOMY_DP)) Space.TILE_RADIUS else Space.CARD_RADIUS)
        if (surface != 0) {
            fill.color = faded(surface, alpha)
            canvas.drawRoundRect(at, radius, radius, fill)
        }
        stroke.color = hue
        stroke.strokeWidth = dp(STROKE_DP)
        val half = stroke.strokeWidth / 2
        box.set(at.left + half, at.top + half, at.right - half, at.bottom - half)
        canvas.drawRoundRect(box, radius - half, radius - half, stroke)

        val pad = (short * PAD_SHARE).coerceIn(dp(MIN_PAD_DP), dp(Space.M))
        val size = (short * TEXT_SHARE).coerceIn(dp(MIN_TEXT_DP), sp(Type.SMALL))
        nameInk.textSize = size
        valueInk.textSize = size
        nameInk.color = hue
        valueInk.color = hue
        val left = at.left + pad
        val right = at.right - pad
        val nameMetrics = nameInk.fontMetrics
        val base = at.top + pad - nameMetrics.ascent
        val lineHeight = nameMetrics.descent - nameMetrics.ascent
        nameInk.textAlign = Paint.Align.LEFT
        canvas.drawText(label, left, base, nameInk)
        valueInk.textAlign = Paint.Align.RIGHT
        val together = nameInk.measureText(label) + dp(Space.XS) + valueInk.measureText(value) <= right - left
        var top = at.top + pad + lineHeight
        if (together) {
            canvas.drawText(value, right, base, valueInk)
        } else {
            valueInk.textAlign = Paint.Align.LEFT
            canvas.drawText(value, left, base + lineHeight, valueInk)
            top += lineHeight
        }
        spark(canvas, left, top + dp(Space.XS), right, at.bottom - pad, metric.series, ring, hue, alpha)
    }

    /** The history as a line with a soft fill, over a 0..100 scale, newest on the right. */
    private fun spark(
        canvas: Canvas,
        left: Float,
        upper: Float,
        right: Float,
        lower: Float,
        series: Int,
        ring: StatsRing?,
        hue: Int,
        alpha: Int,
    ) {
        if (ring == null || ring.size < 2 || lower - upper < dp(MIN_GRAPH_DP) || right <= left) return
        val step = (right - left) / (StatsRing.CAPACITY - 1)
        line.reset()
        area.reset()
        var first = true
        var lastX = right
        var firstX = right
        for (i in 0 until ring.size) {
            val v = ring.value(series, i)
            if (v < 0) continue
            val x = right - (ring.size - 1 - i) * step
            val y = lower - (lower - upper) * minOf(v, FULL) / FULL
            if (first) {
                line.moveTo(x, y)
                area.moveTo(x, lower)
                firstX = x
                first = false
            } else {
                line.lineTo(x, y)
            }
            area.lineTo(x, y)
            lastX = x
        }
        if (first) return
        area.lineTo(lastX, lower)
        area.lineTo(firstX, lower)
        area.close()
        fill.color = faded(hue, alpha * FILL_ALPHA / OPAQUE)
        canvas.drawPath(area, fill)
        stroke.color = hue
        stroke.strokeWidth = dp(LINE_DP)
        canvas.drawPath(line, stroke)
    }

    /** How wide [rate] draws: the arrow's box, a gap and the speed. */
    fun rateWidth(speed: String): Float = dp(ARROW_BOX_DP + Space.S) + rateInk.measureText(speed)

    /** Draws an arrow in a rounded square with [speed] after it, from [left], centred on [centreY]. */
    fun rate(
        canvas: Canvas,
        left: Float,
        centreY: Float,
        up: Boolean,
        speed: String,
        alpha: Int = OPAQUE,
    ) {
        val hue = faded(netHue, alpha)
        val side = dp(ARROW_BOX_DP)
        stroke.color = hue
        stroke.strokeWidth = dp(STROKE_DP)
        val half = stroke.strokeWidth / 2
        box.set(left + half, centreY - side / 2 + half, left + side - half, centreY + side / 2 - half)
        canvas.drawRoundRect(box, dp(Space.XS * 2), dp(Space.XS * 2), stroke)
        // The arrow: a shaft with a head of two strokes, pointing at [tip].
        val mid = left + side / 2
        val reach = side * ARROW_SHARE
        val tip = if (up) centreY - reach else centreY + reach
        val tail = if (up) centreY + reach else centreY - reach
        val head = side * HEAD_SHARE
        val back = if (up) head else -head
        line.reset()
        line.moveTo(mid, tail)
        line.lineTo(mid, tip)
        line.moveTo(mid - head, tip + back)
        line.lineTo(mid, tip)
        line.lineTo(mid + head, tip + back)
        canvas.drawPath(line, stroke)
        rateInk.color = hue
        rateInk.textAlign = Paint.Align.LEFT
        val ink = rateInk.fontMetrics
        canvas.drawText(speed, left + side + dp(Space.S), centreY - (ink.ascent + ink.descent) / 2, rateInk)
    }

    private fun text(face: Typeface): TextPaint = TextPaint(Paint.ANTI_ALIAS_FLAG).apply { typeface = face }

    private fun faded(
        color: Int,
        alpha: Int,
    ): Int = (color and RGB_MASK) or (alpha.coerceIn(0, OPAQUE) shl ALPHA_SHIFT)

    private fun dp(value: Float): Float = value * density

    private fun sp(value: Float): Float = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, metrics)

    private companion object {
        const val OPAQUE = 255
        const val RGB_MASK = 0x00FFFFFF
        const val ALPHA_SHIFT = 24
        const val FILL_ALPHA = 56
        const val FULL = 100
        const val STROKE_DP = 1.5f
        const val LINE_DP = 1.5f
        const val ROOMY_DP = 96f
        const val PAD_SHARE = 0.12f
        const val MIN_PAD_DP = 5f
        const val TEXT_SHARE = 0.2f
        const val MIN_TEXT_DP = 10f
        const val MIN_GRAPH_DP = 8f
        const val ARROW_BOX_DP = 28f
        const val ARROW_SHARE = 0.22f
        const val HEAD_SHARE = 0.16f
    }
}
