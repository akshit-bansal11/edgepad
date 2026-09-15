package me.akshitbansal.edgepad.screens

import android.animation.ValueAnimator
import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.drawable.Drawable
import android.text.TextPaint
import android.view.View
import android.view.animation.AccelerateDecelerateInterpolator
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.roundToInt

/**
 * One guide page's drawing, in a 300 by 130 frame fitted into the view over a dotted ground. The still
 * lines are a vector drawable in the theme's colours; the few parts that move are drawn here, and move
 * for five seconds after the page appears, then rest. With animations off in system settings they rest
 * from the start. The page's own text says everything the drawing shows, so screen readers skip it.
 */
class GuideArt(
    context: Context,
    private val kind: Kind,
) : View(context) {
    /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
    constructor(context: Context) : this(context, Kind.FEATURES)

    enum class Kind(
        val art: Int,
    ) {
        FEATURES(R.drawable.guide_features),
        ANATOMY(R.drawable.guide_anatomy),
        SURFACE(R.drawable.guide_surface),
        KEYBOARD(R.drawable.guide_keyboard),
        GAMEPAD(R.drawable.guide_gamepad),
    }

    private val density = resources.displayMetrics.density
    private val palette = Palette.of(context)
    private val still: Drawable? = context.getDrawable(kind.art)
    private val labels = labelsOf(context, kind)
    private val ease = AccelerateDecelerateInterpolator()
    private val ground = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = palette.line }
    private val border =
        Paint().apply {
            style = Paint.Style.STROKE
            strokeWidth = Space.HAIR * density
            color = palette.line
        }
    private val line =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = STROKE
            strokeCap = Paint.Cap.ROUND
            strokeJoin = Paint.Join.ROUND
        }
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val text =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
            textSize = LABEL_SIZE
            letterSpacing = LABEL_TRACKING
            color = palette.dim
        }
    private var scale = 1f
    private var left = 0f
    private var top = 0f
    private var elapsed = REST_MS
    private var animator: ValueAnimator? = null

    init {
        importantForAccessibility = IMPORTANT_FOR_ACCESSIBILITY_NO
    }

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        scale = minOf(w / FRAME_W, h / FRAME_H)
        left = (w - FRAME_W * scale) / 2
        top = (h - FRAME_H * scale) / 2
        // Bounds in real pixels, drawn before the frame's scale, so the vector is rasterised sharp.
        still?.setBounds(left.toInt(), top.toInt(), (left + FRAME_W * scale).toInt(), (top + FRAME_H * scale).toInt())
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        if (!ValueAnimator.areAnimatorsEnabled()) return
        animator =
            ValueAnimator.ofFloat(0f, 1f).apply {
                duration = RUN_MS.toLong()
                addUpdateListener {
                    elapsed = if (it.animatedFraction >= 1f) REST_MS else it.animatedFraction * RUN_MS
                    invalidate()
                }
                start()
            }
    }

    override fun onDetachedFromWindow() {
        animator?.cancel()
        animator = null
        elapsed = REST_MS
        super.onDetachedFromWindow()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val cell = GROUND_DP * density
        val dot = density
        var y = (height % cell) / 2
        while (y < height) {
            var x = (width % cell) / 2
            while (x < width) {
                canvas.drawCircle(x, y, dot, ground)
                x += cell
            }
            y += cell
        }
        val hair = border.strokeWidth / 2
        canvas.drawRect(hair, hair, width - hair, height - hair, border)

        still?.draw(canvas)
        val saved = canvas.save()
        canvas.translate(left, top)
        canvas.scale(scale, scale)
        when (kind) {
            Kind.FEATURES -> features(canvas)
            Kind.ANATOMY -> anatomy(canvas)
            Kind.SURFACE -> surface(canvas)
            Kind.KEYBOARD -> keyboard(canvas)
            Kind.GAMEPAD -> gamepad(canvas)
        }
        for (i in labels.indices) {
            val label = labels[i]
            text.textAlign = label.align
            canvas.drawText(label.text, label.x, label.y, text)
        }
        canvas.restoreToCount(saved)
    }

    /** A dot runs along the link from the phone to the laptop, fading in and out at the ends. */
    private fun features(canvas: Canvas) {
        val t = phase(TRAVEL_MS)
        val dx = TRAVEL * ease.getInterpolation(t)
        val alpha =
            if (t < EDGE) {
                t / EDGE
            } else if (t > 1 - EDGE) {
                (1 - t) / EDGE
            } else {
                1f
            }
        fill.color = palette.ink
        fill.alpha = (alpha * OPAQUE).roundToInt()
        canvas.drawCircle(58 + dx, 63f, 3.2f, fill)
        line.color = palette.dim
        line.alpha = fill.alpha
        canvas.drawLine(40 + dx, 63f, 50 + dx, 63f, line)
    }

    /** The trackpad's ring breathes in the middle of the phone. */
    private fun anatomy(canvas: Canvas) {
        line.color = palette.dim
        line.alpha = (wave(BREATHE_MS, BREATHE_LOW) * OPAQUE).roundToInt()
        canvas.drawCircle(150f, 60f, 10f, line)
    }

    /** One finger's arrow sweeps right, two fingers' rises, three fingers' wobbles. */
    private fun surface(canvas: Canvas) {
        line.color = palette.ink
        val sweep = phase(SWEEP_MS)
        line.alpha = (fade(sweep) * OPAQUE).roundToInt()
        val dx = SWEEP_FROM + SWEEP_SPAN * sweep
        canvas.drawLine(78 + dx, 66f, 100 + dx, 66f, line)
        canvas.drawLine(96 + dx, 61f, 101 + dx, 66f, line)
        canvas.drawLine(101 + dx, 66f, 96 + dx, 71f, line)

        val rise = phase(SWEEP_MS, RISE_DELAY_MS)
        line.alpha = (fade(rise) * OPAQUE).roundToInt()
        val dy = RISE_FROM - RISE_SPAN * rise
        canvas.drawLine(172f, 74 + dy, 172f, 54 + dy, line)
        canvas.drawLine(167f, 59 + dy, 172f, 54 + dy, line)
        canvas.drawLine(172f, 54 + dy, 177f, 59 + dy, line)

        line.color = palette.dim
        val wx = wobbleX()
        val wy = wobbleY()
        canvas.drawLine(258 + wx, 68 + wy, 276 + wx, 68 + wy, line)
        canvas.drawLine(272 + wx, 63 + wy, 277 + wx, 68 + wy, line)
        canvas.drawLine(277 + wx, 68 + wy, 272 + wx, 73 + wy, line)
    }

    /** One key blinks, as a latched modifier waits for the next key. */
    private fun keyboard(canvas: Canvas) {
        line.color = palette.ink
        canvas.drawRoundRect(106f, 48f, 122f, 62f, 2f, 2f, line)
        fill.color = palette.ink
        fill.alpha = (wave(BLINK_MS, BLINK_LOW) * OPAQUE).roundToInt()
        canvas.drawRoundRect(106f, 48f, 122f, 62f, 2f, 2f, fill)
    }

    /** The left stick wanders and one face button blinks. */
    private fun gamepad(canvas: Canvas) {
        line.color = palette.ink
        canvas.drawCircle(108 + wobbleX(), 62 + wobbleY(), 5f, line)
        line.alpha = (wave(PAD_BLINK_MS, BLINK_LOW) * OPAQUE).roundToInt()
        canvas.drawCircle(196f, 72f, 5f, line)
    }

    /** Where [elapsed] falls in a cycle of [period] ms that starts [delay] ms late, from 0 to 1. */
    private fun phase(
        period: Float,
        delay: Float = 0f,
    ): Float = (((elapsed - delay) % period + period) % period) / period

    /** Low, up to 1, and back, once per [period]. */
    private fun wave(
        period: Float,
        low: Float,
    ): Float = low + (1 - low) * (1 - cos(2 * PI.toFloat() * phase(period))) / 2

    /** In over the first 30% of the cycle, out over the rest. */
    private fun fade(t: Float): Float = if (t < FADE_IN) t / FADE_IN else 1 - (t - FADE_IN) / (1 - FADE_IN)

    private fun wobbleX(): Float = wobble(WOBBLE_X)

    private fun wobbleY(): Float = wobble(WOBBLE_Y)

    /** A small loop through four points, straight between them. */
    private fun wobble(points: FloatArray): Float {
        val at = phase(WOBBLE_MS) * points.size
        val i = at.toInt() % points.size
        val next = points[(i + 1) % points.size]
        return points[i] + (next - points[i]) * (at - at.toInt())
    }

    private class Label(
        val text: String,
        val x: Float,
        val y: Float,
        val align: Paint.Align,
    )

    private companion object {
        const val FRAME_W = 300f
        const val FRAME_H = 130f
        const val GROUND_DP = 16f
        const val STROKE = 1.4f
        const val LABEL_SIZE = 8.5f
        const val LABEL_TRACKING = 0.14f
        const val OPAQUE = 255
        const val RUN_MS = 5000f

        /** Where everything rests: a moment at which every moving part is in view. */
        const val REST_MS = 700f
        const val TRAVEL_MS = 2600f
        const val TRAVEL = 164f
        const val EDGE = 0.08f
        const val BREATHE_MS = 3000f
        const val BREATHE_LOW = 0.35f
        const val SWEEP_MS = 1800f
        const val SWEEP_FROM = -10f
        const val SWEEP_SPAN = 36f
        const val RISE_DELAY_MS = 400f
        const val RISE_FROM = 8f
        const val RISE_SPAN = 22f
        const val FADE_IN = 0.3f
        const val WOBBLE_MS = 2400f
        const val BLINK_MS = 1600f
        const val PAD_BLINK_MS = 1800f
        const val BLINK_LOW = 0.15f
        val WOBBLE_X = floatArrayOf(0f, 4f, -2f, -4f)
        val WOBBLE_Y = floatArrayOf(0f, -3f, 3f, -2f)

        fun labelsOf(
            context: Context,
            kind: Kind,
        ): List<Label> {
            fun label(
                res: Int,
                x: Float,
                y: Float,
                align: Paint.Align = Paint.Align.LEFT,
            ) = Label(context.getString(res), x, y, align)
            return when (kind) {
                Kind.FEATURES -> {
                    listOf(
                        label(R.string.guide_art_phone, 33f, 108f, Paint.Align.CENTER),
                        label(R.string.guide_art_laptop, 259f, 96f, Paint.Align.CENTER),
                        label(R.string.guide_art_link, 150f, 122f, Paint.Align.CENTER),
                    )
                }

                Kind.ANATOMY -> {
                    listOf(
                        label(R.string.guide_art_dials, 22f, 31f),
                        label(R.string.guide_art_trackpad, 12f, 63f),
                        label(R.string.guide_art_media, 22f, 100f),
                        label(R.string.guide_art_keys, 234f, 27f),
                        label(R.string.guide_art_gestures, 234f, 63f),
                    )
                }

                Kind.SURFACE -> {
                    listOf(
                        label(R.string.guide_art_move, 44f, 106f),
                        label(R.string.guide_art_scroll, 128f, 106f),
                        label(R.string.guide_art_swipe, 212f, 106f),
                    )
                }

                Kind.KEYBOARD -> {
                    listOf(label(R.string.guide_art_latch, 150f, 116f, Paint.Align.CENTER))
                }

                Kind.GAMEPAD -> {
                    listOf(label(R.string.guide_art_presets, 150f, 116f, Paint.Align.CENTER))
                }
            }
        }
    }
}
