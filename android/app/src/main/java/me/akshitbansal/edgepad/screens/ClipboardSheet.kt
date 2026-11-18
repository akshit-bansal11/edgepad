package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.text.TextUtils
import android.text.format.DateUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.view.accessibility.AccessibilityNodeInfo
import android.view.accessibility.AccessibilityNodeInfo.AccessibilityAction
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.ClipItem
import me.akshitbansal.edgepad.link.ClipSide
import me.akshitbansal.edgepad.link.ClipboardHistory

private const val MAX_WIDTH_DP = 360f
private const val PREVIEW_CHARS = 240
private const val PREVIEW_LINES = 3
private const val EMPTY_HEIGHT_DP = 180f
private const val EMPTY_ICON_DP = 40f
private const val JUST_NOW_MS = 60_000L
private val SPACES = Regex("[ \\t]+")

/**
 * [text] with each line trimmed, runs of spaces and tabs made one space and blank lines dropped, cut at
 * [PREVIEW_CHARS] without splitting a surrogate pair. The card shows [PREVIEW_LINES] lines of it.
 */
internal fun clipPreview(text: String): String {
    val lines = text.lineSequence().map { it.trim().replace(SPACES, " ") }.filter { it.isNotEmpty() }
    val joined = lines.joinToString("\n")
    if (joined.length <= PREVIEW_CHARS) return joined
    val end = if (Character.isHighSurrogate(joined[PREVIEW_CHARS - 1])) PREVIEW_CHARS - 1 else PREVIEW_CHARS
    return joined.substring(0, end) + "…"
}

/** True while an item is under a minute old (or the clock stepped back). */
internal fun isJustNow(ageMs: Long): Boolean = ageMs < JUST_NOW_MS

/**
 * The recent copies from the laptop and this phone as a modal over the surface: same card and window as
 * [BatterySheet], growing from [show]'s owner. A tap on a card runs [onSend] with the full text and closes the sheet.
 * The text is shown, never logged.
 */
class ClipboardSheet(
    private val ui: Ui,
    private val history: ClipboardHistory,
    private val onSend: (String) -> Unit,
) {
    private var dialog: AlertDialog? = null
    private val list = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }

    val isOpen: Boolean get() = dialog?.isShowing == true

    fun show(owner: View) {
        dismiss()
        val bars =
            owner.rootWindowInsets?.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
        val width =
            minOf(
                owner.width - (bars?.left ?: 0) - (bars?.right ?: 0) - 2 * ui.dp(Space.PAGE),
                ui.dp(MAX_WIDTH_DP),
            )
        val pad = ui.dp(Space.L)
        list.setPadding(pad, pad, pad, pad)
        (list.parent as? ViewGroup)?.removeView(list)
        fill()
        val scroll = ScrollView(ui.context).apply { addView(list) }
        val shown = AlertDialog.Builder(ui.context).setView(scroll).openOver(owner)
        dialog = shown
        shown.window?.apply {
            setTitle(ui.string(R.string.clip_history_title))
            setBackgroundDrawable(ui.rounded(ui.palette.card, Space.CARD_RADIUS))
            setGravity(Gravity.CENTER)
            setLayout(width, ViewGroup.LayoutParams.WRAP_CONTENT)
            Carry.reveal(this, owner)
        }
    }

    fun dismiss() {
        dialog?.dismiss()
        dialog = null
    }

    private fun fill() {
        list.removeAllViews()
        val items = history.items
        val title = ui.section(ui.string(R.string.clip_history_title)).apply { setPadding(0, 0, 0, 0) }
        val clear =
            if (items.isEmpty()) {
                null
            } else {
                ui.button(ui.string(R.string.annotate_clear), Ui.Style.QUIET) {
                    history.clear()
                    fill()
                }
            }
        list.addView(ui.row(title, clear))
        if (items.isEmpty()) {
            list.addView(empty())
            return
        }
        val now = System.currentTimeMillis()
        items.forEach { item ->
            val params =
                LinearLayout
                    .LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                    .apply { bottomMargin = ui.dp(Space.S) }
            list.addView(card(item, now), params)
        }
    }

    private fun empty(): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            minimumHeight = ui.dp(EMPTY_HEIGHT_DP)
            val icon =
                ImageView(ui.context).apply {
                    setImageResource(R.drawable.ic_clipboard)
                    setColorFilter(ui.palette.dim)
                    importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                }
            addView(icon, LinearLayout.LayoutParams(ui.dp(EMPTY_ICON_DP), ui.dp(EMPTY_ICON_DP)))
            val head =
                ui.text(ui.string(R.string.clip_history_empty), Type.BODY, ui.palette.ink, face = Type.bold).apply {
                    gravity = Gravity.CENTER
                    setPadding(0, ui.dp(Space.S), 0, 0)
                }
            addView(head)
            val hint =
                ui.secondary(ui.string(R.string.clip_history_empty_hint), Type.SMALL).apply {
                    gravity = Gravity.CENTER
                    setPadding(0, ui.dp(Space.XS), 0, 0)
                }
            addView(hint)
        }

    private fun card(
        item: ClipItem,
        now: Long,
    ): View {
        val label = ui.string(R.string.clip_history_send_hint)
        val sideRes =
            if (item.side == ClipSide.LAPTOP) R.string.clip_history_from_laptop else R.string.clip_history_from_phone
        val age =
            if (isJustNow(now - item.at)) {
                ui.string(R.string.clip_history_just_now)
            } else {
                val flags = DateUtils.FORMAT_ABBREV_RELATIVE
                DateUtils.getRelativeTimeSpanString(item.at, now, DateUtils.MINUTE_IN_MILLIS, flags).toString()
            }
        val meta = ui.string(R.string.clip_history_meta, ui.string(sideRes), age)
        val preview = clipPreview(item.text)
        return LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            background = ui.rounded(ui.palette.faint, Space.CARD_RADIUS)
            val pad = ui.dp(Space.M)
            setPadding(pad, pad, pad, pad)
            minimumHeight = ui.dp(Space.TOUCH)
            contentDescription = ui.string(R.string.clip_history_item_desc, preview, meta)
            addView(
                ui.text(preview, Type.BODY, ui.palette.ink).apply {
                    maxLines = PREVIEW_LINES
                    ellipsize = TextUtils.TruncateAt.END
                },
            )
            addView(ui.secondary(meta, Type.SMALL).apply { setPadding(0, ui.dp(Space.XS), 0, 0) })
            ui.tappable(this, Space.CARD_RADIUS) {
                dismiss()
                onSend(item.text)
            }
            accessibilityDelegate =
                object : View.AccessibilityDelegate() {
                    override fun onInitializeAccessibilityNodeInfo(
                        host: View,
                        info: AccessibilityNodeInfo,
                    ) {
                        super.onInitializeAccessibilityNodeInfo(host, info)
                        info.addAction(AccessibilityAction(AccessibilityNodeInfo.ACTION_CLICK, label))
                    }
                }
        }
    }
}
