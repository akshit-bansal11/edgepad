package me.akshitbansal.edgepad.screens

import android.animation.ValueAnimator
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.PopupWindow
import me.akshitbansal.edgepad.Motion
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.link.xfer.XferPhase
import me.akshitbansal.edgepad.link.xfer.XferSnapshot

/**
 * Holds [XferCardView] in a floating window at the bottom of [owner], above the navigation bar: it never takes focus
 * or the touches outside it, so the app stays usable while a long file moves. A batch that all arrived closes itself
 * after 4 s, one with failures after 8 s; Try again is on the card while it is open. It sits at the end edge.
 */
class XferCard(
    private val ui: Ui,
    private val owner: View,
    onCancel: () -> Unit,
    onRetry: () -> Unit,
    private val onDone: () -> Unit,
) {
    private val view = XferCardView(ui, onCancel, onRetry, onDone)
    private val autoDone = Runnable { onDone() }
    private var window: PopupWindow? = null
    private var lastPhase = XferPhase.IDLE

    /** Shows [s], opening the window when it is not open; an idle snapshot closes it. */
    fun show(
        s: XferSnapshot,
        laptopName: String,
    ) {
        if (s.phase == XferPhase.IDLE) {
            hide()
            return
        }
        view.bind(s, laptopName)
        if (s.phase != lastPhase) {
            lastPhase = s.phase
            owner.removeCallbacks(autoDone)
            if (s.phase == XferPhase.FINISHED) {
                val ok = titleKind(s) == CardTitle.DONE_ALL
                owner.postDelayed(autoDone, if (ok) DONE_MS else FAILED_MS)
            }
        }
        if (window?.isShowing != true) open()
    }

    fun hide() {
        owner.removeCallbacks(autoDone)
        lastPhase = XferPhase.IDLE
        window?.dismiss()
        window = null
    }

    private fun open() {
        if (!owner.isAttachedToWindow) return
        val bars =
            owner.rootWindowInsets?.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
        val width =
            minOf(
                owner.width - (bars?.left ?: 0) - (bars?.right ?: 0) - 2 * ui.dp(Space.PAGE),
                ui.dp(MAX_WIDTH_DP),
            )
        (view.parent as? ViewGroup)?.removeView(view)
        val popup =
            PopupWindow(view, width, ViewGroup.LayoutParams.WRAP_CONTENT, false).apply {
                isOutsideTouchable = false
                setBackgroundDrawable(ui.rounded(ui.palette.card, Space.CARD_RADIUS))
                elevation = ui.dp(ELEVATION_DP).toFloat()
            }
        window = popup
        val side = maxOf(bars?.left ?: 0, bars?.right ?: 0) + ui.dp(Space.PAGE)
        val above = (bars?.bottom ?: 0) + ui.dp(Space.PAGE)
        popup.showAtLocation(owner, Gravity.BOTTOM or Gravity.END, side, above)
        if (ValueAnimator.areAnimatorsEnabled()) {
            view.alpha = 0f
            view
                .animate()
                .alpha(1f)
                .setDuration(Motion.REVEAL)
                .start()
        } else {
            view.alpha = 1f
        }
    }

    private companion object {
        const val MAX_WIDTH_DP = 320f
        const val ELEVATION_DP = 8f
        const val DONE_MS = 4_000L
        const val FAILED_MS = 8_000L
    }
}
