package me.akshitbansal.edgepad.screens

import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.FrameLayout
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.PowerState
import me.akshitbansal.edgepad.protocol.PowerStateToken

/**
 * The armed restart or shut down, on whatever screen the phone shows: a line with the seconds left and Cancel, over
 * the top of the stage. The Power page is behind a Controls tile the owner can hide, so it cannot be the only place
 * the countdown is stopped. It is a plain row, not a View subclass, and ticks once a second only while it is shown;
 * a hidden banner has nothing posted.
 */
class PowerBanner(
    private val ui: Ui,
    onCancel: () -> Unit,
) {
    private val line = ui.text("", Type.BODY, ui.palette.ink)
    private var power: PowerState? = null
    private var name = ""

    private val tick =
        object : Runnable {
            override fun run() {
                draw()
            }
        }

    /** Gone until [update] has a countdown to show. */
    val view: View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            visibility = View.GONE
            background = ui.rounded(ui.palette.popover, Space.CARD_RADIUS)
            elevation = ui.dp(ELEVATION_DP).toFloat()
            setPadding(ui.dp(Space.L), 0, ui.dp(Space.XS), 0)
            accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_NONE
            addView(line, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            val cancel = ui.button(ui.string(R.string.power_cancel), Ui.Style.QUIET, onCancel)
            cancel.minHeight = ui.dp(Space.TOUCH)
            addView(cancel, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ui.dp(Space.TOUCH)))
            // Below the status bar and any cutout, whatever the screen under it does.
            setOnApplyWindowInsetsListener { v, insets ->
                val bars = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
                val params = v.layoutParams as FrameLayout.LayoutParams
                val top = bars.top + ui.dp(Space.S)
                if (params.topMargin != top) {
                    params.topMargin = top
                    v.layoutParams = params
                }
                insets
            }
        }

    /** Shows [power] for [laptop], or hides the banner for null and stops its clock. */
    fun update(
        power: PowerState?,
        laptop: String,
    ) {
        this.power = power
        name = laptop
        draw()
    }

    private fun draw() {
        view.removeCallbacks(tick)
        val now = power
        val left = now?.secondsLeft(PowerState.now()) ?: 0
        if (now == null || left == 0) {
            view.visibility = View.GONE
            return
        }
        val res = if (now.kind == PowerStateToken.RESTART) R.string.power_restarting else R.string.power_shutting_down
        val text = ui.string(res, name, left)
        if (line.text.toString() != text) line.text = text
        view.visibility = View.VISIBLE
        view.postDelayed(tick, TICK_MS)
    }

    private companion object {
        const val TICK_MS = 1000L
        const val ELEVATION_DP = 6f
    }
}
