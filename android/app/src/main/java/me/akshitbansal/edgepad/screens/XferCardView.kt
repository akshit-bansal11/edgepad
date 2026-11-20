package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.drawable.ClipDrawable
import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.xfer.XferPhase
import me.akshitbansal.edgepad.link.xfer.XferSnapshot
import me.akshitbansal.edgepad.link.xfer.middleTruncated

/** What the card's title says, from the snapshot alone. */
enum class CardTitle { RUNNING_SEND, RUNNING_RECEIVE, DONE_ALL, DONE_SOME, FAILED_ONE, FAILED_MANY }

internal fun titleKind(s: XferSnapshot): CardTitle =
    when {
        s.phase != XferPhase.FINISHED -> if (s.sending) CardTitle.RUNNING_SEND else CardTitle.RUNNING_RECEIVE
        s.failed.isEmpty() -> CardTitle.DONE_ALL
        s.done > 0 -> CardTitle.DONE_SOME
        s.failed.size == 1 -> CardTitle.FAILED_ONE
        else -> CardTitle.FAILED_MANY
    }

/**
 * The small card of the batch of files moving: a file icon, one line of words, a thin bar while it moves, and a close
 * button that cancels while it moves and dismisses after. Its views are made once; [bind] only sets text, visibility
 * and the bar's level.
 */
class XferCardView(
    private val ui: Ui,
    onCancel: () -> Unit,
    onRetry: () -> Unit,
    onDone: () -> Unit,
) : LinearLayout(ui.context) {
    /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
    constructor(context: Context) : this(Ui(context), {}, {}, {})

    private val p = ui.palette
    private val icon =
        ImageView(ui.context).apply {
            setImageResource(R.drawable.ic_file)
            importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
        }
    private val line =
        ui.text("", Type.BODY, p.ink).apply {
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
        }
    private val fill = ClipDrawable(ui.rounded(p.accent, BAR_DP), Gravity.START, ClipDrawable.HORIZONTAL)
    private val bar =
        FrameLayout(ui.context).apply {
            background = ui.rounded(p.line, BAR_DP)
            addView(View(ui.context).apply { background = fill }, matchBoth())
        }
    private val retry = ui.button(ui.string(R.string.xfer_retry), Ui.Style.QUIET, onRetry)
    private var running = false
    private val close =
        ImageView(ui.context).apply {
            setImageResource(R.drawable.ic_close)
            setColorFilter(p.dim)
            val pad = ui.dp(CLOSE_PAD_DP)
            setPadding(pad, pad, pad, pad)
            setOnClickListener { if (running) onCancel() else onDone() }
        }

    private var lastKind: CardTitle? = null

    init {
        orientation = VERTICAL
        background = ui.rounded(p.card, Space.CARD_RADIUS)
        elevation = ui.dp(ELEVATION_DP).toFloat()
        setPadding(ui.dp(Space.M), ui.dp(Space.XS), ui.dp(Space.XS), ui.dp(Space.S))
        accessibilityPaneTitle = ui.string(R.string.xfer_card_a11y)
        val top =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(icon, LayoutParams(ui.dp(ICON_DP), ui.dp(ICON_DP)))
                addView(
                    line,
                    LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                        marginStart = ui.dp(Space.M)
                        marginEnd = ui.dp(Space.S)
                    },
                )
                addView(retry)
                addView(close, LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH)))
            }
        addView(top)
        val barParams = LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(BAR_DP))
        barParams.marginEnd = ui.dp(Space.M)
        addView(bar, barParams)
    }

    /** Shows [s]. [laptopName] is not shown on the one-line card; callers still pass it. */
    @Suppress("UNUSED_PARAMETER")
    fun bind(
        s: XferSnapshot,
        laptopName: String,
    ) {
        val kind = titleKind(s)
        running = kind == CardTitle.RUNNING_SEND || kind == CardTitle.RUNNING_RECEIVE
        if (kind != lastKind) {
            lastKind = kind
            val bad = kind == CardTitle.FAILED_ONE || kind == CardTitle.FAILED_MANY || kind == CardTitle.DONE_SOME
            icon.setColorFilter(if (bad) p.danger else p.accent)
            close.contentDescription = ui.string(if (running) R.string.xfer_cancel else R.string.editor_done)
        }
        // The words follow every percent, but only the end is read aloud.
        line.accessibilityLiveRegion =
            if (running) View.ACCESSIBILITY_LIVE_REGION_NONE else View.ACCESSIBILITY_LIVE_REGION_POLITE
        val text = if (running && s.cancelling) ui.string(R.string.xfer_card_cancelling) else lineOf(kind, s)
        if (line.text.toString() != text) line.text = text
        show(bar, running)
        show(retry, !running && s.sending && s.failed.isNotEmpty())
        close.isEnabled = !(running && s.cancelling)
        if (running) fill.level = s.percent * LEVEL_PER_PERCENT
    }

    private fun lineOf(
        kind: CardTitle,
        s: XferSnapshot,
    ): String {
        val res = ui.context.resources
        val name = middleTruncated(s.name)
        return when (kind) {
            CardTitle.RUNNING_SEND, CardTitle.RUNNING_RECEIVE -> {
                val send = kind == CardTitle.RUNNING_SEND
                if (s.total > 1) {
                    val words = if (send) R.string.xfer_line_sending_files else R.string.xfer_line_receiving_files
                    ui.string(words, s.index, s.total, s.percent)
                } else {
                    ui.string(if (send) R.string.xfer_sending else R.string.xfer_receiving, name, s.percent)
                }
            }

            CardTitle.DONE_ALL -> {
                if (s.done == 1) {
                    ui.string(R.string.xfer_done, name)
                } else {
                    val plural = if (s.sending) R.plurals.xfer_card_sent else R.plurals.xfer_card_received
                    res.getQuantityString(plural, s.done, s.done)
                }
            }

            CardTitle.DONE_SOME -> {
                val total = s.done + s.failed.size
                res.getQuantityString(R.plurals.xfer_card_partial, total, s.done, total)
            }

            CardTitle.FAILED_ONE -> {
                ui.string(R.string.xfer_failed, middleTruncated(s.failed[0].name))
            }

            CardTitle.FAILED_MANY -> {
                res.getQuantityString(R.plurals.xfer_card_not_sent, s.failed.size, s.failed.size)
            }
        }
    }

    private fun show(
        view: View,
        visible: Boolean,
    ) {
        val wanted = if (visible) View.VISIBLE else View.GONE
        if (view.visibility != wanted) view.visibility = wanted
    }

    private fun matchBoth() =
        FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)

    private companion object {
        const val ICON_DP = 24f
        const val CLOSE_PAD_DP = 14f
        const val BAR_DP = 4f
        const val ELEVATION_DP = 8f

        /** A ClipDrawable's level runs to 10000. */
        const val LEVEL_PER_PERCENT = 100
    }
}
