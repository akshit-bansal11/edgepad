package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.RectF
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.DiskEntry
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.StatsFormat
import me.akshitbansal.edgepad.link.StatsRing
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.surface.StatCards
import me.akshitbansal.edgepad.surface.StatMetric

/**
 * The Laptop page of System, under the [bar] the hub draws. The laptop's processor, graphics, memory and network as
 * rounded cards with a graph of the last minute (the same cards the widgets draw), then their details, ping and
 * disks, and the batteries of the laptop and what is paired to it. Upright it is one column of two cards a row;
 * sideways the cards (four a row) and their details are on the left and the ping and disks on the right. A graphics
 * card the laptop does not report has no card.
 *
 * Nothing here polls: the laptop sends a sample a second only while the phone says this screen is open, and the
 * activity calls [refresh] when one arrived. It sets the texts and the graphs in place, and rebuilds the cards only
 * when the shape changed: the laptop gained or lost the feature, or its list of disks did.
 *
 * @param state The laptop this screen shows, read afresh on each [refresh].
 */
class SystemScreen(
    private val ui: Ui,
    private val state: () -> LaptopState,
    bar: View,
) {
    private val none = ui.string(R.string.sys_none)
    private val fmt = StatsFormat(none)
    private val holder = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
    private val updaters = ArrayList<(LaptopState) -> Unit>()
    private var shape: List<Any>? = null

    /** How tall a graph is: 88 dp upright, 72 dp sideways. */
    private val graphDp = if (ui.landscape) GRAPH_LANDSCAPE_DP else GRAPH_PORTRAIT_DP

    val view: View = ui.page(bar) { add(holder) }

    init {
        refresh()
    }

    /** Shows the newest sample. */
    fun refresh() {
        val s = state()
        val reported = StatMetric.entries.map { it.reported(s.stats) }
        val now = listOf(Feature.STATS in s.features, s.disks.map { it.label }, reported, s.batteries)
        if (now != shape) {
            shape = now
            holder.removeAllViews()
            updaters.clear()
            Column(ui, holder).render(s)
        }
        for (update in updaters) update(s)
    }

    private fun Column.render(s: LaptopState) {
        if (Feature.STATS !in s.features) {
            card { body(ui.string(R.string.sys_off)).setPadding(0, ui.dp(Space.M), 0, ui.dp(Space.M)) }
            batteryCard(s)
            return
        }
        columns(
            first = {
                grid()
                detailsCard(s)
            },
            second = {
                pingCard(0f)
                disksCard(s.disks.size)
                batteryCard(s)
            },
        )
    }

    /** The laptop's own battery and its peripherals' (mice, headsets): a name, the charge and a bar for each. */
    private fun Column.batteryCard(s: LaptopState) {
        if (s.batteries.isEmpty()) return
        section(ui.string(R.string.battery_title))
        card {
            s.batteries.forEachIndexed { index, battery ->
                if (index > 0) hairline()
                val percent = ui.string(R.string.battery_percent, battery.level)
                val charge =
                    if (battery.charging) {
                        ui.string(R.string.net_join, percent, ui.string(R.string.battery_charging))
                    } else {
                        percent
                    }
                val low = battery.level <= LOW && !battery.charging
                add(ui.row(ui.text(battery.name, Type.BODY, ui.palette.ink), ui.secondary(charge, Type.VALUE)))
                meter(battery.name, battery.level, if (low) ui.palette.danger else ui.palette.accent)
                add(View(ui.context), height = ui.dp(Space.M))
            }
        }
    }

    /** The metrics as cards, 2 to a row upright and 4 sideways, and the network speeds under them. */
    private fun Column.grid() {
        val grid = StatGridView(ui, if (ui.landscape) LANDSCAPE_COLUMNS else PORTRAIT_COLUMNS, fmt)
        add(grid)
        updaters.add { grid.set(it.stats) }
    }

    /** What the cards leave out: the names of the processor and graphics card, the temperatures and the memory used. */
    private fun Column.detailsCard(s: LaptopState) {
        card(Space.XL) {
            StatMetric.entries.filter { it.reported(s.stats) }.forEachIndexed { index, metric ->
                if (index > 0) hairline()
                detailRow(metric)
            }
        }
    }

    private fun Column.detailRow(metric: StatMetric) {
        val title = if (metric == StatMetric.RAM) R.string.sys_ram else metric.label
        val heading = ui.text(ui.string(title), Type.BODY, ui.palette.ink)
        val subLine = ui.secondary("")
        val valueText = ui.secondary("", Type.VALUE)
        val lines =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(0, ui.dp(Space.M), 0, ui.dp(Space.M))
                addView(heading)
                addView(subLine)
            }
        add(ui.row(lines, valueText))
        updaters.add { s ->
            val info = s.statsInfo
            val sub =
                when (metric) {
                    StatMetric.CPU -> info?.cpuName
                    StatMetric.GPU -> info?.gpuName
                    else -> null
                }
            subLine.text = sub.orEmpty()
            subLine.visibility = if (sub.isNullOrEmpty()) View.GONE else View.VISIBLE
            valueText.text = detail(metric, s)
        }
    }

    private fun detail(
        metric: StatMetric,
        s: LaptopState,
    ): String =
        when (metric) {
            StatMetric.CPU -> {
                degrees(s.stats.latest(StatsRing.CPU_TEMP))
            }

            StatMetric.GPU -> {
                val power = s.stats.latest(StatsRing.GPU_POWER)
                "${degrees(s.stats.latest(StatsRing.GPU_TEMP))} · ${if (power < 0) none else "$power W"}"
            }

            StatMetric.RAM -> {
                used(s.stats.latest(StatsRing.RAM), s.statsInfo?.ramMiB ?: -1)
            }

            StatMetric.VRAM -> {
                used(s.stats.latest(StatsRing.VRAM), s.statsInfo?.vramMiB ?: -1)
            }
        }

    /** "used / total" from a percent and the total in MiB, which is -1 until known. */
    private fun used(
        pct: Int,
        totalMiB: Int,
    ): String = if (totalMiB < 0 || pct < 0) none else "${fmt.gib(totalMiB * pct / FULL)} / ${fmt.gib(totalMiB)}"

    private fun Column.pingCard(top: Float) {
        val loss = ui.secondary("", Type.VALUE)
        metricCard(
            top,
            R.string.sys_ping,
            sub = { "" },
            value = { "" },
            graphs = listOf(Graph(StatsRing.PING, R.string.sys_ping, 0, ::millis, MS)),
            extra = {
                hairline()
                add(ui.field(ui.string(R.string.sys_loss), loss))
                updaters.add { loss.text = fmt.percent(it.stats.latest(StatsRing.LOSS)) }
            },
        )
    }

    private fun Column.disksCard(count: Int) {
        if (count == 0) return
        section(ui.string(R.string.sys_disks))
        card {
            for (index in 0 until count) {
                if (index > 0) hairline()
                disk(index)
            }
        }
    }

    /** One disk: its name and what is free, with a thin bar for how full it is under them. */
    private fun Column.disk(index: Int) {
        val name = ui.text("", Type.BODY, ui.palette.ink)
        val free = ui.secondary("", Type.VALUE)
        val used = View(ui.context)
        val rest = View(ui.context)
        val track =
            LinearLayout(ui.context).apply {
                weightSum = FULL.toFloat()
                background = ui.rounded(ui.palette.track, Space.CARD_RADIUS)
                addView(used, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 0f))
                addView(rest, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, FULL.toFloat()))
            }
        add(ui.row(name, free))
        add(track, height = ui.dp(BAR_DP))
        add(View(ui.context), height = ui.dp(Space.M))
        var shown = -1
        updaters.add { s ->
            val disk: DiskEntry = s.disks.getOrNull(index) ?: return@add
            name.text = disk.label
            free.text = if (disk.freeGiB < 0) none else ui.string(R.string.sys_free, "${disk.freeGiB} GB")
            val pct = disk.usedPct.coerceIn(0, FULL)
            track.contentDescription = "${disk.label}, $pct%"
            // The bar is laid out again only when its share moved, not on every DISKS frame.
            if (pct == shown) return@add
            shown = pct
            used.background =
                ui.rounded(if (pct >= DANGER_FROM) ui.palette.danger else ui.palette.accent, Space.CARD_RADIUS)
            (used.layoutParams as LinearLayout.LayoutParams).weight = pct.toFloat()
            (rest.layoutParams as LinearLayout.LayoutParams).weight = (FULL - pct).toFloat()
            track.requestLayout()
        }
    }

    /** One graph of a card: its series, caption, the top of its scale (0 = the running maximum) and its text. */
    private class Graph(
        val series: Int,
        val caption: Int,
        val fixedMax: Int,
        val text: (Int) -> String,
        val unit: String = "",
    )

    /**
     * A card: a header row of the name, a sub-line under it and a value at the far side, then a graph for each
     * [Graph]. [extra] adds rows under the graphs.
     */
    private fun Column.metricCard(
        top: Float,
        title: Int,
        sub: (LaptopState) -> String,
        value: (LaptopState) -> String,
        graphs: List<Graph>,
        extra: (Column.() -> Unit)? = null,
    ) {
        val heading = ui.text(ui.string(title), Type.BODY, ui.palette.ink)
        val subLine = ui.secondary("")
        val valueText = ui.secondary("", Type.VALUE)
        val lines =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(0, ui.dp(Space.M), 0, ui.dp(Space.M))
                addView(heading)
                addView(subLine)
            }
        val views = graphs.map { GraphView(ui.context) }
        card(top) {
            add(ui.row(lines, valueText))
            views.forEach { graph ->
                graph.setPadding(0, 0, 0, ui.dp(Space.M))
                add(graph, height = ui.dp(graphDp + Space.M))
            }
            extra?.invoke(this)
        }
        updaters.add { s ->
            val line = sub(s)
            subLine.text = line
            subLine.visibility = if (line.isEmpty()) View.GONE else View.VISIBLE
            valueText.text = value(s)
            graphs.forEachIndexed { at, graph ->
                val latest = s.stats.latest(graph.series)
                views[at].set(
                    s.stats,
                    graph.series,
                    ui.string(graph.caption),
                    graph.text(latest),
                    graph.fixedMax,
                    graph.unit,
                    graph.unit.isNotEmpty(),
                )
            }
        }
    }

    private fun millis(v: Int): String = if (v < 0) none else "$v$MS"

    private fun degrees(v: Int): String = if (v < 0) none else "$v°C"

    private companion object {
        const val FULL = 100
        const val DANGER_FROM = 90
        const val LOW = 20
        const val MS = " ms"
        const val BAR_DP = 6f
        const val GRAPH_PORTRAIT_DP = 88f
        const val GRAPH_LANDSCAPE_DP = 72f
        const val PORTRAIT_COLUMNS = 2
        const val LANDSCAPE_COLUMNS = 4
    }
}

