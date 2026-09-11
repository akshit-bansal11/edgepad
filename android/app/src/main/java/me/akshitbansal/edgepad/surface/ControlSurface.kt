package me.akshitbansal.edgepad.surface

import android.content.Context
import android.content.res.Configuration
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.Typeface
import android.text.TextPaint
import android.text.TextUtils
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.sin

/**
 * The full-screen control surface: ruler dials wherever Settings placed them on the edges, a media bar
 * along the top, and everything else is the trackpad. Black on white or white on black, following the
 * app's theme. Touch is dispatched unbuffered so samples arrive as they happen, not batched to the
 * display's vsync, and every historical sample in a MOVE is fed through, so nothing the finger did is skipped.
 */
class ControlSurface(
    context: Context,
    settings: Settings,
    private val send: (Frame) -> Unit,
) : View(context) {
    /** For layout tools only: a surface that sends nowhere. */
    constructor(context: Context) : this(context, Settings(context), send = {})

    private val density = resources.displayMetrics.density
    private val trackpad = TrackpadRecognizer(density, send)
    private val dials =
        DialKind.entries
            .map { it to settings.placement(it) }
            .filter { (_, placement) -> placement.edge != Edge.OFF }
            .map { (kind, placement) -> kind.dial(placement, context.getString(kind.labelRes), send, ::haptic) }

    private val dark =
        (resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES
    private val background = if (dark) BLACK else WHITE
    private val foreground = if (dark) LIGHT_GREY else DARK_GREY
    private val accent = if (dark) WHITE else BLACK
    private val dim = if (dark) DIM_ON_BLACK else DIM_ON_WHITE

    /** The dial that owns the current touch, if a finger went down on one. */
    private var activeDial: Dial? = null

    /** True while a finger that went down on a media button is still down. */
    private var mediaPressed = false
    private val holdTimer =
        Runnable {
            activeDial?.hold()
            invalidate()
        }

    private var nowPlaying = ""
    private var app = ""
    private var playing = false
    private var mediaPosition = 0

    /** Shown faintly in the middle so the real round-trip number on this hardware is always in view. */
    var status: String = ""
        set(value) {
            field = value
            invalidate()
        }

    private val stroke =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeCap = Paint.Cap.ROUND
        }
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val text =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            textAlign = Paint.Align.CENTER
            typeface = Typeface.MONOSPACE
        }
    private val path = Path()

    init {
        keepScreenOn = true
        isFocusable = true
    }

    private fun haptic() {
        performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
    }

    fun onState(
        control: Int,
        value: Int,
        flags: Int,
    ) {
        val id = ControlId.of(control) ?: return
        val flag = (flags and FLAG_BIT) != 0
        if (id == ControlId.MEDIA_POSITION) {
            mediaPosition = value
            playing = flag
        }
        dials.firstOrNull { it.control == id }?.fromLaptop(value, flag)
        invalidate()
    }

    fun onText(
        kind: Int,
        value: String,
    ) {
        if (kind == TEXT_NOW_PLAYING) nowPlaying = value else app = value
        invalidate()
    }

    /** One per dial, filled in when the size is known; nothing is allocated during layout. */
    private val exclusions = List(dials.size) { Rect() }

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        // Keep Android's back gesture off the side dials; the bottom edge (home) cannot be claimed.
        val hit = dp(HIT_RADIUS_DP).toInt()
        dials.forEachIndexed { i, dial ->
            val cx = dial.placement.centreX(w.toFloat()).toInt()
            val cy = dial.placement.centreY(h.toFloat()).toInt()
            exclusions[i].set(cx - hit, cy - hit, cx + hit, cy + hit)
        }
        systemGestureExclusionRects = exclusions
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                requestUnbufferedDispatch(event)
                val dial = dialAt(event.x, event.y)
                val button = mediaButtonAt(event.x, event.y)
                when {
                    dial != null -> {
                        activeDial = dial
                        dial.down(event.x - centreX(dial), event.y - centreY(dial))
                        postDelayed(holdTimer, HOLD_MS)
                    }

                    button != null -> {
                        mediaPressed = true
                        haptic()
                        send(button.frame())
                        performClick()
                    }

                    else -> {
                        activeDial = null
                        feed(TrackpadRecognizer.Action.DOWN, event, exclude = -1)
                    }
                }
            }

            MotionEvent.ACTION_POINTER_DOWN -> {
                if (onTrackpad()) feed(TrackpadRecognizer.Action.DOWN, event, exclude = -1)
            }

            MotionEvent.ACTION_MOVE -> {
                val dial = activeDial
                if (dial != null) {
                    dial.move(event.x - centreX(dial), event.y - centreY(dial), dp(TrackpadRecognizer.SLOP_DP))
                } else if (onTrackpad()) {
                    for (h in 0 until event.historySize) feedHistorical(event, h)
                    feed(TrackpadRecognizer.Action.MOVE, event, exclude = -1)
                }
            }

            MotionEvent.ACTION_POINTER_UP -> {
                if (onTrackpad()) feed(TrackpadRecognizer.Action.UP, event, exclude = event.actionIndex)
            }

            MotionEvent.ACTION_UP -> {
                removeCallbacks(holdTimer)
                val dial = activeDial
                when {
                    dial != null -> {
                        activeDial = null
                        if (dial.up()) performClick()
                    }

                    mediaPressed -> {
                        mediaPressed = false
                    }

                    else -> {
                        feed(TrackpadRecognizer.Action.UP, event, exclude = event.actionIndex)
                    }
                }
            }

            MotionEvent.ACTION_CANCEL -> {
                removeCallbacks(holdTimer)
                activeDial?.cancel()
                activeDial = null
                mediaPressed = false
                trackpad.handle(TrackpadRecognizer.Action.CANCEL, FloatArray(0), FloatArray(0), event.eventTime)
            }

            else -> {
                return super.onTouchEvent(event)
            }
        }
        invalidate()
        return true
    }

    private fun onTrackpad(): Boolean = activeDial == null && !mediaPressed

    private fun feed(
        action: TrackpadRecognizer.Action,
        event: MotionEvent,
        exclude: Int,
    ) {
        val n = event.pointerCount - (if (exclude >= 0) 1 else 0)
        val xs = FloatArray(n)
        val ys = FloatArray(n)
        var j = 0
        for (i in 0 until event.pointerCount) {
            if (i == exclude) continue
            xs[j] = event.getX(i)
            ys[j] = event.getY(i)
            j++
        }
        trackpad.handle(action, xs, ys, event.eventTime)
    }

    private fun feedHistorical(
        event: MotionEvent,
        h: Int,
    ) {
        val xs = FloatArray(event.pointerCount) { event.getHistoricalX(it, h) }
        val ys = FloatArray(event.pointerCount) { event.getHistoricalY(it, h) }
        trackpad.handle(TrackpadRecognizer.Action.MOVE, xs, ys, event.getHistoricalEventTime(h))
    }

    private fun dialAt(
        x: Float,
        y: Float,
    ): Dial? = dials.firstOrNull { hypot(x - centreX(it), y - centreY(it)) <= dp(HIT_RADIUS_DP) }

    private fun centreX(dial: Dial): Float = dial.placement.centreX(width.toFloat())

    private fun centreY(dial: Dial): Float = dial.placement.centreY(height.toFloat())

    // The media bar: a strip along the top between the top dials, with previous, play/pause and next.

    private fun mediaBar(): RectF {
        val margin = dp(HIT_RADIUS_DP)
        val barWidth = minOf(width - 2 * margin, dp(MEDIA_BAR_MAX_WIDTH_DP))
        val left = (width - barWidth) / 2
        return RectF(left, dp(MEDIA_BAR_TOP_DP), left + barWidth, dp(MEDIA_BAR_TOP_DP) + dp(MEDIA_BAR_HEIGHT_DP))
    }

    private fun mediaButton(index: Int): RectF {
        val bar = mediaBar()
        val size = dp(MEDIA_BUTTON_DP)
        val gap = dp(MEDIA_BUTTON_GAP_DP)
        val left = bar.centerX() - (MEDIA_BUTTONS * size + (MEDIA_BUTTONS - 1) * gap) / 2 + index * (size + gap)
        val top = bar.bottom - size
        return RectF(left, top, left + size, top + size)
    }

    private fun mediaButtonAt(
        x: Float,
        y: Float,
    ): ActionId? =
        when {
            mediaButton(0).contains(x, y) -> ActionId.PREVIOUS_TRACK
            mediaButton(1).contains(x, y) -> ActionId.PLAY_PAUSE
            mediaButton(2).contains(x, y) -> ActionId.NEXT_TRACK
            else -> null
        }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawColor(background)
        for (dial in dials) drawDial(canvas, dial)
        drawMediaBar(canvas)
        text.color = dim
        text.textSize = dp(STATUS_TEXT_DP)
        canvas.drawText(status, width / 2f, height / 2f, text)
    }

    private fun drawDial(
        canvas: Canvas,
        dial: Dial,
    ) {
        val cx = centreX(dial)
        val cy = centreY(dial)
        val placement = dial.placement
        val radius = dp(if (dial.armed) ARMED_RADIUS_DP else REST_RADIUS_DP)
        val colour =
            if (dial.muted) {
                MUTED
            } else if (dial.armed) {
                accent
            } else {
                foreground
            }
        val indicatorDeg = placement.indicatorDeg
        val visible = placement.halfSpanDeg - (if (dial.armed) 0f else REST_TRIM_DEG)

        // Ticks are laid out along the ruler by value; the ruler is shifted so the current value sits
        // under the indicator. A stepper has no value, so its ruler just scrolls with the finger.
        val shift =
            if (dial.control != null) {
                dial.level
            } else {
                placement.direction * dial.rulerOffsetDeg / Dial.DEGREES_PER_UNIT
            }
        stroke.color = colour
        stroke.strokeWidth = dp(TICK_STROKE_DP)
        val reach = (visible / Dial.DEGREES_PER_UNIT).toInt() + 1
        for (v in (shift.toInt() - reach)..(shift.toInt() + reach)) {
            if (dial.control != null && (v < 0 || v > Dial.MAX_LEVEL.toInt())) continue
            val deg = indicatorDeg + placement.direction * (shift - v) * Dial.DEGREES_PER_UNIT
            if (abs(deg - indicatorDeg) > visible) continue
            val long = v % TICKS_PER_LONG == 0
            val length = dp(if (long) LONG_TICK_DP else SHORT_TICK_DP)
            val rad = Math.toRadians(deg.toDouble())
            val ux = cos(rad).toFloat()
            val uy = sin(rad).toFloat()
            canvas.drawLine(
                cx + ux * radius,
                cy + uy * radius,
                cx + ux * (radius + length),
                cy + uy * (radius + length),
                stroke,
            )
        }

        val rad = Math.toRadians(indicatorDeg.toDouble())
        val ux = cos(rad).toFloat()
        val uy = sin(rad).toFloat()
        stroke.strokeWidth = dp(INDICATOR_STROKE_DP)
        val inner = radius - dp(INDICATOR_DP)
        val outer = radius - dp(INDICATOR_GAP_DP)
        canvas.drawLine(cx + ux * inner, cy + uy * inner, cx + ux * outer, cy + uy * outer, stroke)

        // The small number rides the centre of the dial, inside the arc.
        text.color = colour
        if (dial.control != null) {
            text.textSize = dp(if (dial.armed) ARMED_VALUE_TEXT_DP else VALUE_TEXT_DP)
            val number = if (dial.muted) context.getString(R.string.dial_muted_short) else dial.value.toString()
            val numberRadius = radius * VALUE_RADIUS_FRACTION
            canvas.drawText(number, cx + ux * numberRadius, cy + uy * numberRadius + text.textSize / 3, text)
        }

        text.textSize = dp(LABEL_TEXT_DP)
        val labelRadius = radius + dp(LABEL_OFFSET_DP)
        canvas.drawText(dial.label, cx + ux * labelRadius, cy + uy * labelRadius + text.textSize / 3, text)
    }

    private fun drawMediaBar(canvas: Canvas) {
        val bar = mediaBar()
        val monogram = dp(MONOGRAM_DP)
        val textTop = bar.top + monogram / 2

        // App monogram: the first letter of whatever is playing it, in a ring.
        stroke.color = foreground
        stroke.strokeWidth = dp(TICK_STROKE_DP)
        val ringX = bar.left + monogram / 2
        canvas.drawCircle(ringX, textTop, monogram / 2, stroke)
        text.color = foreground
        text.textSize = dp(LABEL_TEXT_DP)
        canvas.drawText(app.take(1).uppercase(), ringX, textTop + text.textSize / 3, text)

        // Now playing, ellipsised to the room left of the monogram.
        text.textAlign = Paint.Align.LEFT
        text.textSize = dp(NOW_PLAYING_TEXT_DP)
        text.color = if (nowPlaying.isEmpty()) dim else accent
        val shown = nowPlaying.ifEmpty { context.getString(R.string.nothing_playing) }
        val room = bar.width() - monogram - dp(MEDIA_TEXT_GAP_DP)
        val line = TextUtils.ellipsize(shown, text, room, TextUtils.TruncateAt.END).toString()
        canvas.drawText(line, bar.left + monogram + dp(MEDIA_TEXT_GAP_DP), textTop + text.textSize / 3, text)
        text.textAlign = Paint.Align.CENTER

        // Progress under the text.
        val progressY = textTop + monogram / 2 + dp(PROGRESS_GAP_DP)
        stroke.color = dim
        canvas.drawLine(bar.left, progressY, bar.right, progressY, stroke)
        stroke.color = accent
        canvas.drawLine(bar.left, progressY, bar.left + bar.width() * mediaPosition / Dial.MAX_LEVEL, progressY, stroke)

        fill.color = foreground
        drawPrevious(canvas, mediaButton(0))
        if (playing) drawPause(canvas, mediaButton(1)) else drawPlay(canvas, mediaButton(1))
        drawNext(canvas, mediaButton(2))
    }

    private fun drawPlay(
        canvas: Canvas,
        box: RectF,
    ) {
        val inset = box.width() * GLYPH_INSET
        path.reset()
        path.moveTo(box.left + inset, box.top + inset)
        path.lineTo(box.right - inset, box.centerY())
        path.lineTo(box.left + inset, box.bottom - inset)
        path.close()
        canvas.drawPath(path, fill)
    }

    private fun drawPause(
        canvas: Canvas,
        box: RectF,
    ) {
        val inset = box.width() * GLYPH_INSET
        val barWidth = (box.width() - 2 * inset) / GLYPH_THIRDS
        canvas.drawRect(box.left + inset, box.top + inset, box.left + inset + barWidth, box.bottom - inset, fill)
        canvas.drawRect(box.right - inset - barWidth, box.top + inset, box.right - inset, box.bottom - inset, fill)
    }

    private fun drawNext(
        canvas: Canvas,
        box: RectF,
    ) {
        val inset = box.width() * GLYPH_INSET
        val barWidth = (box.width() - 2 * inset) / GLYPH_FIFTHS
        path.reset()
        path.moveTo(box.left + inset, box.top + inset)
        path.lineTo(box.right - inset - barWidth, box.centerY())
        path.lineTo(box.left + inset, box.bottom - inset)
        path.close()
        canvas.drawPath(path, fill)
        canvas.drawRect(box.right - inset - barWidth, box.top + inset, box.right - inset, box.bottom - inset, fill)
    }

    private fun drawPrevious(
        canvas: Canvas,
        box: RectF,
    ) {
        val inset = box.width() * GLYPH_INSET
        val barWidth = (box.width() - 2 * inset) / GLYPH_FIFTHS
        path.reset()
        path.moveTo(box.right - inset, box.top + inset)
        path.lineTo(box.left + inset + barWidth, box.centerY())
        path.lineTo(box.right - inset, box.bottom - inset)
        path.close()
        canvas.drawPath(path, fill)
        canvas.drawRect(box.left + inset, box.top + inset, box.left + inset + barWidth, box.bottom - inset, fill)
    }

    private fun dp(value: Float): Float = value * density

    private companion object {
        const val FLAG_BIT = 1
        const val TEXT_NOW_PLAYING = 0
        const val HOLD_MS = 250L
        const val HIT_RADIUS_DP = 96f
        const val REST_RADIUS_DP = 44f
        const val ARMED_RADIUS_DP = 72f
        const val REST_TRIM_DEG = 10f
        const val SHORT_TICK_DP = 8f
        const val LONG_TICK_DP = 16f
        const val TICK_STROKE_DP = 1.5f
        const val INDICATOR_STROKE_DP = 3f
        const val INDICATOR_DP = 14f
        const val INDICATOR_GAP_DP = 2f
        const val LABEL_OFFSET_DP = 34f
        const val LABEL_TEXT_DP = 12f
        const val VALUE_TEXT_DP = 14f
        const val ARMED_VALUE_TEXT_DP = 22f
        const val VALUE_RADIUS_FRACTION = 0.45f
        const val STATUS_TEXT_DP = 12f
        const val TICKS_PER_LONG = 5
        const val MEDIA_BAR_TOP_DP = 24f
        const val MEDIA_BAR_HEIGHT_DP = 88f
        const val MEDIA_BAR_MAX_WIDTH_DP = 360f
        const val MEDIA_BUTTONS = 3
        const val MEDIA_BUTTON_DP = 44f
        const val MEDIA_BUTTON_GAP_DP = 24f
        const val MONOGRAM_DP = 28f
        const val MEDIA_TEXT_GAP_DP = 10f
        const val NOW_PLAYING_TEXT_DP = 14f
        const val PROGRESS_GAP_DP = 8f
        const val GLYPH_INSET = 0.25f
        const val GLYPH_THIRDS = 3f
        const val GLYPH_FIFTHS = 5f
        const val BLACK = 0xFF000000.toInt()
        const val WHITE = 0xFFFFFFFF.toInt()
        const val LIGHT_GREY = 0xFFB8C0C4.toInt()
        const val DARK_GREY = 0xFF3A4246.toInt()
        const val DIM_ON_BLACK = 0xFF4A5558.toInt()
        const val DIM_ON_WHITE = 0xFFB0B8BC.toInt()
        const val MUTED = 0xFFD97A5A.toInt()
    }
}
