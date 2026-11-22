package me.akshitbansal.edgepad.screens

import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.LimitEntry
import me.akshitbansal.edgepad.link.LimitWindow
import me.akshitbansal.edgepad.link.SpendEntry
import me.akshitbansal.edgepad.protocol.AgentToken
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.SessionStateToken
import java.text.DecimalFormatSymbols
import java.text.NumberFormat
import java.util.Currency

/**
 * What Claude Code and Codex have used: a ring for every limit window the laptop reported, what was spent today, this
 * week and this month, and the sessions that are open. Built from the same grouped cards as every other page, with the
 * two agents side by side when the phone is sideways.
 *
 * Nothing here polls. The laptop sends limits, spend and sessions when they change, so the activity calls [refresh]
 * when one of those frames arrived; it rebuilds the cards inside the page and leaves the scroll position alone.
 *
 * @param state The laptop this screen shows, read afresh on each [refresh].
 */
class AiScreen(
    private val ui: Ui,
    private val state: () -> LaptopState,
    bar: View,
) {
    private val money =
        NumberFormat.getCurrencyInstance().apply { currency = Currency.getInstance(DOLLARS) }
    private val decimal = DecimalFormatSymbols.getInstance().decimalSeparator
    private val holder = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }

    val view: View = ui.page(bar) { add(holder) }

    init {
        refresh()
    }

    /** Draws what the laptop has said so far. */
    fun refresh() {
        holder.removeAllViews()
        Column(ui, holder).render(state())
    }

    private fun Column.render(s: LaptopState) {
        val agents =
            listOf(AgentToken.CLAUDE to Feature.CLAUDE, AgentToken.CODEX to Feature.CODEX)
                .filter { it.second in s.features }
                .map { it.first }
        if (agents.isEmpty()) {
            body(ui.string(R.string.ai_setup))
            return
        }
        if (agents.size == 2) {
            columns(
                first = { agentCard(s, agents[0], 2) },
                second = { agentCard(s, agents[1], 2) },
            )
        } else {
            agentCard(s, agents[0], 1)
        }
        section(ui.string(R.string.ai_sessions))
        if (s.sessions.isEmpty()) {
            body(ui.string(R.string.ai_none))
            return
        }
        // Upright one pill a row, sideways two.
        val perRow = if (ui.landscape) 2 else 1
        s.sessions.chunked(perRow).forEachIndexed { index, chunk ->
            val row = LinearLayout(ui.context)
            chunk.forEachIndexed { at, entry ->
                val pill =
                    SessionPill(ui.context).apply {
                        set(entry.agent, entry.title, entry.state, ui.string(stateName(entry.state)))
                    }
                row.addView(
                    pill,
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                        if (at > 0) marginStart = ui.dp(Space.S)
                    },
                )
            }
            // A last row of one keeps the width of the others.
            repeat(perRow - chunk.size) {
                val gap = LinearLayout.LayoutParams(0, 0, 1f).apply { marginStart = ui.dp(Space.S) }
                row.addView(View(ui.context), gap)
            }
            add(row, if (index == 0) 0f else Space.S)
        }
    }

    /** [across] is how many agent cards share the width, which sizes the rings. */
    private fun Column.agentCard(
        s: LaptopState,
        agent: AgentToken,
        across: Int,
    ) {
        val limits =
            s.limits
                .filter { it.agent == agent }
                .sortedWith(LimitWindowLabel.shortestFirst)
                .take(MAX_RINGS)
        val spend = s.spend.firstOrNull { it.agent == agent }
        val measured =
            (limits.map { it.measuredAtMin } + listOfNotNull(spend?.measuredAtMin)).filter { it >= 0 }.maxOrNull()
        val now = System.currentTimeMillis() / MS_PER_MIN
        card {
            val sub = measured?.let { ui.secondary(ui.string(R.string.ai_updated, durationText(maxOf(now - it, 0L)))) }
            add(header(agent, sub))
            if (limits.isEmpty()) {
                val words = if (agent == AgentToken.CLAUDE) R.string.ai_need_mods else R.string.ai_no_limits
                body(ui.string(words)).setPadding(0, 0, 0, ui.dp(Space.M))
            } else {
                add(rings(limits, now, across))
            }
            if (spend != null) spendRows(spend)
        }
        if (spend != null) {
            footnote(ui.string(R.string.ai_estimate))
            if (spend.unpriced) footnote(ui.string(R.string.ai_unpriced))
        }
    }

    private fun header(
        agent: AgentToken,
        sub: View?,
    ): LinearLayout {
        val name = ui.string(if (agent == AgentToken.CLAUDE) R.string.agent_claude else R.string.agent_codex)
        val tile =
            TextView(ui.context).apply {
                text = name.take(1)
                gravity = Gravity.CENTER
                setTextColor(ui.palette.onAccent)
                typeface = Type.bold
                textSize = Type.VALUE
                background = ui.rounded(ui.palette.accent, Space.TILE_RADIUS)
            }
        val lines =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(0, ui.dp(Space.M), 0, ui.dp(Space.M))
                addView(ui.text(name, Type.BODY, ui.palette.ink, face = Type.bold))
                if (sub != null) addView(sub)
            }
        val side = ui.dp(TILE_DP)
        val start =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(tile, LinearLayout.LayoutParams(side, side).apply { marginEnd = ui.dp(Space.M) })
                addView(lines, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            }
        return ui.row(start).apply { minimumHeight = ui.dp(ROW_DP) }
    }

    private fun rings(
        limits: List<LimitEntry>,
        now: Long,
        across: Int,
    ): LinearLayout {
        val screen = ui.context.resources.displayMetrics.widthPixels
        val inner = screen - 2 * ui.dp(Space.PAGE) - 2 * ui.dp(Space.CARD_PAD)
        val share = if (across == 2 && ui.landscape) inner / 2 else inner
        val side = minOf(ui.dp(RING_DP), share / limits.size)
        val row = LinearLayout(ui.context).apply { setPadding(0, 0, 0, ui.dp(Space.M)) }
        for (limit in limits) {
            val reset = limit.resetAtMin - now
            val known = limit.resetAtMin >= 0 && reset > 0
            val resets = if (known) ui.string(R.string.ai_resets, durationText(reset)) else ""
            // A window that reset since it was measured shows a dash: its old use is no longer true.
            val ring = RingGauge(ui.context).apply { set(limit.pctAt(now), labelText(limit.window), resets) }
            val slot = FrameLayout(ui.context)
            val params = FrameLayout.LayoutParams(side, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.CENTER_HORIZONTAL)
            slot.addView(ring, params)
            row.addView(slot, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        }
        return row
    }

    private fun Column.spendRows(spend: SpendEntry) {
        val periods =
            listOf(
                Triple(R.string.ai_today, spend.tokToday, spend.centsToday),
                Triple(R.string.ai_week, spend.tokWeek, spend.centsWeek),
                Triple(R.string.ai_month, spend.tokMonth, spend.centsMonth),
            )
        for ((label, tokens, cents) in periods) {
            hairline()
            val count = ui.string(R.string.ai_tokens, CompactNumber.format(tokens, decimal))
            add(ui.field(ui.string(label), ui.secondary("$count · ~${money.format(cents / CENTS)}", Type.VALUE)))
        }
    }

    private fun labelText(window: LimitWindow): String = LimitWindowLabel.of(window).words(ui.context)

    private fun durationText(minutes: Long): String =
        LimitWindowLabel.duration(minutes.coerceAtMost(Int.MAX_VALUE.toLong()).toInt()).words(ui.context)

    private fun stateName(state: SessionStateToken): Int =
        when (state) {
            SessionStateToken.BUSY -> R.string.state_busy
            SessionStateToken.WAIT -> R.string.state_wait
            SessionStateToken.IDLE -> R.string.state_idle
        }

    private companion object {
        const val DOLLARS = "USD"
        const val CENTS = 100.0
        const val MS_PER_MIN = 60_000L
        const val MAX_RINGS = 4
        const val RING_DP = 104f
        const val TILE_DP = 36f
        const val ROW_DP = 64f
    }
}
