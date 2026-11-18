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
 * after a few seconds; one with failures stays until Done or Try again.
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
            if (s.phase == XferPhase.FINISHED && titleKind(s) == CardTitle.DONE_ALL) {
                owner.postDelayed(autoDone, AUTO_DONE_MS)
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
        val above = (bars?.bottom ?: 0) + ui.dp(Space.PAGE)
        popup.showAtLocation(owner, Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL, 0, above)
        if (ValueAnimator.areAnimatorsEnabled()) {
            view.alpha = 0f
            view.animate().alpha(1f).setDuration(Motion.REVEAL).start()
        } else {
            view.alpha = 1f
        }
    }

    private companion object {
        const val MAX_WIDTH_DP = 360f
        const val ELEVATION_DP = 8f
        const val AUTO_DONE_MS = 5_000L
    }
}
