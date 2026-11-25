package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.drawable.ClipDrawable
import android.text.TextUtils
import android.text.format.Formatter
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
import java.util.Locale

/** What the card's title says, from the snapshot alone. */
enum class CardTitle { RUNNING_SEND, RUNNING_RECEIVE, DONE_ALL, DONE_SOME, FAILED_ONE, FAILED_MANY }

/** Which title the transfer card shows for [s]: running one way, or how the batch ended. */
internal fun titleKind(s: XferSnapshot): CardTitle =
    when {
        s.phase != XferPhase.FINISHED -> if (s.sending) CardTitle.RUNNING_SEND else CardTitle.RUNNING_RECEIVE
        s.failed.isEmpty() -> CardTitle.DONE_ALL
        s.done > 0 -> CardTitle.DONE_SOME
        s.failed.size == 1 -> CardTitle.FAILED_ONE
        else -> CardTitle.FAILED_MANY
    }

/**
 * Which buttons the card shows: [hold] is Pause or Resume, [resume] says it reads Resume, [stop] and
 * [retry] as named.
 */
internal data class CardButtons(
    val hold: Boolean,
    val resume: Boolean,
    val stop: Boolean,
    val retry: Boolean,
)

private val UNITS = arrayOf("B", "KB", "MB", "GB", "TB")

private fun unitOf(bytes: Long): Int {
    var unit = 0
    var left = bytes
    while (left >= 1000 && unit < UNITS.size - 1) {
        left /= 1000
        unit++
    }
    return unit
}

private fun scaled(
    bytes: Long,
    unit: Int,
    locale: Locale,
): String {
    var value = bytes.toDouble()
    repeat(unit) { value /= 1000 }
    // One decimal below 100, none above or when whole: "12.4", "48", "980".
    val text = if (value >= 100 || value == Math.rint(value)) "%.0f" else "%.1f"
    return String.format(locale, text, value)
}

/**
 * [sent] and [size] bytes as the two words of "12.4 of 48 MB": the unit once, on the end, when both share it; each with
 * its own ("980 KB" of "1.2 GB") when not. Units are 1000 steps, like the system's.
 */
internal fun sizesOf(
    sent: Long,
    size: Long,
    locale: Locale = Locale.getDefault(),
): Pair<String, String> {
    val unit = unitOf(size)
    val sentUnit = unitOf(sent)
    val end = scaled(size, unit, locale) + " " + UNITS[unit]
    if (sentUnit == unit) return scaled(sent, unit, locale) to end
    return (scaled(sent, sentUnit, locale) + " " + UNITS[sentUnit]) to end
}

/** Whether the figures start with "file 2 of 5": a batch of several files, one of them begun. */
internal fun showsCount(s: XferSnapshot) = s.total > 1 && s.index > 0

/** Which of the card's buttons show for [s]: Pause or Resume and Stop while it runs, Retry once a send has failures. */
internal fun buttonsOf(s: XferSnapshot): CardButtons {
    val running = s.phase != XferPhase.FINISHED
    return CardButtons(running, running && s.paused, running, !running && s.sending && s.failed.isNotEmpty())
}

/**
 * The small card of the batch of files moving: a file icon, the file's name, a line of figures, a thin bar while it
 * moves, and three buttons: Pause or Resume, Stop, and Close, which only hides the card. After the end only Close (and
 * Try again after a failed send) is left. Its views are made once; [bind] only sets text, visibility and the bar.
 */
