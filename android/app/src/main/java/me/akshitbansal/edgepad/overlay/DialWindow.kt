package me.akshitbansal.edgepad.overlay

import android.content.Context
import android.content.res.Configuration
import android.graphics.Canvas
import android.graphics.Rect
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import me.akshitbansal.edgepad.surface.Dial
import me.akshitbansal.edgepad.surface.DialSlide
import me.akshitbansal.edgepad.surface.Perimeter
import me.akshitbansal.edgepad.surface.RulerPainter
import me.akshitbansal.edgepad.surface.TrackpadRecognizer
import kotlin.math.abs

/**
 * One dial's window over other apps: a small view the size of its ruler band (see [DialBounds]) that draws
 * the ruler as the control surface does and slides it with the finger. Everything outside the band belongs
 * to the app underneath, because the window is no larger than the band.
 *
 * The view is built empty and [bind] gives it its dial, so Android lint's (Context) constructor rule holds.
 */
class DialWindow(
    context: Context,
) : View(context) {
    private val density = resources.displayMetrics.density
    private val hit = FloatArray(2)
    private val pt = FloatArray(4)
    private val exclusion = Rect()
    private val exclusions = listOf(exclusion)
    private var isBound = false
    private lateinit var dial: Dial
    private lateinit var perimeter: Perimeter
    private lateinit var painter: RulerPainter
    private lateinit var numberOf: (Dial) -> String
    private var centre = 0f
    private var after = 0f
    private var before = 0f
    private var originX = 0
    private var originY = 0
    private var ink = 0
    private var accent = 0
    private var dim = 0
    private var lastS = 0f
    private var key = Int.MIN_VALUE
    private var number = ""

    /** Run once on the main thread when the screen turns, so the owner can lay the windows out again. */
    var onTurned: Runnable? = null

    /** Gives the view its dial and where its band sits; the paints and the text are the owner's. */
    fun bind(
        dial: Dial,
        perimeter: Perimeter,
        painter: RulerPainter,
        centre: Float,
        before: Float,
        after: Float,
        bounds: DialBounds,
        colors: IntArray,
        numberOf: (Dial) -> String,
    ) {
        this.dial = dial
        this.perimeter = perimeter
        this.painter = painter
        this.centre = centre
        this.before = before
        this.after = after
        this.numberOf = numberOf
        originX = bounds.left
        originY = bounds.top
        ink = colors[0]
        accent = colors[1]
        dim = colors[2]
        isBound = true
        contentDescription = dial.label
        sync(true)
    }

    /** Reads the dial's text again when its value changed, or always when [force], then draws. */
    fun sync(force: Boolean) {
        if (!isBound) return
        val next = ((dial.value * KEY_PRIME + dial.steps) * 2 + bit(dial.known)) * 2 + bit(dial.flag)
        if (force || next != key) {
            key = next
            number = numberOf(dial)
        }
        invalidate()
    }

    private fun bit(on: Boolean) = if (on) 1 else 0

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        // Keeps Android's back gesture off the whole band; the one rectangle is reused.
        exclusion.set(0, 0, w, h)
        systemGestureExclusionRects = exclusions
    }

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        onTurned?.let { post(it) }
    }

    override fun onDraw(canvas: Canvas) {
        if (!isBound) return
        canvas.save()
        // The painter works in screen pixels; this window is the piece of the screen at its origin.
        canvas.translate(-originX.toFloat(), -originY.toFloat())
        painter.draw(
            canvas,
            perimeter,
            centre,
            after,
            before,
            dial.rulerDp,
            if (dial.control != null) dial.rulerLengthDp else null,
            dial.armed,
            ink,
            accent,
            dim,
            dial.label,
            number,
            pt,
        )
        canvas.restore()
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (!isBound) return false
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                if (!onRuler(event.rawX, event.rawY)) return false
                dial.down()
                lastS = hit[0]
            }

            MotionEvent.ACTION_MOVE -> {
                if (!dial.pressed) return false
                val was = dial.armed
                lastS =
                    DialSlide.slide(
                        dial,
                        perimeter,
                        event.rawX,
                        event.rawY,
                        hit,
                        lastS,
                        density,
                        TrackpadRecognizer.SLOP_DP,
                    )
                if (dial.armed && !was) performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
            }

            MotionEvent.ACTION_UP -> {
                if (dial.up()) performClick()
            }

            MotionEvent.ACTION_CANCEL -> {
                dial.cancel()
            }

            else -> {
                return true
            }
        }
        sync(false)
        return true
    }

    /** Whether the screen point is on this dial's ruler; leaves its position along the edge in [hit]. */
    private fun onRuler(
        x: Float,
        y: Float,
    ): Boolean {
        perimeter.project(x, y, hit)
        val delta = perimeter.delta(centre, hit[0])
        val reach = if (delta >= 0) after else before
        return abs(delta) < reach + HIT_SLACK_DP * density
    }

    private companion object {
        /** Past the ruler's end a finger still counts, as on the control surface. */
        const val HIT_SLACK_DP = 12f
        const val KEY_PRIME = 31
    }
}
