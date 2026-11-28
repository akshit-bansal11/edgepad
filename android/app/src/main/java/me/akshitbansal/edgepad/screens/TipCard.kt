package me.akshitbansal.edgepad.screens

import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import me.akshitbansal.edgepad.Motion
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/**
 * A one-line tip over a screen, shown the first time that screen opens and never again. It replaces the help text a
 * screen used to carry: a small picture, a short line and a close button, floating near the bottom so it covers no
 * control the screen needs. Showing it marks it seen, so leaving without closing it still counts.
 */
object TipCard {
    private const val MAX_WIDTH_DP = 360f
    private const val PICTURE_DP = 36f
    private const val SLIDE_DP = 16f
    private const val ELEVATION_DP = 4f
    private const val CLOSE_PAD_DP = 12f

    /**
     * Shows the tip [key] on [host] unless it has been seen. [bottomDp], [startDp] and [endDp] keep it clear of the
     * host's own controls. Returns whether it was shown.
     */
    fun show(
        host: FrameLayout,
        settings: Settings,
        key: String,
        icon: StencilIcon.Kind,
        text: Int,
        bottomDp: Float = Space.L,
        startDp: Float = Space.L,
        endDp: Float = Space.L,
    ): Boolean {
        if (settings.tipSeen(key)) return false
        settings.setTipSeen(key)
        notice(host, icon, text, bottomDp, startDp, endDp)
        return true
    }

    /**
     * Shows [text] on [host] every time it is called: the tip card for a message the screen must give again, such as
     * what a change just turned off. [gravity] puts it at the bottom centre, or at the bottom right with
     * `Gravity.BOTTOM or Gravity.END`.
     */
    fun notice(
        host: FrameLayout,
        icon: StencilIcon.Kind,
        text: Int,
        bottomDp: Float = Space.L,
        startDp: Float = Space.L,
        endDp: Float = Space.L,
        gravity: Int = Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL,
    ) {
        val ui = Ui(host.context)
        val p = ui.palette
        val card =
            LinearLayout(host.context).apply {
                this.gravity = Gravity.CENTER_VERTICAL
                background = ui.rounded(p.popover, Space.CARD_RADIUS)
                elevation = ui.dp(ELEVATION_DP).toFloat()
                setPadding(ui.dp(Space.M), 0, 0, 0)
                alpha = 0f
                translationY = ui.dp(SLIDE_DP).toFloat()
            }
        val picture =
            ImageView(host.context).apply {
                setImageDrawable(StencilIcon(icon, p.ink, p.accent))
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            }
        val line =
            ui.text(ui.string(text), Type.SMALL, p.ink).apply {
                maxLines = 2
                accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
            }
        val close =
            ImageView(host.context).apply {
                setImageResource(R.drawable.ic_close)
                setColorFilter(p.dim)
                val pad = ui.dp(CLOSE_PAD_DP)
                setPadding(pad, pad, pad, pad)
                contentDescription = ui.string(R.string.tip_close)
                ui.tappable(this, Space.TOUCH / 2) {
                    card
                        .animate()
                        .alpha(0f)
                        .setDuration(Motion.PRESS)
                        .withEndAction { host.removeView(card) }
                        .start()
                }
            }
        val wrap = ViewGroup.LayoutParams.WRAP_CONTENT
        val size = ui.dp(PICTURE_DP)
        card.addView(picture, LinearLayout.LayoutParams(size, size))
        card.addView(line, LinearLayout.LayoutParams(0, wrap, 1f).apply { marginStart = ui.dp(Space.M) })
        card.addView(close, LinearLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH)))
        // The screen's width and not the host's, which has not been measured yet when a screen is first built.
        val room = host.resources.displayMetrics.widthPixels - ui.dp(startDp) - ui.dp(endDp)
        val at =
            FrameLayout
                .LayoutParams(minOf(room, ui.dp(MAX_WIDTH_DP)), wrap, gravity)
                .apply {
                    marginStart = ui.dp(startDp)
                    marginEnd = ui.dp(endDp)
                    bottomMargin = ui.dp(bottomDp)
                }
        host.addView(card, at)
        card
            .animate()
            .alpha(1f)
            .translationY(0f)
            .setDuration(Motion.REVEAL)
            .start()
    }
}
