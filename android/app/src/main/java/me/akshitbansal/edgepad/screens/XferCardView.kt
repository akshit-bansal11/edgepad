package me.akshitbansal.edgepad.screens

import android.graphics.drawable.ClipDrawable
import android.graphics.drawable.GradientDrawable
import android.text.format.Formatter
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.xfer.XferFailure
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

/** The sentence that says why a file did not arrive. */
internal fun reasonRes(f: XferFailure): Int =
    when (f) {
        XferFailure.REFUSED -> R.string.xfer_why_refused
        XferFailure.DAMAGED -> R.string.xfer_why_damaged
        XferFailure.WRITE_FAILED -> R.string.xfer_why_write
        XferFailure.BUSY -> R.string.xfer_why_busy
        XferFailure.TIMED_OUT -> R.string.xfer_why_timeout
        XferFailure.LINK_LOST -> R.string.xfer_why_lost
        XferFailure.UNREADABLE -> R.string.xfer_why_unreadable
        XferFailure.TOO_BIG -> R.string.xfer_why_big
        XferFailure.CANCELLED -> R.string.xfer_why_cancelled
        XferFailure.NOT_SENT -> R.string.xfer_why_skipped
    }

/**
 * The card of the batch of files moving: who to, which file of how many, a bar, and at the end what did not arrive.
 * Its views are made once; [bind] only sets text, visibility and the bar's level, so an update lays nothing out
 * unless a text's length changes.
 */
