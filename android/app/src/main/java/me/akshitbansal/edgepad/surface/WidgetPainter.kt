package me.akshitbansal.edgepad.surface

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.RenderNode
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
import me.akshitbansal.edgepad.link.LimitEntry
import me.akshitbansal.edgepad.link.StatsFormat
import me.akshitbansal.edgepad.link.StatsRing
import me.akshitbansal.edgepad.link.StatsSample
import me.akshitbansal.edgepad.link.WeatherInfo
import me.akshitbansal.edgepad.protocol.AgentToken
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.screens.LimitWindowLabel
import me.akshitbansal.edgepad.screens.words
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlin.math.sin

/**
 * Draws the widgets that are switched on, behind the trackpad and at about half strength, in the surface's own ink and
 * accent. They take no touches: the surface's touch code never asks about them, so a finger over one moves the pointer
 * as it would anywhere else.
 *
 * Nothing is made while drawing. [update] and [tickClock] build every string, and [layout] fits it to the box, so
 * [draw] only places text, arcs and one reused [Path]. The surface calls [update] when the laptop sent something, and
 * the clock follows the system's minute tick for as long as the surface is attached ([attach]); no widget keeps a timer
 * of its own, and a widget that is off costs nothing. The picture is kept in a [RenderNode] and recorded again only
 * when it changed, so the surface redrawing for a touch costs the widgets one replay. A widget with nothing to show
 * draws nothing.
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
        val scale: Float,
    ) {
        /** The unscaled box; [draw] scales the canvas about its centre by [drawScale]. */
        val box = RectF()
        val ring = RectF()
        var drawScale = 1f

        /** Nothing to show (the laptop does not provide this widget, or sent nothing yet): drawn as nothing at all. */
        var empty = true
        var primary = ""
        var secondary = ""
        var compact = ""
        var shownPrimary = ""
        var shownSecondary = ""
        var shownCompact = ""

        /** The stat cards: which metrics the laptop reports, their percents as text, and the ring's version drawn. */
        val reported = BooleanArray(StatMetric.entries.size)
        val percents = Array(StatMetric.entries.size) { "" }
        var version = -1

        /** The ring's percent, or -1. */
        var pct = -1
    }

    private val metrics = context.resources.displayMetrics
    private val density = metrics.density
    private val slots =
        if (enabled) {
            SurfaceWidget.entries
                .filter(settings::widgetOn)
                .map { Slot(it, settings.widget(it), settings.widgetScale(it)) }
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
    private val cards = StatCards(context)
    private val metricNames = Array(StatMetric.entries.size) { context.getString(StatMetric.entries[it].label) }
    private val cardGap = dp(CARD_GAP_DP)
    private val cell = RectF()

    // The widgets' picture, recorded only when it changed ([dirty]), so a touch that redraws the surface replays one
    // node instead of laying out every line of text again.
    private val node = RenderNode("widgets")
    private var dirty = true

    // What [update] last read, so a call with nothing new returns before building any string.
    private var seenFeatures: Set<Feature>? = null
    private var seenWeather: WeatherInfo? = null
    private var seenLimits: List<LimitEntry>? = null
    private var seenStats = -1
    private var seenMinute = -1L

    private val fmt = StatsFormat(context.getString(R.string.sys_none))
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
        val changed = set(slot, time, date, time)
        if (changed) dirty = true
        return changed
    }

    /**
     * Takes what the laptop last said. True when something a visible widget shows changed. Returns false before
     * reading further when nothing it reads has moved since the last call: the laptop's lists are replaced, not
     * edited, when they change, so comparing them by identity is enough.
     */
    fun update(state: LaptopState): Boolean {
        val minute = System.currentTimeMillis() / MS_PER_MIN
        if (slots.isEmpty() || unchanged(state, minute)) return false
        seenFeatures = state.features
        seenWeather = state.weather
        seenLimits = state.limits
        seenStats = state.stats.version
        seenMinute = minute
        stats = state.stats
        var changed = false
        for (slot in slots) {
            changed = changed or
                when (slot.widget) {
                    SurfaceWidget.CLOCK -> false
                    SurfaceWidget.WEATHER -> weather(slot, state)
                    SurfaceWidget.SYSTEM -> system(slot, state)
                    SurfaceWidget.NETWORK -> network(slot, state)
                    SurfaceWidget.AI -> ai(slot, state, minute)
                }
        }
        if (changed) dirty = true
        return changed
    }

    private fun unchanged(
        state: LaptopState,
        minute: Long,
    ): Boolean =
        state.features === seenFeatures &&
            state.weather === seenWeather &&
            state.limits === seenLimits &&
            state.stats.version == seenStats &&
            minute == seenMinute

    private fun weather(
        slot: Slot,
        state: LaptopState,
    ): Boolean {
        val weather = state.weather ?: return hide(slot)
        val temp = temperature(weather.tempTenthsC)
        val sky = condition(weather.wmoCode)?.let { context.getString(it) }.orEmpty()
        val detail = listOf(sky, weather.city).filter { it.isNotEmpty() }.joinToString(" · ")
        return set(slot, temp, detail, if (detail.isEmpty()) temp else "$temp · $detail")
    }

    private fun temperature(tenths: Int): String {
        val degrees = if (fahrenheit) Math.round(tenths * 9 / 50f + 32) else Math.round(tenths / 10f)
        return "$degrees°${if (fahrenheit) "F" else "C"}"
    }

    private fun system(
        slot: Slot,
        state: LaptopState,
    ): Boolean {
        if (Feature.STATS !in state.features) return hide(slot)
        return cardsOf(slot, state.stats)
    }

    /** Takes the percents of [s] for the cards. True when anything they draw changed, the graph included. */
    private fun cardsOf(
        slot: Slot,
        s: StatsRing,
    ): Boolean {
        var changed = set(slot, "", "", "") or (slot.version != s.version)
        slot.version = s.version
        for (metric in StatMetric.entries) {
            val at = metric.ordinal
            val reported = metric.reported(s)
            if (slot.reported[at] != reported) changed = true
            slot.reported[at] = reported
            val percent = fmt.percent(s.latest(metric.series))
            if (slot.percents[at] != percent) changed = true
            slot.percents[at] = percent
        }
        return changed
    }

    private fun network(
        slot: Slot,
        state: LaptopState,
    ): Boolean {
        if (Feature.STATS !in state.features) return hide(slot)
        val s = state.stats
        val down = fmt.rate(s.latest(StatsRing.DOWN))
        val up = fmt.rate(s.latest(StatsRing.UP))
        return set(slot, down, up, "$down $up")
    }

    private fun ai(
        slot: Slot,
        state: LaptopState,
        minute: Long,
    ): Boolean {
        val agents = AGENTS.filter { it.second in state.features }.map { it.first }
        val entry =
            agents.firstNotNullOfOrNull { agent ->
                state.limits.filter { it.agent == agent }.minWithOrNull(LimitWindowLabel.shortestFirst)
            } ?: return hide(slot)
        val agentName = if (entry.agent == AgentToken.CLAUDE) R.string.agent_claude else R.string.agent_codex
        val name = context.getString(agentName)
        val label = LimitWindowLabel.of(entry.window).words(context)
        // A window that has reset since it was measured shows a dash, not the old use.
        val pct = entry.pctAt(minute)
        val moved = slot.pct != pct
        slot.pct = pct
        val shown = fmt.percent(pct)
        return set(slot, shown, "$name · $label", "$name $label $shown") or moved
    }

    /** Draws [slot] as nothing. True when it was showing something. */
    private fun hide(slot: Slot): Boolean {
        val changed = !slot.empty
        slot.empty = true
        slot.version = -1
        slot.pct = -1
        return changed
    }

    private fun set(
        slot: Slot,
        primary: String,
        secondary: String,
        compact: String,
    ): Boolean {
        val changed = slot.empty || slot.primary != primary || slot.secondary != secondary || slot.compact != compact
        slot.empty = false
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
        node.setPosition(0, 0, w.toInt(), h.toInt())
        dirty = true
        for (slot in slots) {
            val (bw, bh) = boxSize(slot.widget, w, h, density)
            val s = fitScale(slot.scale, bw, bh, w, h)
            val (fx, fy) = slot.at
            // Kept on the screen whatever the fraction says, at the size it is drawn.
            val halfW = bw * s / 2
            val halfH = bh * s / 2
            val cx = (fx * w).coerceIn(halfW, maxOf(w - halfW, halfW))
            val cy = (fy * h).coerceIn(halfH, maxOf(h - halfH, halfH))
            slot.box.set(cx - bw / 2, cy - bh / 2, cx + bw / 2, cy + bh / 2)
            slot.drawScale = s
            fit(slot)
        }
    }

    private fun fit(slot: Slot) {
        val room = slot.box.width() - 2 * dp(Space.S)
        if (room <= 0f) return
        val beside = if (slot.widget == SurfaceWidget.AI) room - dp(RING_DP + Space.M) else room
        slot.shownPrimary = ellipsize(slot.primary, primaryPaint(slot.widget), room)
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

    /**
     * Replays the recorded picture, recording it again first only when something changed. A canvas that is not
     * hardware accelerated (a software snapshot) cannot replay a node and is painted directly.
     */
    fun draw(canvas: Canvas) {
        if (!canvas.isHardwareAccelerated) {
            paint(canvas)
            return
        }
        if (dirty || !node.hasDisplayList()) {
            val recording = node.beginRecording()
            try {
                paint(recording)
            } finally {
                node.endRecording()
            }
            dirty = false
        }
        canvas.drawRenderNode(node)
    }

    private fun paint(canvas: Canvas) {
        for (slot in slots) paintSlot(canvas, slot)
    }

    private fun paintSlot(
        canvas: Canvas,
        slot: Slot,
    ) {
        val box = slot.box
        if (box.isEmpty || slot.empty) return
        val s = slot.drawScale
        if (s != 1f) {
            canvas.save()
            canvas.scale(s, s, box.centerX(), box.centerY())
        }
        when {
            slot.widget == SurfaceWidget.SYSTEM -> {
                drawCards(canvas, slot)
            }

            slot.widget == SurfaceWidget.NETWORK -> {
                drawRates(canvas, slot)
            }

            box.height() < dp(COMPACT_BELOW_DP) -> {
                val paint = if (slot.widget == SurfaceWidget.CLOCK) bigPaint else valuePaint
                centred(canvas, slot.shownCompact, paint, box.centerX(), box.centerY())
            }

            slot.widget == SurfaceWidget.AI -> {
                drawAi(canvas, slot)
            }

            else -> {
                drawTwoLines(canvas, slot)
            }
        }
        if (s != 1f) canvas.restore()
    }

    /**
     * The layout editor's picture of [widget] centred on [cx], [cy] on a surface [w] by [h] px at [scale], over the
     * values [sample] made. It is drawn through the same code as the surface, so its footprint is the real one.
     */
    fun drawAt(
        canvas: Canvas,
        widget: SurfaceWidget,
        cx: Float,
        cy: Float,
        w: Float,
        h: Float,
        scale: Float,
    ) {
        val slot = slots.firstOrNull { it.widget == widget } ?: return
        val (bw, bh) = baseSize(widget, w, h, density)
        val widthChanged = slot.box.width() != bw
        slot.box.set(cx - bw / 2, cy - bh / 2, cx + bw / 2, cy + bh / 2)
        slot.drawScale = fitScale(scale, bw, bh, w, h)
        if (widthChanged) fit(slot)
        paintSlot(canvas, slot)
    }

    /** Fills every widget with made-up values, for the layout editor. */
    fun sample() {
        tickClock()
        val ring = StatsRing()
        for (i in 0 until StatsRing.CAPACITY) {
            val wave = SAMPLE_BASE + (SAMPLE_SWING * sin(i * SAMPLE_STEP)).toInt()
            ring.push(StatsSample(wave, wave + 8, wave - 6, wave + 3, 1200, 300, 20, 0, 55, 50, 90))
        }
        stats = ring
        for (slot in slots) {
            when (slot.widget) {
                SurfaceWidget.CLOCK -> {}

                SurfaceWidget.WEATHER -> {
                    val sky = context.getString(R.string.wx_clear)
                    set(slot, temperature(SAMPLE_TENTHS_C), sky, "${temperature(SAMPLE_TENTHS_C)} · $sky")
                }

                SurfaceWidget.SYSTEM -> {
                    cardsOf(slot, ring)
                }

                SurfaceWidget.NETWORK -> {
                    val down = fmt.rate(ring.latest(StatsRing.DOWN))
                    val up = fmt.rate(ring.latest(StatsRing.UP))
                    set(slot, down, up, "$down $up")
                }

                SurfaceWidget.AI -> {
                    slot.pct = SAMPLE_PCT
                    val name = context.getString(R.string.agent_claude)
                    set(slot, fmt.percent(SAMPLE_PCT), name, "$name ${fmt.percent(SAMPLE_PCT)}")
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

    /** The metrics the laptop reports as square cards in a row, the row centred in the box. */
    private fun drawCards(
        canvas: Canvas,
        slot: Slot,
    ) {
        val box = slot.box
        val count = slot.reported.count { it }
        if (count == 0) return
        val side = minOf(box.height(), (box.width() - (count - 1) * cardGap) / count)
        var x = box.centerX() - (count * side + (count - 1) * cardGap) / 2
        val top = box.centerY() - side / 2
        for (metric in StatMetric.entries) {
            val at = metric.ordinal
            if (!slot.reported[at]) continue
            cell.set(x, top, x + side, top + side)
            cards.card(canvas, cell, metric, metricNames[at], slot.percents[at], stats, STRONG_ALPHA)
            x += side + cardGap
        }
    }

    /** The download and the upload, each an arrow in a small box with its speed, the pair centred in the box. */
    private fun drawRates(
        canvas: Canvas,
        slot: Slot,
    ) {
        val box = slot.box
        val gap = dp(Space.L)
        val first = cards.rateWidth(slot.primary)
        val total = first + gap + cards.rateWidth(slot.secondary)
        val left = box.centerX() - total / 2
        cards.rate(canvas, left, box.centerY(), false, slot.primary, STRONG_ALPHA)
        cards.rate(canvas, left + first + gap, box.centerY(), true, slot.secondary, STRONG_ALPHA)
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
        private const val MS_PER_MIN = 60_000L
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
         * 220 dp or 30 percent sideways, then times [scale] as far as the screen allows. The layout editor draws its
         * boxes from the same numbers.
         */
        fun boxSize(
            widget: SurfaceWidget,
            w: Float,
            h: Float,
            density: Float,
            scale: Float = 1f,
        ): Pair<Float, Float> {
            val (bw, bh) = baseSize(widget, w, h, density)
            val s = fitScale(scale, bw, bh, w, h)
            return bw * s to bh * s
        }

        /** [scale], made smaller where a [bw] by [bh] box would not fit on a [w] by [h] screen. */
        private fun fitScale(
            scale: Float,
            bw: Float,
            bh: Float,
            w: Float,
            h: Float,
        ): Float = if (bw <= 0f || bh <= 0f) scale else minOf(scale, w / bw, h / bh)

        private fun baseSize(
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

                    SurfaceWidget.NETWORK -> NETWORK_HEIGHT_DP

                    // Four square cards across the width, so the box is as tall as one card.
                    SurfaceWidget.SYSTEM -> (width / density - (CARDS - 1) * CARD_GAP_DP) / CARDS

                    else -> DEFAULT_HEIGHT_DP
                }
            return width to minOf(natural * density, HEIGHT_SHARE * h)
        }

        private const val CLOCK_HEIGHT_DP = 72f
        private const val DEFAULT_HEIGHT_DP = 64f
        private const val NETWORK_HEIGHT_DP = 36f
        private const val CARDS = 4
        private const val CARD_GAP_DP = 6f
        private const val SAMPLE_BASE = 50
        private const val SAMPLE_SWING = 30
        private const val SAMPLE_STEP = 0.5
        private const val SAMPLE_TENTHS_C = 210
        private const val SAMPLE_PCT = 42
    }
}
