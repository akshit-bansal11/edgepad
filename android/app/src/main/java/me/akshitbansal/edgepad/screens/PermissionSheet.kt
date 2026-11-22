package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.graphics.Typeface
import android.os.SystemClock
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.PermAsk
import me.akshitbansal.edgepad.link.PermQueue
import me.akshitbansal.edgepad.link.allowEnabled
import me.akshitbansal.edgepad.protocol.AgentToken

private const val MAX_WIDTH_DP = 420f
private const val SHORT_OWNER_DP = 360f
private const val BOX_SHARE_UPRIGHT = 0.40f
private const val BOX_SHARE_SIDEWAYS = 0.45f
private const val BOX_MAX_SIDEWAYS_DP = 160f

/** Taps this soon after a sheet opens are dropped, so a double tap on one ask cannot answer the next. */
private const val ARM_MS = 500L

/**
 * An AI agent's approval as a modal over any screen: same card and window as [ClipboardSheet]. The laptop's text is
 * untrusted, so it is shown as plain text only (no links, markup or selection, left to right), whole and scrollable;
 * Allow wakes only once all of it has been on screen. Deny sits left and Allow right, a [Space.XL] apart, and Deny
 * holds the default focus. Each ask is answered at most once, through [onAnswer] with its id and the PERM_ANSWER text.
 *
 * The host calls [update] with [me.akshitbansal.edgepad.link.LaptopState.permAsks] whenever the state or the screen
 * changes, and with an empty list when the link drops; the next ask follows an answer by itself.
 */
