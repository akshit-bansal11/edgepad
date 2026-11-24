package me.akshitbansal.edgepad.screens

import android.animation.ValueAnimator
import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.view.View
import android.view.animation.LinearInterpolator
import me.akshitbansal.edgepad.LogoMotion
import me.akshitbansal.edgepad.R
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.roundToInt
import kotlin.math.sin

/**
 * The Edgepad logo in its dark colours, drawn here so it can play its 0.75 s pop: the exact tile path (a vector), a
 * true quarter-circle arc and the ticks, each [LogoMotion] part springing out from the pivot. One animator, started
 * when the view joins a window and ended when it leaves; the system's animation scale zeroes it, which draws the
 * finished logo at once. Decorative.
 */
class LogoView(
    context: Context,
) : View(context) {
    private val tile = context.getDrawable(R.drawable.logo_tile)
    private val pen =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeCap = Paint.Cap.ROUND
        }
    private val box = RectF()
    private var ms = LogoMotion.TOTAL_MS
    private var animator: ValueAnimator? = null

    init {
        importantForAccessibility = IMPORTANT_FOR_ACCESSIBILITY_NO
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        animator =
            ValueAnimator.ofFloat(0f, LogoMotion.TOTAL_MS).apply {
                duration = LogoMotion.TOTAL_MS.toLong()
                interpolator = LinearInterpolator()
                addUpdateListener {
                    ms = it.animatedValue as Float
                    invalidate()
                }
                start()
            }
    }

    override fun onDetachedFromWindow() {
        animator?.cancel()
        animator = null
        super.onDetachedFromWindow()
    }

    override fun onDraw(canvas: Canvas) {
        val unit = min(width, height) / LogoMotion.CANVAS
        tile?.setBounds(0, 0, width, height)
        tile?.draw(canvas)
        pen.strokeWidth = LogoMotion.STROKE * unit
        val pivot = LogoMotion.PIVOT * unit
        LogoMotion.strokes.forEachIndexed { index, stroke ->
            val alpha = (LogoMotion.opacity(index, ms) * ALPHA_MAX).roundToInt()
            if (alpha == 0) return@forEachIndexed
            val k = LogoMotion.scale(index, ms)
            pen.color = withAlpha(if (stroke.accent) accent else ink, alpha)
            if (stroke.arc) {
                val r = stroke.outer * k * unit
                box.set(pivot - r, pivot - r, pivot + r, pivot + r)
                canvas.drawArc(box, ARC_START, ARC_SWEEP, false, pen)
                return@forEachIndexed
            }
            val radians = Math.toRadians(stroke.angle.toDouble())
            val dx = cos(radians).toFloat()
            val dy = sin(radians).toFloat()
            val a = stroke.inner * k * unit
            val b = stroke.outer * k * unit
            canvas.drawLine(pivot + dx * a, pivot + dy * a, pivot + dx * b, pivot + dy * b, pen)
        }
    }

    private fun withAlpha(
        colour: Int,
        alpha: Int,
    ): Int = Color.argb(alpha, Color.red(colour), Color.green(colour), Color.blue(colour))

    private companion object {
        const val ALPHA_MAX = 255f
        const val ARC_START = 180f
        const val ARC_SWEEP = 90f
        val ink = Color.parseColor("#F2F3F5")
        val accent = Color.parseColor("#4DA3FF")
    }
}
