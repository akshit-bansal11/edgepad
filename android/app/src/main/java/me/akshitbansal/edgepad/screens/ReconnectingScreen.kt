package me.akshitbansal.edgepad.screens

import android.animation.ValueAnimator
import android.graphics.drawable.GradientDrawable
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.Clock

/**
 * The link dropped on its own. Says so, counts the attempts to get it back, and offers the two ways out.
 * Sideways there is little height, so the circle is smaller, the gaps tighter and the two buttons sit side
 * by side.
 *
 * A screen reader hears both halves without looking for them: the page is a pane named for the drop, which
 * is announced as it appears, and the attempt line is a live region, read out each time it changes.
 */
class ReconnectingScreen(
    private val ui: Ui,
    laptop: String,
    private val address: String,
    onRetry: () -> Unit,
    onPickAnother: () -> Unit,
) {
    enum class State { TRYING, STOPPED, OFF }

    private val dot =
        View(ui.context).apply {
            background =
                GradientDrawable().apply {
                    shape = GradientDrawable.OVAL
                    setColor(ui.palette.accent)
                }
        }
    private val progress = View(ui.context).apply { background = ui.rounded(ui.palette.accent, TRACK_DP / 2) }
    private var blink: ValueAnimator? = null
    private var trying = false
    private lateinit var attempt: TextView
    private lateinit var lastSeen: TextView

    val view: View =
        ui.page(centred = true) {
            val sideways = ui.landscape
            val gap = if (sideways) Space.L else GAP_DP
            grow()
            val ringSize = if (sideways) RING_SIDEWAYS_DP else RING_DP
            val ring =
                FrameLayout(ui.context).apply {
                    background =
                        GradientDrawable().apply {
                            shape = GradientDrawable.OVAL
                            setColor(ui.palette.accentSoft)
                        }
                    addView(dot, FrameLayout.LayoutParams(ui.dp(DOT_DP), ui.dp(DOT_DP), Gravity.CENTER))
                }
            add(ring, height = ui.dp(ringSize), width = ui.dp(ringSize))
            headline(ui.string(R.string.lost_title), Type.TITLE, gap).gravity = Gravity.CENTER
            body(ui.string(R.string.lost_body, laptop), BODY_GAP_DP).gravity = Gravity.CENTER
            attempt = add(ui.text("", Type.SMALL, ui.palette.dim, face = Type.bold), gap)
            attempt.gravity = Gravity.CENTER
            attempt.accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
            val track =
                FrameLayout(ui.context).apply {
                    background = ui.rounded(ui.palette.off, TRACK_DP / 2)
                    clipToOutline = true
                    addView(progress, FrameLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT))
                }
            add(track, BODY_GAP_DP, height = ui.dp(TRACK_DP), width = ui.dp(TRACK_WIDTH_DP))
            val retry = ui.button(ui.string(R.string.retry_now), Ui.Style.FILLED, onRetry)
            val pick = ui.button(ui.string(R.string.pick_another), Ui.Style.QUIET, onPickAnother)
            if (sideways) {
                val pair =
                    LinearLayout(ui.context).apply {
                        addView(retry, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
                        addView(
                            pick,
                            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                                marginStart = ui.dp(Space.S)
                            },
                        )
                    }
                add(pair, BUTTON_TOP_SIDEWAYS_DP, width = ui.dp(BUTTONS_SIDEWAYS_DP))
            } else {
                add(retry, BUTTON_TOP_DP)
                add(pick, Space.S)
            }
            grow()
            lastSeen = mono("", Type.SMALL, topDp = gap)
            lastSeen.gravity = Gravity.CENTER
        }

    init {
        view.accessibilityPaneTitle = ui.string(R.string.lost_title)
        view.addOnAttachStateChangeListener(
            object : View.OnAttachStateChangeListener {
                override fun onViewAttachedToWindow(v: View) = updateBlink()

                override fun onViewDetachedFromWindow(v: View) = stopBlink()
            },
        )
    }

    /** [attempt] of [max], what the screen is doing about it, and how long ago the laptop was last heard. */
    fun update(
        attempt: Int,
        max: Int,
        state: State,
        secondsSinceSeen: Long,
    ) {
        val line =
            when (state) {
                State.TRYING -> ui.string(R.string.lost_attempt, attempt, max)
                State.STOPPED -> ui.string(R.string.lost_stopped)
                State.OFF -> ui.string(R.string.lost_off)
            }
        // This runs every second for the last-seen clock. Setting the same text again would still count as a
        // change to the live region, and a screen reader would read the attempt out once a second.
        if (this.attempt.text.toString() != line) this.attempt.text = line
        progress.layoutParams.width = ui.dp(TRACK_WIDTH_DP) * attempt.coerceIn(0, max) / max
        progress.requestLayout()
        lastSeen.text = ui.string(R.string.lost_last_seen, Clock.format(secondsSinceSeen.toInt()), address)
        trying = state == State.TRYING
        updateBlink()
    }

    private fun updateBlink() {
        if (!trying || !view.isAttachedToWindow) {
            stopBlink()
            return
        }
        // With animations removed in system settings, the dot simply stays lit.
        if (blink != null || !ValueAnimator.areAnimatorsEnabled()) return
        blink =
            ValueAnimator.ofFloat(0f, 1f).apply {
                duration = BLINK_MS
                repeatCount = ValueAnimator.INFINITE
                addUpdateListener { dot.alpha = if (it.animatedFraction < HALF) 1f else DIM_ALPHA }
                start()
            }
    }

    private fun stopBlink() {
        blink?.cancel()
        blink = null
        dot.alpha = 1f
    }

    private companion object {
        const val RING_DP = 64f
        const val RING_SIDEWAYS_DP = 52f
        const val DOT_DP = 12f
        const val GAP_DP = 28f
        const val BODY_GAP_DP = 10f
        const val TRACK_DP = 4f
        const val TRACK_WIDTH_DP = 160f
        const val BUTTON_TOP_DP = 40f
        const val BUTTON_TOP_SIDEWAYS_DP = 20f

        /** Sideways the pair of buttons is held to this width rather than stretched across the whole screen. */
        const val BUTTONS_SIDEWAYS_DP = 520f
        const val BLINK_MS = 1400L
        const val HALF = 0.5f
        const val DIM_ALPHA = 0.15f
    }
}