class PermissionSheet(
    private val ui: Ui,
    private val onAnswer: (id: String, text: String) -> Unit,
) {
    private val queue = PermQueue()
    private var dialog: AlertDialog? = null
    private var shownId: String? = null

    /** The asks still unanswered, oldest first: what a notification may announce. */
    val waiting: List<PermAsk> get() = queue.asks

    val isOpen: Boolean get() = dialog?.isShowing == true

    /**
     * Matches the sheet to the laptop's [asks]: an ask that went is dismissed, and the oldest unanswered one is shown
     * over [owner]. A null or detached [owner] only updates the queue; the sheet shows on the next call with one.
     */
    fun update(
        owner: View?,
        asks: List<PermAsk>,
    ) {
        queue.sync(asks)
        val ask = queue.current
        if (ask == null || ask.id != shownId) dismiss()
        if (ask == null || isOpen) return
        if (owner == null || !owner.isAttachedToWindow || owner.width == 0) return
        show(owner, ask)
    }

    fun dismiss() {
        dialog?.dismiss()
        dialog = null
        shownId = null
    }

    private fun show(
        owner: View,
        ask: PermAsk,
    ) {
        dismiss()
        val bars =
            owner.rootWindowInsets?.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
        val width =
            minOf(
                owner.width - (bars?.left ?: 0) - (bars?.right ?: 0) - 2 * ui.dp(Space.PAGE),
                ui.dp(MAX_WIDTH_DP),
            )
        val pad = ui.dp(Space.L)
        val title = ui.string(R.string.perm_title, ui.string(agentRes(ask.agent)))
        val shownAt = SystemClock.uptimeMillis()
        var sent = false
        var allowOk = false
        lateinit var deny: TextView
        var allow: TextView? = null

        fun answer(yes: Boolean) {
            if (sent || (yes && !allowOk) || SystemClock.uptimeMillis() - shownAt < ARM_MS) return
            val text = queue.answer(ask.id, yes) ?: return
            sent = true
            ui.enable(deny, false)
            allow?.let { ui.enable(it, false) }
            onAnswer(ask.id, text)
            dismiss()
            queue.current?.let { show(owner, it) }
        }

        val list =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(pad, pad, pad, pad)
            }
        list.addView(ui.section(title).apply { setPadding(0, 0, 0, 0) })
        val tool =
            ui.text(ask.tool, Type.BODY, ui.palette.ink, face = Type.bold).apply {
                plain(this)
                setPadding(0, ui.dp(Space.S), 0, 0)
            }
        list.addView(tool)
        deny = ui.button(ui.string(R.string.perm_deny), Ui.Style.OUTLINED) { answer(false) }
        if (ask.complete) {
            val shown = ui.button(ui.string(R.string.perm_allow), Ui.Style.FILLED) { answer(true) }
            allow = shown
            val boxPad = ui.dp(Space.M)
            val text =
                ui.text(ask.text, Type.VALUE, ui.palette.ink, face = Typeface.MONOSPACE).apply {
                    plain(this)
                    setPadding(boxPad, boxPad, boxPad, boxPad)
                }
            text.measure(
                View.MeasureSpec.makeMeasureSpec(width - 2 * pad, View.MeasureSpec.EXACTLY),
                View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED),
            )
            val max = boxMax(owner)
            val fits = text.measuredHeight <= max
            val box =
                ScrollView(ui.context).apply {
                    background = ui.rounded(ui.palette.faint, Space.CARD_RADIUS)
                    isNestedScrollingEnabled = true
                    addView(text)
                }
            val boxParams =
                LinearLayout
                    .LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT,
                        if (fits) ViewGroup.LayoutParams.WRAP_CONTENT else max,
                    ).apply { topMargin = ui.dp(Space.S) }
            list.addView(box, boxParams)
            val hint =
                ui.secondary(ui.string(R.string.perm_scroll), Type.SMALL).apply {
                    setPadding(0, ui.dp(Space.XS), 0, 0)
                    visibility = if (fits) View.GONE else View.VISIBLE
                }
            list.addView(hint)
            allowOk = allowEnabled(complete = true, fits = fits, atEnd = false)
            ui.enable(shown, allowOk)
            if (!fits) {
                // At the end once nothing is left below; checked after layout too, in case the measure above was off.
                val atEnd = { v: View ->
                    if (!allowOk && !sent && !v.canScrollVertically(1)) {
                        allowOk = allowEnabled(complete = true, fits = false, atEnd = true)
                        hint.visibility = View.GONE
                        ui.enable(shown, allowOk)
                    }
                }
                box.setOnScrollChangeListener { v, _, _, _, _ -> atEnd(v) }
                box.addOnLayoutChangeListener { v, _, _, _, _, _, _, _, _ -> atEnd(v) }
            }
        } else {
            list.addView(
                ui.text(ui.string(R.string.perm_terminal), Type.BODY, ui.palette.ink).apply {
                    setPadding(0, ui.dp(Space.S), 0, 0)
                },
            )
        }
        list.addView(buttons(deny, allow), buttonParams())
        val content: View =
            if (owner.height < ui.dp(SHORT_OWNER_DP)) ScrollView(ui.context).apply { addView(list) } else list
        val opened =
            AlertDialog
                .Builder(ui.context)
                .setView(content)
                .setCancelable(false)
                .openOver(owner)
        dialog = opened
        shownId = ask.id
        opened.window?.apply {
            setTitle(title)
            setBackgroundDrawable(ui.rounded(ui.palette.card, Space.CARD_RADIUS))
            setGravity(Gravity.CENTER)
            setLayout(width, ViewGroup.LayoutParams.WRAP_CONTENT)
            Carry.reveal(this, owner)
        }
    }

    /** Deny left and Allow right in every language, so the hand learns one place for each. */
    private fun buttons(
        deny: TextView,
        allow: TextView?,
    ): LinearLayout =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.HORIZONTAL
            layoutDirection = View.LAYOUT_DIRECTION_LTR
            deny.isFocusedByDefault = true
            deny.filterTouchesWhenObscured = true
            addView(deny, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            if (allow != null) {
                allow.filterTouchesWhenObscured = true
                val params =
                    LinearLayout
                        .LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                        .apply { marginStart = ui.dp(Space.XL) }
                addView(allow, params)
            }
        }

    private fun buttonParams(): LinearLayout.LayoutParams =
        LinearLayout
            .LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { topMargin = ui.dp(Space.L) }

    /** The text box's tallest: 40 % of the owner upright; sideways 45 % and at most [BOX_MAX_SIDEWAYS_DP]. */
    private fun boxMax(owner: View): Int =
        if (ui.landscape) {
            minOf((owner.height * BOX_SHARE_SIDEWAYS).toInt(), ui.dp(BOX_MAX_SIDEWAYS_DP))
        } else {
            (owner.height * BOX_SHARE_UPRIGHT).toInt()
        }.coerceAtLeast(ui.dp(Space.TOUCH))
}

/** The agent's display name. */
private fun agentRes(agent: AgentToken): Int =
    when (agent) {
        AgentToken.CLAUDE -> R.string.agent_claude
        // Approvals come only from Claude and Codex; the other tools never send one.
        else -> R.string.agent_codex
    }

/** Laptop text as plain text only: no selection, no links, no movement method, laid out left to right. */
private fun plain(view: TextView) {
    view.setTextIsSelectable(false)
    view.autoLinkMask = 0
    view.linksClickable = false
    view.movementMethod = null
    view.isLongClickable = false
    view.textDirection = View.TEXT_DIRECTION_LTR
}
