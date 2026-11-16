package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.view.accessibility.AccessibilityNodeInfo
import android.view.accessibility.AccessibilityNodeInfo.AccessibilityAction
import android.widget.LinearLayout
import android.widget.ScrollView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.ClipboardHistory

private const val MAX_WIDTH_DP = 360f
private const val PREVIEW_CHARS = 80
private val WHITESPACE = Regex("\\s+")

/** [text] on one line, cut at [PREVIEW_CHARS] without splitting a surrogate pair. */
internal fun clipPreview(text: String): String {
    val line = text.trim().replace(WHITESPACE, " ")
    if (line.length <= PREVIEW_CHARS) return line
    val end = if (Character.isHighSurrogate(line[PREVIEW_CHARS - 1])) PREVIEW_CHARS - 1 else PREVIEW_CHARS
    return line.substring(0, end) + "…"
}

/**
 * The laptop's recent copies as a modal over the surface: same card and window as [BatterySheet], growing from
 * [show]'s owner. A tap on a row runs [onSend] with the full text and closes the sheet.
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
        val title = ui.section(ui.string(R.string.clip_history_title))
        title.setPadding(0, 0, 0, ui.dp(Space.S))
        list.addView(title)
        val items = history.items
        if (items.isEmpty()) {
            list.addView(ui.secondary(ui.string(R.string.clip_history_empty), Type.SMALL))
            return
        }
        items.forEach { list.addView(row(it)) }
        list.addView(
            ui.button(ui.string(R.string.annotate_clear), Ui.Style.QUIET) {
                history.clear()
                fill()
            },
        )
    }

    private fun row(text: String): View {
        val label = ui.string(R.string.clip_history_send_hint)
        return ui.text(clipPreview(text), Type.BODY, ui.palette.ink).apply {
            setSingleLine()
            ellipsize = TextUtils.TruncateAt.END
            minHeight = ui.dp(Space.TOUCH)
            gravity = Gravity.CENTER_VERTICAL
            ui.tappable(this, Space.CARD_RADIUS) {
                dismiss()
                onSend(text)
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
