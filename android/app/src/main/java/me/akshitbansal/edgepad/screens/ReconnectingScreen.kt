package me.akshitbansal.edgepad.screens

import android.animation.ValueAnimator
import android.graphics.drawable.GradientDrawable
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.Clock

/** The link dropped on its own. Says so, counts the attempts to get it back, and offers the two ways out. */
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
                    setColor(ui.palette.ink)
                }
        }
    private val progress = View(ui.context).apply { setBackgroundColor(ui.palette.ink) }
    private var blink: ValueAnimator? = null
    private var trying = false
    private lateinit var attempt: TextView
    private lateinit var lastSeen: TextView

    val view: View =
        ui.page(centred = true) {
            grow()
            val ring =
                FrameLayout(ui.context).apply {
                    background =
                        GradientDrawable().apply {
                            shape = GradientDrawable.OVAL
                            setStroke(ui.dp(Space.HAIR), ui.palette.dim)
                        }
                    addView(dot, FrameLayout.LayoutParams(ui.dp(DOT_DP), ui.dp(DOT_DP), Gravity.CENTER))
                }
            add(ring, height = ui.dp(RING_DP), width = ui.dp(RING_DP))
            headline(ui.string(R.string.lost_title), Type.TITLE, Space.XXL).gravity = Gravity.CENTER
            body(ui.string(R.string.lost_body, laptop), Space.L).gravity = Gravity.CENTER
            attempt = mono("", Type.SMALL, topDp = Space.XL)
            attempt.gravity = Gravity.CENTER
            val track =
                FrameLayout(ui.context).apply {
                    setBackgroundColor(ui.palette.line)
                    addView(progress, FrameLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT))
                }
            add(track, Space.M, height = ui.dp(Space.HAIR), width = ui.dp(TRACK_DP))
            add(ui.button(ui.string(R.string.retry_now), Ui.Style.FILLED, onRetry), Space.XXXL)
            add(ui.button(ui.string(R.string.pick_another), Ui.Style.QUIET, onPickAnother), Space.M)
            grow()
            lastSeen = mono("", Type.MICRO, topDp = Space.XL)
            lastSeen.gravity = Gravity.CENTER
        }

    init {
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
        this.attempt.text =
            when (state) {
                State.TRYING -> ui.string(R.string.lost_attempt, attempt, max)
                State.STOPPED -> ui.string(R.string.lost_stopped)
                State.OFF -> ui.string(R.string.lost_off)
            }
        progress.layoutParams.width = ui.dp(TRACK_DP) * attempt.coerceIn(0, max) / max
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
        const val RING_DP = 54f
        const val DOT_DP = 8f
        const val TRACK_DP = 130f
        const val BLINK_MS = 1400L
        const val HALF = 0.5f
        const val DIM_ALPHA = 0.15f
    }
}
