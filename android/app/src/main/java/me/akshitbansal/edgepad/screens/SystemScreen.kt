package me.akshitbansal.edgepad.screens

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

/**
 * The laptop's processor, graphics, memory, disks and network, one card each with a line graph of the last minute.
 * Upright it is one column; sideways the processor, graphics and memory are on the left and the network, ping and
 * disks on the right.
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
    onBack: () -> Unit,
) {
    private val none = ui.string(R.string.sys_none)
    private val fmt = StatsFormat(none)
    private val holder = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
    private val updaters = ArrayList<(LaptopState) -> Unit>()
    private var shape: List<Any>? = null

    /** How tall a graph is: 88 dp upright, 72 dp sideways. */
    private val graphDp = if (ui.landscape) GRAPH_LANDSCAPE_DP else GRAPH_PORTRAIT_DP

    val view: View =
        ui.page(ui.bar(ui.string(R.string.sys_title), onBack, backLabel = ui.string(R.string.back_to_surface))) {
            add(holder)
        }

    init {
        refresh()
    }

    /** Shows the newest sample. */
    fun refresh() {
        val s = state()
        val now = listOf(Feature.STATS in s.features, s.disks.map { it.label })
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
            return
        }
        // Upright the second run follows the first; sideways it starts beside it.
        val second = if (ui.landscape) 0f else Space.XL
        columns(
            first = {
                cpuCard(0f)
                gpuCard(Space.XL)
                memoryCard(Space.XL, R.string.sys_ram, StatsRing.RAM) { it.statsInfo?.ramMiB ?: -1 }
                memoryCard(Space.XL, R.string.sys_vram, StatsRing.VRAM) { it.statsInfo?.vramMiB ?: -1 }
            },
            second = {
                networkCard(second)
                pingCard(Space.XL)
                disksCard(s.disks.size)
            },
        )
    }

    private fun Column.cpuCard(top: Float) {
        metricCard(
            top,
            R.string.sys_cpu,
            sub = { it.statsInfo?.cpuName.orEmpty() },
            value = { degrees(it.stats.latest(StatsRing.CPU_TEMP)) },
            graphs = listOf(Graph(StatsRing.CPU, R.string.sys_cpu, FULL, fmt::percent)),
        )
    }

    private fun Column.gpuCard(top: Float) {
        metricCard(
            top,
            R.string.sys_gpu,
            sub = { it.statsInfo?.gpuName.orEmpty() },
            value = {
                val power = it.stats.latest(StatsRing.GPU_POWER)
                val watts = if (power < 0) none else "$power W"
                "${degrees(it.stats.latest(StatsRing.GPU_TEMP))} · $watts"
            },
            graphs = listOf(Graph(StatsRing.GPU, R.string.sys_gpu, FULL, fmt::percent)),
        )
    }

    /** Memory or VRAM: the percent as a graph and "used / total" beside the name; [totalMiB] is -1 until known. */
    private fun Column.memoryCard(
        top: Float,
        title: Int,
        series: Int,
        totalMiB: (LaptopState) -> Int,
    ) {
        metricCard(
            top,
            title,
            sub = { "" },
            value = {
                val total = totalMiB(it)
                val pct = it.stats.latest(series)
                if (total < 0 || pct < 0) none else "${fmt.gib(total * pct / FULL)} / ${fmt.gib(total)}"
            },
            graphs = listOf(Graph(series, title, FULL, fmt::percent)),
        )
    }

    private fun Column.networkCard(top: Float) {
        metricCard(
            top,
            R.string.sys_net,
            sub = { "" },
            value = { it.statsInfo?.netName.orEmpty() },
            graphs =
                listOf(
                    Graph(StatsRing.DOWN, R.string.sys_down, 0, fmt::rate),
                    Graph(StatsRing.UP, R.string.sys_up, 0, fmt::rate),
                ),
        )
    }

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
        const val MS = " ms"
        const val BAR_DP = 6f
        const val GRAPH_PORTRAIT_DP = 88f
        const val GRAPH_LANDSCAPE_DP = 72f
    }
}