class XferCardView(
    private val ui: Ui,
    onCancel: () -> Unit,
    onRetry: () -> Unit,
    onDone: () -> Unit,
) : LinearLayout(ui.context) {
    private val p = ui.palette
    private val badge =
        ImageView(ui.context).apply {
            val pad = ui.dp(BADGE_PAD_DP)
            setPadding(pad, pad, pad, pad)
            importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
        }
    private val title = ui.text("", Type.BODY, p.ink, face = Type.bold)
    private val subtitle = ui.secondary("")
    private val fill = ClipDrawable(ui.rounded(p.accent, BAR_DP), Gravity.START, ClipDrawable.HORIZONTAL)
    private val bar =
        FrameLayout(ui.context).apply {
            background = ui.rounded(p.line, BAR_DP)
            addView(View(ui.context).apply { background = fill }, matchBoth())
        }
    private val percent = ui.secondary("")
    private val detail = ui.secondary("").apply { gravity = Gravity.END }
    private val numbers =
        LinearLayout(ui.context).apply {
            addView(percent, LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            addView(detail)
        }
    private val rows = List(MAX_ROWS + 1) { ui.secondary("") }
    private val cancel = ui.button(ui.string(R.string.xfer_cancel), Ui.Style.QUIET, onCancel)
    private val retry = ui.button(ui.string(R.string.xfer_retry), Ui.Style.FILLED, onRetry)
    private val done = ui.button(ui.string(R.string.editor_done), Ui.Style.QUIET, onDone)

    private var lastKind: CardTitle? = null
    private var lastDecile = -1

    init {
        orientation = VERTICAL
        background = ui.rounded(p.card, Space.CARD_RADIUS)
        elevation = ui.dp(ELEVATION_DP).toFloat()
        val pad = ui.dp(Space.L)
        setPadding(pad, pad, pad, pad)
        accessibilityPaneTitle = ui.string(R.string.xfer_card_a11y)
        val words =
            LinearLayout(ui.context).apply {
                orientation = VERTICAL
                addView(title)
                addView(subtitle)
            }
        val header =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(badge, LayoutParams(ui.dp(BADGE_DP), ui.dp(BADGE_DP)))
                addView(
                    words,
                    LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { marginStart = ui.dp(Space.M) },
                )
            }
        addView(header)
        addView(bar, spaced(ui.dp(BAR_DP)))
        addView(numbers, spaced(ViewGroup.LayoutParams.WRAP_CONTENT, Space.XS))
        for (row in rows) addView(row, spaced(ViewGroup.LayoutParams.WRAP_CONTENT, Space.XS))
        val actions =
            LinearLayout(ui.context).apply {
                gravity = Gravity.END
                addView(retry)
                addView(cancel)
                addView(done)
            }
        addView(actions, spaced(ViewGroup.LayoutParams.WRAP_CONTENT))
    }

    /** Shows [s]; [laptopName] is who the files go to or come from. */
    fun bind(
        s: XferSnapshot,
        laptopName: String,
    ) {
        val kind = titleKind(s)
        if (kind != lastKind) {
            lastKind = kind
            paintBadge(kind)
        }
        val running = kind == CardTitle.RUNNING_SEND || kind == CardTitle.RUNNING_RECEIVE
        set(title, titleOf(kind, s, laptopName))
        val sub = subtitleOf(kind, s)
        set(subtitle, sub)
        show(subtitle, sub.isNotEmpty())
        show(bar, running)
        show(numbers, running)
        show(cancel, running)
        show(retry, !running && s.sending && s.failed.isNotEmpty())
        show(done, !running)
        if (running) {
            fill.level = s.percent * LEVEL_PER_PERCENT
            val decile = s.percent / DECILE
            // Read aloud at most once every ten percent; the text itself follows every percent.
            percent.accessibilityLiveRegion =
                if (decile != lastDecile) {
                    View.ACCESSIBILITY_LIVE_REGION_POLITE
                } else {
                    View.ACCESSIBILITY_LIVE_REGION_NONE
                }
            lastDecile = decile
            set(percent, ui.string(R.string.xfer_card_percent, s.percent))
            set(detail, detailOf(s))
            set(cancel, ui.string(if (s.cancelling) R.string.xfer_card_cancelling else R.string.xfer_cancel))
            cancel.isEnabled = !s.cancelling
        } else {
            lastDecile = -1
        }
        bindRows(kind, s)
    }

    private fun paintBadge(kind: CardTitle) {
        val (fillColor, icon, ink) =
            when (kind) {
                CardTitle.RUNNING_SEND, CardTitle.RUNNING_RECEIVE ->
                    Triple(p.accentSoft, R.drawable.ic_laptop, p.accent)
                CardTitle.DONE_ALL -> Triple(p.accent, R.drawable.ic_check, p.onAccent)
                else -> Triple(p.danger, R.drawable.ic_close, p.onAccent)
            }
        badge.background =
            GradientDrawable().apply {
                shape = GradientDrawable.OVAL
                setColor(fillColor)
            }
        badge.setImageResource(icon)
        badge.setColorFilter(ink)
    }

    private fun titleOf(
        kind: CardTitle,
        s: XferSnapshot,
        laptopName: String,
    ): String {
        val res = ui.context.resources
        return when (kind) {
            CardTitle.RUNNING_SEND -> ui.string(R.string.xfer_card_sending_to, laptopName)
            CardTitle.RUNNING_RECEIVE -> ui.string(R.string.xfer_card_receiving_from, laptopName)
            CardTitle.DONE_ALL ->
                res.getQuantityString(
                    if (s.sending) R.plurals.xfer_card_sent else R.plurals.xfer_card_received,
                    s.done,
                    s.done,
                )
            CardTitle.DONE_SOME -> {
                val total = s.done + s.failed.size
                res.getQuantityString(R.plurals.xfer_card_partial, total, s.done, total)
            }
            CardTitle.FAILED_ONE -> ui.string(R.string.xfer_failed, middleTruncated(s.failed[0].name))
            CardTitle.FAILED_MANY -> res.getQuantityString(R.plurals.xfer_card_not_sent, s.failed.size, s.failed.size)
        }
    }

    private fun subtitleOf(
        kind: CardTitle,
        s: XferSnapshot,
    ): String {
        val name = middleTruncated(s.name)
        return when (kind) {
            CardTitle.RUNNING_SEND, CardTitle.RUNNING_RECEIVE ->
                if (s.total > 0) ui.string(R.string.xfer_card_position, s.index, s.total, name) else name
            CardTitle.FAILED_ONE -> ui.string(reasonRes(s.failed[0].why))
            else -> ""
        }
    }

    private fun detailOf(s: XferSnapshot): String {
        val bytes =
            ui.string(
                R.string.xfer_card_bytes,
                Formatter.formatFileSize(ui.context, s.sent),
                Formatter.formatFileSize(ui.context, s.size),
            )
        if (s.bytesPerSecond <= 0) return bytes
        val speed = Formatter.formatFileSize(ui.context, s.bytesPerSecond)
        return bytes + SEPARATOR + ui.string(R.string.xfer_card_speed, speed)
    }

    // Up to three files that did not arrive, then how many more; only when more than one failed.
    private fun bindRows(
        kind: CardTitle,
        s: XferSnapshot,
    ) {
        val listed = if (kind == CardTitle.DONE_SOME || kind == CardTitle.FAILED_MANY) s.failed else emptyList()
        for (i in 0 until MAX_ROWS) {
            val failed = listed.getOrNull(i)
            if (failed != null) {
                val why = ui.string(reasonRes(failed.why))
                set(rows[i], ui.string(R.string.xfer_card_row, middleTruncated(failed.name), why))
            }
            show(rows[i], failed != null)
        }
        val more = listed.size - MAX_ROWS
        if (more > 0) set(rows[MAX_ROWS], ui.string(R.string.xfer_card_more, more))
        show(rows[MAX_ROWS], more > 0)
    }

    // A text set only when it changed, so an update with the same words lays nothing out.
    private fun set(
        view: TextView,
        value: String,
    ) {
        if (view.text.toString() != value) view.text = value
    }

    private fun show(
        view: View,
        visible: Boolean,
    ) {
        val wanted = if (visible) View.VISIBLE else View.GONE
        if (view.visibility != wanted) view.visibility = wanted
    }

    private fun spaced(
        height: Int,
        topDp: Float = Space.M,
    ) = LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, height).apply { topMargin = ui.dp(topDp) }

    private fun matchBoth() =
        FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)

    private companion object {
        const val BADGE_DP = 40f
        const val BADGE_PAD_DP = 10f
        const val BAR_DP = 6f
        const val ELEVATION_DP = 8f
        const val MAX_ROWS = 3
        const val DECILE = 10

        /** A ClipDrawable's level runs to 10000. */
        const val LEVEL_PER_PERCENT = 100
        const val SEPARATOR = " · "
    }
}
