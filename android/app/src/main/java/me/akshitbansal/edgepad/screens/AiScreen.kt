package me.akshitbansal.edgepad.screens

import android.graphics.Color
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.Logos
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.LimitEntry
import me.akshitbansal.edgepad.link.LimitWindow
import me.akshitbansal.edgepad.link.SpendEntry
import me.akshitbansal.edgepad.protocol.AgentToken
import me.akshitbansal.edgepad.protocol.SessionStateToken
import java.text.DecimalFormatSymbols
import java.text.NumberFormat
import java.util.Currency

/**
 * What the connected AI tools have used: one equal column per tool, side by side when the phone is sideways and one
 * under another when it is upright. A column is a card (logo, name, when it was measured, a ring for each limit
 * window, what was spent today and this week) and under it that tool's open sessions. A tool that reports no limits
 * says so and shows what it spent; it never gets a ring or a percentage the laptop did not send.
 *
 * Nothing here polls. The laptop sends limits, spend and sessions when they change, so the activity calls [refresh]
 * when one of those frames arrived; it rebuilds the columns inside the page and leaves the scroll position alone.
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
        val s = state()
        val shown = AiLayout.tools.filter { it.feature in s.features }
        if (shown.isEmpty()) {
            holder.addView(ui.text(ui.string(R.string.ai_setup), Type.VALUE, ui.palette.dim))
            return
        }
        val columns = AiLayout.columns(shown.size)
        val screenDp = ui.context.resources.displayMetrics.run { widthPixels / density }
        val widthDp = AiLayout.columnWidthDp(screenDp, columns, ui.landscape)
        val strip =
            LinearLayout(ui.context).apply {
                orientation = if (ui.landscape) LinearLayout.HORIZONTAL else LinearLayout.VERTICAL
            }
        shown.forEachIndexed { index, tool ->
            val column = toolColumn(s, tool, columns, widthDp)
            val params =
                if (ui.landscape) {
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                } else {
                    LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                }
            if (index > 0) {
                if (ui.landscape) params.marginStart = ui.dp(AiLayout.GAP_DP) else params.topMargin = ui.dp(Space.L)
            }
            strip.addView(column, params)
        }
        holder.addView(strip)
    }

    private fun toolColumn(
        s: LaptopState,
        tool: AiTool,
        columns: Int,
        widthDp: Float,
    ): LinearLayout {
        val column = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        val limits =
            s.limits
                .filter { it.agent == tool.agent }
                .sortedWith(LimitWindowLabel.shortestFirst)
                .take(AiLayout.MAX_RINGS)
        val spend = s.spend.firstOrNull { it.agent == tool.agent }
        val measured =
            (limits.map { it.measuredAtMin } + listOfNotNull(spend?.measuredAtMin)).filter { it >= 0 }.maxOrNull()
        val now = System.currentTimeMillis() / MS_PER_MIN
        val pad = ui.dp(AiLayout.CARD_PAD_DP)
        val card =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                background = ui.rounded(ui.palette.card, Space.CARD_RADIUS)
                setPadding(pad, ui.dp(Space.M), pad, ui.dp(Space.M))
            }
        card.addView(header(tool, measured?.let { durationText(maxOf(now - it, 0L)) }, columns))
        when {
            limits.isNotEmpty() -> {
                card.addView(rings(limits, now, AiLayout.ringDp(widthDp, limits.size)), spaced())
            }

            else -> {
                val words = if (tool.agent == AgentToken.CLAUDE) R.string.ai_need_mods else R.string.ai_limits_na
                card.addView(ui.secondary(ui.string(words), SUB_SP), spaced())
            }
        }
        if (spend != null) {
            card.addView(spendRow(spend, tool.hasLimits), spaced())
            card.addView(ui.secondary(ui.string(R.string.ai_estimate), SUB_SP), spaced(Space.S))
            if (spend.unpriced) card.addView(ui.secondary(ui.string(R.string.ai_unpriced), SUB_SP))
        }
        column.addView(card)
        val locale = ui.context.resources.configuration.locales[0]
        val label = ui.secondary(ui.string(R.string.ai_sessions).uppercase(locale), SUB_SP)
        label.typeface = Type.bold
        label.isAccessibilityHeading = true
        label.setPadding(ui.dp(Space.XS), 0, 0, 0)
        column.addView(label, spaced(Space.M))
        val rows = s.sessions.filter { it.agent == tool.agent }
        if (rows.isEmpty()) column.addView(ui.secondary(ui.string(R.string.ai_none), SUB_SP), spaced(Space.S))
        rows.forEach { entry ->
            val pill =
                SessionPill(ui.context).apply {
                    set(entry.agent, entry.title, entry.state, ui.string(stateName(entry.state)))
                }
            column.addView(pill, spaced(Space.S))
        }
        return column
    }

    private fun spaced(topDp: Float = Space.M): LinearLayout.LayoutParams =
        LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = ui.dp(topDp)
        }

    /** The logo (a letter on the brand's colour when there is no mark yet), the name, and when it was measured. */
    private fun header(
        tool: AiTool,
        age: String?,
        columns: Int,
    ): LinearLayout {
        val name = ui.string(nameOf(tool))
        val side = ui.dp(AiLayout.logoDp(columns))
        val mark = Logos.drawable(tool.logoId)
        val logo: View =
            if (mark != null) {
                ImageView(ui.context).apply {
                    setImageResource(mark)
                    contentDescription = null
                }
            } else {
                TextView(ui.context).apply {
                    text = name.take(1)
                    gravity = Gravity.CENTER
                    setTextColor(Color.WHITE)
                    typeface = Type.bold
                    textSize = Type.SMALL
                    background = ui.rounded(tool.brand, Space.XS * 2)
                }
            }
        val lines =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(ui.text(name, NAME_SP, ui.palette.ink, face = Type.bold))
                if (age != null) addView(ui.secondary(ui.string(R.string.ai_updated, age), SUB_SP))
            }
        return LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            addView(logo, LinearLayout.LayoutParams(side, side).apply { marginEnd = ui.dp(Space.S + Space.XS) })
            addView(lines, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        }
    }

    private fun rings(
        limits: List<LimitEntry>,
        now: Long,
        ringDp: Float,
    ): LinearLayout {
        val side = ui.dp(ringDp)
        val sub = AiLayout.showsReset(ringDp)
        val row = LinearLayout(ui.context)
        for (limit in limits) {
            val reset = limit.resetAtMin - now
            val known = sub && limit.resetAtMin >= 0 && reset > 0
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

    /** Today and this week side by side. A tool with limits shows an estimated price, one without shows tokens. */
    private fun spendRow(
        spend: SpendEntry,
        priced: Boolean,
    ): LinearLayout {
        val row = LinearLayout(ui.context)
        listOf(
            Triple(R.string.ai_today, spend.tokToday, spend.centsToday),
            Triple(R.string.ai_week, spend.tokWeek, spend.centsWeek),
        ).forEach { (label, tokens, cents) ->
            val count = ui.string(R.string.ai_tokens, CompactNumber.format(tokens, decimal))
            val value = if (priced) "~${money.format(cents / CENTS)}" else count
            val cell =
                LinearLayout(ui.context).apply {
                    orientation = LinearLayout.VERTICAL
                    addView(ui.secondary(ui.string(label), SUB_SP))
                    addView(ui.text(value, VALUE_SP, ui.palette.ink, face = Type.bold))
                }
            row.addView(cell, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        }
        return row
    }

    private fun nameOf(tool: AiTool): Int =
        when (tool.agent) {
            AgentToken.CLAUDE -> R.string.agent_claude
            AgentToken.CODEX -> R.string.agent_codex
            AgentToken.GEMINI -> R.string.agent_gemini
            AgentToken.OPENCODE -> R.string.agent_opencode
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
        const val NAME_SP = 15f
        const val SUB_SP = 12f
        const val VALUE_SP = 13f
    }
}
