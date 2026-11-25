package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.LinearLayout
import android.widget.ScrollView
import me.akshitbansal.edgepad.Space

/** The widest a sheet's card grows, on a tablet or sideways. */
private const val MAX_WIDTH_DP = 360f

/**
 * A scrolling list of rows as a modal over the surface, on the same card and window as [ControlsPopup]: centred, at
 * most [MAX_WIDTH_DP] wide and never wider than the screen between its system bars, less a page margin each side.
 * Back or a tap outside closes it. [BatterySheet] and [ClipboardSheet] are the two, and each only says what its rows
 * are, in [fill].
 *
 * @param title the string resource of the window title, which is what a screen reader announces when the sheet opens.
 * @param reveal whether the window grows out of [show]'s owner through [Carry.reveal] rather than just appearing.
 */
abstract class ListSheet(
    protected val ui: Ui,
    private val title: Int,
    private val reveal: Boolean,
) {
    private var dialog: AlertDialog? = null

    /** The rows. [fill] empties and refills it; it is kept across opens and moved into each new window. */
    protected val list = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }

    val isOpen: Boolean get() = dialog?.isShowing == true

    /** Opens the sheet over [owner]'s window, closing one already open first, with its rows made fresh. */
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
            setTitle(ui.string(title))
            setBackgroundDrawable(ui.rounded(ui.palette.card, Space.CARD_RADIUS))
            setGravity(Gravity.CENTER)
            setLayout(width, ViewGroup.LayoutParams.WRAP_CONTENT)
            if (reveal) Carry.reveal(this, owner)
        }
    }

    /** Closes the sheet if it is open. */
    fun dismiss() {
        dialog?.dismiss()
        dialog = null
    }

    /** Empties [list] and adds the rows as they are now. Called on every [show], and by a subclass when they change. */
    protected abstract fun fill()
}