/**
 * The stat cards in a grid of [columns] and the two network speeds under them, drawn on one canvas through
 * [StatCards]. [set] reads the ring; it draws again only to show it, and lays out again only when a card appeared or
 * went (the graphics card is reported from the first sample that has one).
 */
private class StatGridView(
    private val ui: Ui,
    private val columns: Int,
    private val fmt: StatsFormat,
) : View(ui.context) {
    /** For layout tools only (lint ViewConstructor); the app always passes its columns and format. */
    constructor(context: Context) : this(Ui(context), 2, StatsFormat(""))

    private val cards = StatCards(ui.context, ui.palette.card)
    private val names = Array(StatMetric.entries.size) { ui.string(StatMetric.entries[it].label) }
    private val percents = Array(StatMetric.entries.size) { "" }
    private val reported = BooleanArray(StatMetric.entries.size)
    private val cell = RectF()
    private val gap = ui.dp(Space.M).toFloat()
    private var ring: StatsRing? = null
    private var shown = 0
    private var down = ""
    private var up = ""

    fun set(stats: StatsRing) {
        ring = stats
        var count = 0
        for (metric in StatMetric.entries) {
            reported[metric.ordinal] = metric.reported(stats)
            percents[metric.ordinal] = fmt.percent(stats.latest(metric.series))
            if (reported[metric.ordinal]) count++
        }
        down = fmt.rate(stats.latest(StatsRing.DOWN))
        up = fmt.rate(stats.latest(StatsRing.UP))
        contentDescription = description()
        if (count != shown) {
            shown = count
            requestLayout()
        }
        invalidate()
    }

    private fun description(): String {
        val parts = ArrayList<String>()
        for (metric in StatMetric.entries) {
            if (reported[metric.ordinal]) parts.add("${names[metric.ordinal]} ${percents[metric.ordinal]}")
        }
        parts.add("${ui.string(R.string.sys_down)} $down")
        parts.add("${ui.string(R.string.sys_up)} $up")
        return parts.joinToString(", ")
    }

    private fun cardSize(width: Int): Float = (width - (columns - 1) * gap) / columns

    private fun cardHeight(width: Int): Float = cardSize(width) * CARD_ASPECT

    override fun onMeasure(
        widthMeasureSpec: Int,
        heightMeasureSpec: Int,
    ) {
        val width = MeasureSpec.getSize(widthMeasureSpec)
        val rows = (shown + columns - 1) / columns
        val grid = if (rows == 0) 0f else rows * cardHeight(width) + (rows - 1) * gap
        val height = grid + gap + ui.dp(NET_ROW_DP)
        setMeasuredDimension(width, height.toInt())
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val size = cardSize(width)
        val high = cardHeight(width)
        var at = 0
        for (metric in StatMetric.entries) {
            if (!reported[metric.ordinal]) continue
            val x = (at % columns) * (size + gap)
            val y = (at / columns) * (high + gap)
            cell.set(x, y, x + size, y + high)
            cards.card(canvas, cell, metric, names[metric.ordinal], percents[metric.ordinal], ring)
            at++
        }
        val rows = (shown + columns - 1) / columns
        val top = if (rows == 0) 0f else rows * (high + gap)
        val centre = top + ui.dp(NET_ROW_DP) / 2f
        val left = ui.dp(Space.S).toFloat()
        val first = cards.rateWidth(down)
        cards.rate(canvas, left, centre, false, down)
        cards.rate(canvas, left + first + ui.dp(Space.XL), centre, true, up)
    }

    private companion object {
        /** A card's height as a share of its width. */
        const val CARD_ASPECT = 0.8f
        const val NET_ROW_DP = 40f
    }
}