class XferCardView(
    private val ui: Ui,
    onCancel: () -> Unit,
    onPause: () -> Unit,
    onResume: () -> Unit,
    onRetry: () -> Unit,
    onClose: () -> Unit,
) : LinearLayout(ui.context) {
    /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
    constructor(context: Context) : this(Ui(context), {}, {}, {}, {}, {})

    private val p = ui.palette
    private val icon =
        ImageView(ui.context).apply {
            setImageResource(R.drawable.ic_file)
            importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
        }
    private val line =
        ui.text("", Type.VALUE, p.ink, face = Type.bold).apply {
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
        }
    private val detail =
        // Wraps to a second line at a large font scale rather than cutting the figures.
        ui.text("", Type.MICRO, p.dim)
    private val fillShape = ui.rounded(p.accent, BAR_DP / 2)
    private val fill = ClipDrawable(fillShape, Gravity.START, ClipDrawable.HORIZONTAL)
    private val bar =
        FrameLayout(ui.context).apply {
            background = ui.rounded(p.faint, BAR_DP / 2)
            addView(View(ui.context).apply { background = fill }, matchBoth())
        }
    private val retry = ui.button(ui.string(R.string.xfer_retry), Ui.Style.QUIET, onRetry)
    private var held = false
    private val pause = iconButton(R.drawable.ic_pause, p.ink, ICON_DP) { if (held) onResume() else onPause() }
    private val stop =
        iconButton(R.drawable.ic_stop, p.danger, STOP_DP, ui.string(R.string.xfer_stop)) { onCancel() }
    private val close = iconButton(R.drawable.ic_close, p.ink, ICON_DP, ui.string(R.string.xfer_close)) { onClose() }

    private var lastKind: CardTitle? = null
    private var lastHeld: Boolean? = null

    init {
        orientation = HORIZONTAL
        gravity = Gravity.CENTER_VERTICAL
        background = ui.rounded(p.popover, Space.CARD_RADIUS)
        elevation = ui.dp(ELEVATION_DP).toFloat()
        setPadding(ui.dp(PAD_START_DP), ui.dp(Space.M), ui.dp(Space.XS), ui.dp(Space.M))
        accessibilityPaneTitle = ui.string(R.string.xfer_card_a11y)
        addView(icon, LayoutParams(ui.dp(ICON_DP), ui.dp(ICON_DP)))
        val words =
            LinearLayout(ui.context).apply {
                orientation = VERTICAL
                addView(line)
                addView(detail)
                val barParams = LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(BAR_DP))
                barParams.topMargin = ui.dp(Space.XS)
                addView(bar, barParams)
                addView(retry)
            }
        addView(
            words,
            LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { marginStart = ui.dp(GAP_DP) },
        )
        for (button in listOf(pause, stop, close)) {
            addView(button, LayoutParams(ui.dp(BUTTON_W_DP), ui.dp(Space.TOUCH)))
        }
    }

    /** Shows [s]. */
    fun bind(s: XferSnapshot) {
        val kind = titleKind(s)
        val running = kind == CardTitle.RUNNING_SEND || kind == CardTitle.RUNNING_RECEIVE
        val buttons = buttonsOf(s)
        val hold = buttons.resume
        if (kind != lastKind || hold != lastHeld) {
            lastKind = kind
            lastHeld = hold
            held = hold
            val bad = kind == CardTitle.FAILED_ONE || kind == CardTitle.FAILED_MANY || kind == CardTitle.DONE_SOME
            icon.setColorFilter(
                if (bad) {
                    p.danger
                } else if (hold) {
                    p.dim
                } else {
                    p.accent
                },
            )
            fillShape.setColor(if (hold) p.track else p.accent)
            pause.setImageResource(if (hold) R.drawable.ic_play else R.drawable.ic_pause)
            pause.contentDescription = ui.string(if (hold) R.string.xfer_resume else R.string.xfer_pause)
        }
        // The words follow every percent, but only the end is read aloud.
        line.accessibilityLiveRegion =
            if (running) View.ACCESSIBILITY_LIVE_REGION_NONE else View.ACCESSIBILITY_LIVE_REGION_POLITE
        val text = if (running && s.cancelling) ui.string(R.string.xfer_card_cancelling) else lineOf(kind, s)
        if (line.text.toString() != text) line.text = text
        val figures = if (running) detailOf(s, hold) else ""
        if (detail.text.toString() != figures) detail.text = figures
        show(detail, running)
        show(bar, running)
        show(pause, buttons.hold)
        show(stop, buttons.stop)
        show(retry, buttons.retry)
        pause.isEnabled = !s.cancelling
        stop.isEnabled = !s.cancelling
        if (running) fill.level = s.percent * LEVEL_PER_PERCENT
    }

    private fun detailOf(
        s: XferSnapshot,
        hold: Boolean,
    ): String {
        val (sent, size) = sizesOf(s.sent, s.size)
        val figures =
            if (hold) {
                ui.string(R.string.xfer_detail_paused, sent, size)
            } else {
                ui.string(R.string.xfer_detail, sent, size, Formatter.formatShortFileSize(ui.context, s.bytesPerSecond))
            }
        return if (showsCount(s)) ui.string(R.string.xfer_detail_count, s.index, s.total, figures) else figures
    }

    /** A button 48 dp wide in a 48 dp tall touch target, its [icon] [sizeDp] square in [color]. */
    private fun iconButton(
        drawable: Int,
        color: Int,
        sizeDp: Float,
        description: String? = null,
        onTap: () -> Unit,
    ) = ImageView(ui.context).apply {
        setImageResource(drawable)
        setColorFilter(color)
        scaleType = ImageView.ScaleType.CENTER_INSIDE
        val side = ui.dp((BUTTON_W_DP - sizeDp) / 2)
        val top = ui.dp((Space.TOUCH - sizeDp) / 2)
        setPadding(side, top, side, top)
        contentDescription = description
        ui.tappable(this, Space.S, onTap)
    }

    private fun lineOf(
        kind: CardTitle,
        s: XferSnapshot,
    ): String {
        val res = ui.context.resources
        val name = middleTruncated(s.name)
        return when (kind) {
            CardTitle.RUNNING_SEND, CardTitle.RUNNING_RECEIVE -> {
                // The name alone: the figures are the line under it. Before a file is named, the old words.
                val send = kind == CardTitle.RUNNING_SEND
                name.ifEmpty {
                    ui.string(
                        if (send) R.string.xfer_sending else R.string.xfer_receiving,
                        name,
                        s.percent,
                    )
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
        const val STOP_DP = 18f
        const val BUTTON_W_DP = 48f
        const val PAD_START_DP = 14f
        const val GAP_DP = 10f
        const val BAR_DP = 4f
        const val ELEVATION_DP = 8f

        /** A ClipDrawable's level runs to 10000. */
        const val LEVEL_PER_PERCENT = 100
    }
}
