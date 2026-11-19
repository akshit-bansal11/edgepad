package me.akshitbansal.edgepad.surface

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Typeface
import android.text.TextPaint
import android.text.TextUtils
import android.text.format.DateFormat
import android.util.TypedValue
import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.StatsFormat
import me.akshitbansal.edgepad.link.StatsRing
import me.akshitbansal.edgepad.protocol.AgentToken
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.screens.LimitWindowLabel
import me.akshitbansal.edgepad.screens.words
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Draws the widgets that are switched on, behind the trackpad and at about half strength, in the surface's own ink and
 * accent. They take no touches: the surface's touch code never asks about them, so a finger over one moves the pointer
 * as it would anywhere else.
 *
 * Nothing is made while drawing. [update] and [tickClock] build every string, and [layout] fits it to the box, so
 * [draw] only places text, arcs and one reused [Path]. The surface calls [update] when the laptop sent something, and
 * the clock follows the system's minute tick for as long as the surface is attached ([attach]); no widget keeps a timer
 * of its own, and a widget that is off costs nothing.
 */
class WidgetPainter(
    private val context: Context,
    settings: Settings,
    private val ink: Int,
    private val accent: Int,
    enabled: Boolean,
) {
    private class Slot(
        val widget: SurfaceWidget,
        val at: Pair<Float, Float>,
    ) {
        val box = RectF()
        val ring = RectF()

        /** Shows [primary] alone, as a note: the laptop cannot provide this widget. */
        var note = false
        var primary = ""
        var secondary = ""
        var compact = ""
        var shownPrimary = ""
        var shownSecondary = ""
        var shownCompact = ""

        /** The series the sparkline draws, or -1, and the top of its scale (0 = the running maximum). */
        var series = -1
        var fixedMax = 0
        var latest = Int.MIN_VALUE

        /** The ring's percent, or -1. */
        var pct = -1
    }

    private val metrics = context.resources.displayMetrics
    private val density = metrics.density
    private val slots =
        if (enabled) {
            SurfaceWidget.entries.filter(settings::widgetOn).map { Slot(it, settings.widget(it)) }
        } else {
            emptyList()
        }
    private val primaryInk = ink and RGB_MASK or (STRONG_ALPHA shl ALPHA_SHIFT)
    private val secondaryInk = ink and RGB_MASK or (SOFT_ALPHA shl ALPHA_SHIFT)
    private val lineInk = accent and RGB_MASK or (STRONG_ALPHA shl ALPHA_SHIFT)
    private val trackInk = ink and RGB_MASK or (TRACK_ALPHA shl ALPHA_SHIFT)

    private val bigPaint = text(Type.black, Type.LARGE_TITLE, primaryInk)
    private val titlePaint = text(Type.black, Type.TITLE, primaryInk)
    private val valuePaint = text(Type.bold, Type.VALUE, primaryInk)
    private val smallPaint = text(Type.face, Type.SMALL, secondaryInk)
    private val bigMetrics = bigPaint.fontMetrics
    private val titleMetrics = titlePaint.fontMetrics
    private val valueMetrics = valuePaint.fontMetrics
    private val smallMetrics = smallPaint.fontMetrics
    private val stroke =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeCap = Paint.Cap.ROUND
            strokeJoin = Paint.Join.ROUND
            strokeWidth = dp(LINE_DP)
        }
    private val path = Path()

    private val none = context.getString(R.string.sys_none)
    private val fmt = StatsFormat(none)
    private val needLaptop = context.getString(R.string.widget_need_laptop)
    private val cpu = context.getString(R.string.sys_cpu)
    private val ram = context.getString(R.string.sys_ram)
    private val gpu = context.getString(R.string.sys_gpu)
    private val fahrenheit = Locale.getDefault().country in FAHRENHEIT
    private var stats: StatsRing? = null

    private var host: View? = null
    private val tickFilter =
        IntentFilter().apply {
            addAction(Intent.ACTION_TIME_TICK)
            addAction(Intent.ACTION_TIME_CHANGED)
            addAction(Intent.ACTION_TIMEZONE_CHANGED)
        }
    private val tick =
        object : BroadcastReceiver() {
            override fun onReceive(
                receiverContext: Context,
                intent: Intent,
            ) {
                if (tickClock()) host?.invalidate()
            }
        }

    init {
        tickClock()
    }

    /** Whether any widget is on, so the surface can skip all of this when none is. */
    val active: Boolean get() = slots.isNotEmpty()

    /** Starts following the minute while the clock is on. [view] is redrawn on each tick. */
    fun attach(view: View) {
        if (slots.none { it.widget == SurfaceWidget.CLOCK } || host != null) return
        host = view
        context.registerReceiver(tick, tickFilter)
        // The minute may have turned while the surface was away.
        if (tickClock()) view.invalidate()
    }

    fun detach() {
        if (host == null) return
        host = null
        context.unregisterReceiver(tick)
    }

    /** Rebuilds the clock and the date. True when the picture changed. */
    fun tickClock(): Boolean {
        val slot = slots.firstOrNull { it.widget == SurfaceWidget.CLOCK } ?: return false
        val now = Date()
        val time = DateFormat.getTimeFormat(context).format(now)
        val pattern = DateFormat.getBestDateTimePattern(Locale.getDefault(), DATE_SKELETON)
        val date = SimpleDateFormat(pattern, Locale.getDefault()).format(now)
        return set(slot, time, date, time)
    }

    /** Takes what the laptop last said. True when something a visible widget shows changed. */
    fun update(state: LaptopState): Boolean {
        stats = state.stats
        var changed = false
        for (slot in slots) {
            changed = changed or
                when (slot.widget) {
                    SurfaceWidget.CLOCK -> false
                    SurfaceWidget.WEATHER -> weather(slot, state)
                    SurfaceWidget.SYSTEM -> system(slot, state)
                    SurfaceWidget.NETWORK -> network(slot, state)
                    SurfaceWidget.AI -> ai(slot, state)
                }
        }
        return changed
    }

    private fun weather(
        slot: Slot,
        state: LaptopState,
    ): Boolean {
        val weather = state.weather
        if (weather == null) return note(slot, if (Feature.WEATHER in state.features) none else needLaptop)
        val tenths = weather.tempTenthsC
        val degrees = if (fahrenheit) Math.round(tenths * 9 / 50f + 32) else Math.round(tenths / 10f)
        val temp = "$degrees°${if (fahrenheit) "F" else "C"}"
        val sky = condition(weather.wmoCode)?.let { context.getString(it) }.orEmpty()
        val detail = listOf(sky, weather.city).filter { it.isNotEmpty() }.joinToString(" · ")
        return set(slot, temp, detail, if (detail.isEmpty()) temp else "$temp · $detail")
    }

    private fun system(
        slot: Slot,
        state: LaptopState,
    ): Boolean {
        if (Feature.STATS !in state.features) return note(slot, needLaptop)
        val s = state.stats
        val line =
            listOf(
                cpu to s.latest(StatsRing.CPU),
                ram to s.latest(StatsRing.RAM),
                gpu to s.latest(StatsRing.GPU),
            ).filter { it.second >= 0 || it.first == cpu }
                .joinToString(" · ") { "${it.first} ${fmt.percent(it.second)}" }
        slot.series = StatsRing.CPU
        slot.fixedMax = FULL
        return set(slot, line, "", line) or sparked(slot, s)
    }

    private fun network(
        slot: Slot,
        state: LaptopState,
    ): Boolean {
        if (Feature.STATS !in state.features) return note(slot, needLaptop)
        val s = state.stats
        val line = "↓ ${fmt.rate(s.latest(StatsRing.DOWN))} · ↑ ${fmt.rate(s.latest(StatsRing.UP))}"
        slot.series = StatsRing.DOWN
        slot.fixedMax = 0
        return set(slot, line, "", line) or sparked(slot, s)
    }

    private fun ai(
        slot: Slot,
        state: LaptopState,
    ): Boolean {
        val agents = AGENTS.filter { it.second in state.features }.map { it.first }
        if (agents.isEmpty()) return note(slot, needLaptop)
        val entry =
            agents.firstNotNullOfOrNull { agent ->
                state.limits.filter { it.agent == agent }.minWithOrNull(LimitWindowLabel.shortestFirst)
            } ?: return note(slot, none)
        val agentName = if (entry.agent == AgentToken.CLAUDE) R.string.agent_claude else R.string.agent_codex
        val name = context.getString(agentName)
        val label = LimitWindowLabel.of(entry.window).words(context)
        val moved = slot.pct != entry.pct
        slot.pct = entry.pct
        return set(slot, "${entry.pct}%", "$name · $label", "$name $label ${entry.pct}%") or moved
    }

    /** True when the newest sample of the widget's series is not the one drawn last. */
    private fun sparked(
        slot: Slot,
        ring: StatsRing,
    ): Boolean {
        val latest = ring.latest(slot.series)
        val moved = latest != slot.latest
        slot.latest = latest
        return moved
    }

    private fun note(
        slot: Slot,
        words: String,
    ): Boolean {
        val changed = !slot.note || slot.primary != words
        slot.note = true
        slot.primary = words
        slot.series = -1
        slot.pct = -1
        if (changed) fit(slot)
        return changed
    }

    private fun set(
        slot: Slot,
        primary: String,
        secondary: String,
        compact: String,
    ): Boolean {
        val changed = slot.note || slot.primary != primary || slot.secondary != secondary || slot.compact != compact
        slot.note = false
        slot.primary = primary
        slot.secondary = secondary
        slot.compact = compact
        if (changed) fit(slot)
        return changed
    }

    /** Sizes and places every box for a surface [w] by [h] px. */
    fun layout(
        w: Float,
        h: Float,
    ) {
        for (slot in slots) {
            val (bw, bh) = boxSize(slot.widget, w, h, density)
            val (fx, fy) = slot.at
            // Kept on the screen whatever the fraction says.
            val cx = (fx * w).coerceIn(bw / 2, maxOf(w - bw / 2, bw / 2))
            val cy = (fy * h).coerceIn(bh / 2, maxOf(h - bh / 2, bh / 2))
            slot.box.set(cx - bw / 2, cy - bh / 2, cx + bw / 2, cy + bh / 2)
            fit(slot)
        }
    }

    private fun fit(slot: Slot) {
        val room = slot.box.width() - 2 * dp(Space.S)
        if (room <= 0f) return
        val beside = if (slot.widget == SurfaceWidget.AI) room - dp(RING_DP + Space.M) else room
        slot.shownPrimary = ellipsize(slot.primary, if (slot.note) smallPaint else primaryPaint(slot.widget), room)
        slot.shownSecondary = ellipsize(slot.secondary, smallPaint, beside)
        val compactPaint = if (slot.widget == SurfaceWidget.CLOCK) bigPaint else valuePaint
        slot.shownCompact = ellipsize(slot.compact, compactPaint, room)
    }

    private fun ellipsize(
        text: String,
        paint: TextPaint,
        room: Float,
    ): String = TextUtils.ellipsize(text, paint, room, TextUtils.TruncateAt.END).toString()

    private fun primaryPaint(widget: SurfaceWidget): TextPaint =
        when (widget) {
            SurfaceWidget.CLOCK -> bigPaint
            SurfaceWidget.WEATHER, SurfaceWidget.AI -> titlePaint
            SurfaceWidget.SYSTEM, SurfaceWidget.NETWORK -> valuePaint
        }

    private fun metricsOf(paint: TextPaint): Paint.FontMetrics =
        when (paint) {
            bigPaint -> bigMetrics
            titlePaint -> titleMetrics
            valuePaint -> valueMetrics
            else -> smallMetrics
        }

    fun draw(canvas: Canvas) {
        for (index in slots.indices) {
            val slot = slots[index]
            val box = slot.box
            if (box.isEmpty) continue
            when {
                slot.note -> {
                    centred(canvas, slot.shownPrimary, smallPaint, box.centerX(), box.centerY())
                }

                box.height() < dp(COMPACT_BELOW_DP) -> {
                    val paint = if (slot.widget == SurfaceWidget.CLOCK) bigPaint else valuePaint
                    centred(canvas, slot.shownCompact, paint, box.centerX(), box.centerY())
                }

                slot.widget == SurfaceWidget.AI -> {
                    drawAi(canvas, slot)
                }

                slot.series >= 0 -> {
                    drawSpark(canvas, slot)
                }

                else -> {
                    drawTwoLines(canvas, slot)
                }
            }
        }
    }

    /** A line of [text] with its vertical middle at [cy]. */
    private fun centred(
        canvas: Canvas,
        text: String,
        paint: TextPaint,
        cx: Float,
        cy: Float,
    ) {
        val m = metricsOf(paint)
        paint.textAlign = Paint.Align.CENTER
        canvas.drawText(text, cx, cy - (m.ascent + m.descent) / 2, paint)
    }

    /** The big line over the small one, the pair centred in the box. */
    private fun drawTwoLines(
        canvas: Canvas,
        slot: Slot,
    ) {
        val paint = primaryPaint(slot.widget)
        val m = metricsOf(paint)
        val top = slot.box.centerY() - (m.descent - m.ascent + smallMetrics.descent - smallMetrics.ascent) / 2
        paint.textAlign = Paint.Align.CENTER
        smallPaint.textAlign = Paint.Align.CENTER
        canvas.drawText(slot.shownPrimary, slot.box.centerX(), top - m.ascent, paint)
        canvas.drawText(
            slot.shownSecondary,
            slot.box.centerX(),
            top + m.descent - m.ascent - smallMetrics.ascent,
            smallPaint,
        )
    }

    /** The text line at the top of the box and the last minute of the series under it. */
    private fun drawSpark(
        canvas: Canvas,
        slot: Slot,
    ) {
        val box = slot.box
        valuePaint.textAlign = Paint.Align.CENTER
        canvas.drawText(slot.shownPrimary, box.centerX(), box.top - valueMetrics.ascent, valuePaint)
        val ring = stats ?: return
        val left = box.left + dp(Space.S)
        val right = box.right - dp(Space.S)
        val upper = box.top + valueMetrics.descent - valueMetrics.ascent + dp(Space.S)
        val lower = box.bottom - dp(Space.XS)
        if (ring.size < 2 || lower <= upper) return
        var scale = slot.fixedMax
        if (scale <= 0) {
            scale = 1
            for (i in 0 until ring.size) scale = maxOf(scale, ring.value(slot.series, i))
        }
        val step = (right - left) / (StatsRing.CAPACITY - 1)
        stroke.color = trackInk
        canvas.drawLine(left, lower, right, lower, stroke)
        path.reset()
        var pen = false
        for (i in 0 until ring.size) {
            val v = ring.value(slot.series, i)
            if (v < 0) {
                pen = false
                continue
            }
            val x = right - (ring.size - 1 - i) * step
            val y = lower - (lower - upper) * minOf(v, scale) / scale
            if (pen) path.lineTo(x, y) else path.moveTo(x, y)
            pen = true
        }
        stroke.color = lineInk
        canvas.drawPath(path, stroke)
    }

    /** A ring with the percent beside it and the agent and window under that, the pair centred in the box. */
    private fun drawAi(
        canvas: Canvas,
        slot: Slot,
    ) {
        val box = slot.box
        val diameter = minOf(box.height() * RING_SHARE, dp(RING_DP))
        val gap = dp(Space.M)
        val words = maxOf(titlePaint.measureText(slot.shownPrimary), smallPaint.measureText(slot.shownSecondary))
        val start = box.centerX() - (diameter + gap + words) / 2
        val half = stroke.strokeWidth / 2
        val ringTop = box.centerY() - diameter / 2 + half
        slot.ring.set(start + half, ringTop, start + diameter - half, ringTop + diameter - stroke.strokeWidth)
        stroke.color = trackInk
        canvas.drawArc(slot.ring, ARC_START, ARC_SWEEP, false, stroke)
        stroke.color = lineInk
        if (slot.pct > 0) canvas.drawArc(slot.ring, ARC_START, ARC_SWEEP * minOf(slot.pct, FULL) / FULL, false, stroke)
        val left = start + diameter + gap
        val block = titleMetrics.descent - titleMetrics.ascent + smallMetrics.descent - smallMetrics.ascent
        val top = box.centerY() - block / 2
        titlePaint.textAlign = Paint.Align.LEFT
        smallPaint.textAlign = Paint.Align.LEFT
        canvas.drawText(slot.shownPrimary, left, top - titleMetrics.ascent, titlePaint)
        canvas.drawText(
            slot.shownSecondary,
            left,
            top + titleMetrics.descent - titleMetrics.ascent - smallMetrics.ascent,
            smallPaint,
        )
    }

    private fun text(
        face: Typeface,
        size: Float,
        color: Int,
    ): TextPaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = face
            textSize = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, size, metrics)
            this.color = color
        }

    private fun dp(value: Float): Float = value * density

    /** The words for a WMO weather code, or null for one with none. */
    private fun condition(code: Int): Int? =
        when (code) {
            0, 1 -> R.string.wx_clear
            2, 3 -> R.string.wx_cloudy
            45, 48 -> R.string.wx_fog
            in 51..57 -> R.string.wx_drizzle
            in 61..67 -> R.string.wx_rain
            in 71..77, 85, 86 -> R.string.wx_snow
            in 80..82 -> R.string.wx_showers
            95, 96, 99 -> R.string.wx_storm
            else -> null
        }

    companion object {
        private const val RGB_MASK = 0x00FFFFFF
        private const val ALPHA_SHIFT = 24
        private const val STRONG_ALPHA = 0x8C
        private const val SOFT_ALPHA = 0x66
        private const val TRACK_ALPHA = 0x30
        private const val FULL = 100
        private const val LINE_DP = 2f
        private const val RING_DP = 56f
        private const val RING_SHARE = 0.8f
        private const val ARC_START = 135f
        private const val ARC_SWEEP = 270f
        private const val COMPACT_BELOW_DP = 56f
        private const val DATE_SKELETON = "EEEMMMd"

        /** Where the temperature is in Fahrenheit: the United States and the few places that follow it. */
        private val FAHRENHEIT = setOf("US", "LR", "MM", "BS", "BZ", "KY", "PW", "FM", "MH")
        private val AGENTS = listOf(AgentToken.CLAUDE to Feature.CLAUDE, AgentToken.CODEX to Feature.CODEX)

        private const val PORTRAIT_WIDTH_DP = 260f
        private const val PORTRAIT_SHARE = 0.6f
        private const val LANDSCAPE_WIDTH_DP = 220f
        private const val LANDSCAPE_SHARE = 0.3f

        /** The most of the surface's height a box may take, so a short screen gets the one-line form. */
        private const val HEIGHT_SHARE = 0.15f

        /**
         * A widget's box on a surface [w] by [h] px, in px: no wider than 260 dp or 60 percent of the width upright,
         * 220 dp or 30 percent sideways. The layout editor draws its boxes from the same numbers.
         */
        fun boxSize(
            widget: SurfaceWidget,
            w: Float,
            h: Float,
            density: Float,
        ): Pair<Float, Float> {
            val width =
                if (w > h) {
                    minOf(LANDSCAPE_WIDTH_DP * density, LANDSCAPE_SHARE * w)
                } else {
                    minOf(PORTRAIT_WIDTH_DP * density, PORTRAIT_SHARE * w)
                }
            val natural =
                when (widget) {
                    SurfaceWidget.CLOCK -> CLOCK_HEIGHT_DP
                    else -> DEFAULT_HEIGHT_DP
                }
            return width to minOf(natural * density, HEIGHT_SHARE * h)
        }

        private const val CLOCK_HEIGHT_DP = 72f
        private const val DEFAULT_HEIGHT_DP = 64f
    }
}
