package me.akshitbansal.edgepad.screens

import android.animation.ValueAnimator
import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.view.View
import kotlin.math.roundToInt

/**
 * A laptop's status dot: lit for the connected or remembered one, faint otherwise, with a ring that
 * widens and fades while a connection is being made. With animations off in system settings the ring
 * simply stands still round the dot.
 */
class Pulse(
    context: Context,
    private val ink: Int,
    private val off: Int,
) : View(context) {
    /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
    constructor(context: Context) : this(context, 0, 0)

    var lit = false
        set(value) {
            field = value
            invalidate()
        }

    var pulsing = false
        set(value) {
            field = value
            update()
        }

    private val density = resources.displayMetrics.density
    private val dot = Paint(Paint.ANTI_ALIAS_FLAG)
    private val ring =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = density
        }
    private var phase = 0f
    private var animator: ValueAnimator? = null

    init {
        importantForAccessibility = IMPORTANT_FOR_ACCESSIBILITY_NO
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        update()
    }

    override fun onDetachedFromWindow() {
        stop()
        super.onDetachedFromWindow()
    }

    private fun update() {
        if (!pulsing || !isAttachedToWindow || !ValueAnimator.areAnimatorsEnabled()) {
            stop()
            invalidate()
            return
        }
        if (animator != null) return
        animator =
            ValueAnimator.ofFloat(0f, 1f).apply {
                duration = PULSE_MS
                repeatCount = ValueAnimator.INFINITE
                addUpdateListener {
                    phase = it.animatedFraction
                    invalidate()
                }
                start()
            }
    }

    private fun stop() {
        animator?.cancel()
        animator = null
        phase = 0f
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val cx = width / 2f
        val cy = height / 2f
        val r = DOT_DP * density / 2
        dot.color = if (lit || pulsing) ink else off
        canvas.drawCircle(cx, cy, r, dot)
        if (!pulsing) return
        val widest = minOf(width, height) / 2f - ring.strokeWidth
        ring.color = ink
        ring.alpha = ((1f - phase) * OPAQUE).roundToInt()
        canvas.drawCircle(cx, cy, r + (widest - r) * phase, ring)
    }

    private companion object {
        const val DOT_DP = 6f
        const val PULSE_MS = 1800L
        const val OPAQUE = 255
    }
}
