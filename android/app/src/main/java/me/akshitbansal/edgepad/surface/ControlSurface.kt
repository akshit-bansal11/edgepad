package me.akshitbansal.edgepad.surface

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.Rect
import android.graphics.RectF
import android.os.Bundle
import android.text.TextPaint
import android.text.TextUtils
import android.util.TypedValue
import android.view.HapticFeedbackConstants
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.RoundedCorner
import android.view.View
import android.view.accessibility.AccessibilityNodeInfo
import android.view.inputmethod.BaseInputConnection
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputConnection
import android.view.inputmethod.InputMethodManager
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import kotlin.math.abs
import kotlin.math.ceil
import kotlin.math.cos
import kotlin.math.floor
import kotlin.math.roundToInt
import kotlin.math.sin

/**
 * The control surface: a ruler wrapped round each corner that holds a dial, the media pieces wherever the
 * user put them, a small gear at the bottom, and everything else is the trackpad. A touch that starts
 * inside a corner's zone is that dial's; one that starts on a media piece or the gear is a button; any
 * other is the trackpad.
 *
 * Everything is drawn here rather than built from child views: a touch reaches the recogniser with no view
 * hierarchy in between, dispatch is unbuffered so samples arrive as they happen, and every historical
 * sample in a MOVE is fed through. What the laptop reported lives in [state], which outlives this view.
 */
class ControlSurface(
    context: Context,
    settings: Settings,
    private val state: LaptopState,
    private val laptopName: String,
    private val send: (Frame) -> Unit,
    private val onOpenSettings: () -> Unit,
) : View(context) {
    /** For layout tools only: a surface that sends nowhere. */
    constructor(context: Context) : this(context, Settings(context), LaptopState(), "", {}, {})

    private val density = resources.displayMetrics.density
    private val palette = Palette.of(context)
    private val trackpad = TrackpadRecognizer(density, settings.naturalScroll, settings::gesture, send)
    private val hapticsOn = settings.haptics
    private val showHints = settings.hints
    private val dials: List<Dial> =
        (0 until CORNERS).mapNotNull { corner ->
            settings.corner(corner)?.let { kind ->
                kind.dial(
                    corner,
                    context.getString(kind.shortRes),
                    Dial.BASE_UNITS_PER_DP * settings.sensitivity,
                    settings.snap,
                    send,
                    ::haptic,
                )
            }
        }
    private val pieces = MediaPiece.entries.associateWith { settings.piece(it) }
    private val mark = AppMark()

    private val muteText = context.getString(R.string.surface_mute)
    private val unknownText = context.getString(R.string.surface_unknown)
    private val nothingPlaying = context.getString(R.string.surface_nothing_playing)
    private val hintLines = context.getString(R.string.surface_hints).split('\n')
    private val accessibilityActions: Map<AccessibilityNodeInfo.AccessibilityAction, () -> Unit> =
        mapOf(
            action(R.string.surface_play_pause) to { send(ActionId.PLAY_PAUSE.frame()) },
            action(R.string.surface_next) to { send(ActionId.NEXT_TRACK.frame()) },
            action(R.string.surface_previous) to { send(ActionId.PREVIOUS_TRACK.frame()) },
            action(R.string.surface_open_settings) to onOpenSettings,
            action(R.string.surface_keyboard) to ::toggleKeyboard,
        )

    // Geometry, all set in onSizeChanged so nothing is measured or allocated while drawing.
    private var perimeter = Perimeter(1f, 1f, 1f)
    private val centres = FloatArray(dials.size)
    private val exclusions = List(dials.size) { Rect() }
    private val nowPlayingBox = RectF()
    private val prevHit = RectF()
    private val playHit = RectF()
    private val nextHit = RectF()
    private val gearHit = RectF()
    private val keyboardHit = RectF()
    private var keyboardDown = false
    private var keyboardShown = false
    private var titleLine = ""
    private var subLine = ""
    private var statusLine = laptopName.uppercase()

    // The touch in progress.
    private var activeDial = -1
    private var lastS = 0f
    private var buttonDown: ActionId? = null
    private var gearDown = false
    private var fingerDown = false
    private var fingerX = 0f
    private var fingerY = 0f
    private val hit = FloatArray(2)
    private val pt = FloatArray(4)
    private val glyph = Path()

    private val tick = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = palette.ink }
    private val stroke =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = dp(Space.HAIR)
        }
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val mono =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.mono
            textAlign = Paint.Align.CENTER
        }
    private val titlePaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.sans
            textSize = sp(TITLE_SP)
        }

    /** The measured round trip, shown beside the laptop's name. */
    var rtt: Double = Double.NaN
        set(value) {
            field = value
            val name = laptopName.uppercase()
            statusLine = if (value.isNaN()) name else context.getString(R.string.surface_status, name, value)
            invalidate()
        }

    init {
        keepScreenOn = true
        isFocusable = true
        isFocusableInTouchMode = true
        contentDescription = context.getString(R.string.surface_description)
        applyState()
    }

    /** The laptop reported something new; [state] already holds it. */
    fun stateChanged() {
        applyState()
        rebuildText()
        invalidate()
    }

    private fun applyState() {
        for (dial in dials) {
            val control = dial.control ?: continue
            state.level(control)?.let { dial.fromLaptop(it, state.flag(control)) }
        }
    }

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        // The rulers bend round the display's own rounded corners; a square display gets a small bend anyway.
        val insets = rootWindowInsets
        var corner = 0
        if (insets != null) {
            for (position in CORNER_POSITIONS) corner = maxOf(corner, insets.getRoundedCorner(position)?.radius ?: 0)
        }
        perimeter = Perimeter(w.toFloat(), h.toFloat(), maxOf(corner.toFloat(), dp(MIN_BEND_DP)))
        dials.forEachIndexed { i, dial -> centres[i] = perimeter.lengthAt(dial.corner.toFloat()) }
        layoutPieces(w.toFloat(), h.toFloat())
        excludeBackGesture()
        rebuildText()
    }

    private fun layoutPieces(
        w: Float,
        h: Float,
    ) {
        val touch = dp(Space.TOUCH) / 2
        val (px, py) = pieces.getValue(MediaPiece.PLAY)
        val play = dp(PLAY_DP) / 2
        playHit.set(px * w - play, py * h - play, px * w + play, py * h + play)
        val (sx, sy) = pieces.getValue(MediaPiece.SKIP)
        val gap = dp(SKIP_GAP_DP)
        prevHit.set(sx * w - gap - touch, sy * h - touch, sx * w - gap + touch, sy * h + touch)
        nextHit.set(sx * w + gap - touch, sy * h - touch, sx * w + gap + touch, sy * h + touch)
        val (nx, ny) = pieces.getValue(MediaPiece.NOW_PLAYING)
        val half = minOf(dp(NOW_PLAYING_WIDTH_DP), w - 2 * dp(Space.L)) / 2
        val tall = dp(NOW_PLAYING_HEIGHT_DP) / 2
        nowPlayingBox.set(nx * w - half, ny * h - tall, nx * w + half, ny * h + tall)
        gearHit.set(w / 2 - touch, dp(GEAR_TOP_DP) - touch, w / 2 + touch, dp(GEAR_TOP_DP) + touch)
        keyboardHit.set(
            w / 2 - touch,
            h - dp(KEYBOARD_BOTTOM_DP) - touch,
            w / 2 + touch,
            h - dp(KEYBOARD_BOTTOM_DP) + touch,
        )
    }

    /** Keeps Android's back gesture off each corner dial; the bottom edge (home) cannot be claimed. */
    private fun excludeBackGesture() {
        val half = dp(RULER_HALF_DP)
        val step = dp(SAMPLE_DP)
        for (i in dials.indices) {
            val rect = exclusions[i]
            perimeter.point(centres[i] - half, pt)
            rect.set(pt[0].toInt(), pt[1].toInt(), pt[0].toInt(), pt[1].toInt())
            var s = centres[i] - half + step
            while (s <= centres[i] + half) {
                perimeter.point(s, pt)
                rect.union(pt[0].toInt(), pt[1].toInt())
                s += step
            }
            val depth = dp(CORNER_HIT_DP).toInt()
            rect.inset(-depth, -depth)
        }
        systemGestureExclusionRects = exclusions
    }

    private fun rebuildText() {
        val playing = state.nowPlaying
        titlePaint.color = if (playing.isEmpty()) palette.dim else palette.ink
        val room = nowPlayingBox.width() - dp(MARK_DP) - dp(Space.M)
        titleLine =
            TextUtils
                .ellipsize(
                    playing.ifEmpty { nothingPlaying },
                    titlePaint,
                    room,
                    TextUtils.TruncateAt.END,
                ).toString()
        val app = state.app.uppercase()
        val sub =
            when {
                state.duration > 0 && app.isNotEmpty() -> {
                    context.getString(
                        R.string.surface_app_time,
                        app,
                        Clock.format(state.position),
                        Clock.format(state.duration),
                    )
                }

                state.duration > 0 -> {
                    context.getString(R.string.surface_time, Clock.format(state.position), Clock.format(state.duration))
                }

                else -> {
                    app
                }
            }
        subText()
        subLine = TextUtils.ellipsize(sub, mono, room, TextUtils.TruncateAt.END).toString()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawColor(palette.background)
        drawStatus(canvas)
        drawNowPlaying(canvas)
        drawTransport(canvas)
        drawGear(canvas)
        drawKeyboard(canvas)
        if (showHints) drawHints(canvas)
        if (fingerDown) {
            fill.color = palette.ink
            canvas.drawCircle(fingerX, fingerY, dp(FINGER_DP) / 2, fill)
        }
        for (i in dials.indices) drawDial(canvas, i)
    }

    private fun drawStatus(canvas: Canvas) {
        mono.textAlign = Paint.Align.LEFT
        mono.textSize = sp(STATUS_SP)
        mono.letterSpacing = STATUS_TRACKING
        mono.color = palette.dim
        val textWidth = mono.measureText(statusLine)
        val dot = dp(STATUS_DOT_DP)
        val gap = dp(STATUS_GAP_DP)
        val start = width / 2f - (dot + gap + textWidth) / 2
        val y = dp(GEAR_TOP_DP) + dp(Space.XL)
        fill.color = palette.ink
        canvas.drawCircle(start + dot / 2, y - mono.textSize * CAP_CENTRE, dot / 2, fill)
        canvas.drawText(statusLine, start + dot + gap, y, mono)
        mono.textAlign = Paint.Align.CENTER
    }

    private fun drawNowPlaying(canvas: Canvas) {
        val box = nowPlayingBox
        val size = dp(MARK_DP)
        val cx = box.left + size / 2
        val cy = box.centerY() - dp(PROGRESS_BELOW_DP) / 2
        fill.color = palette.ink
        if (state.app.isEmpty()) {
            stroke.color = palette.line
            canvas.drawRect(cx - size / 2, cy - size / 2, cx + size / 2, cy + size / 2, stroke)
        } else {
            mark.draw(canvas, state.app, cx, cy, size, fill)
        }
        val textX = box.left + size + dp(Space.M)
        canvas.drawText(titleLine, textX, cy - dp(Space.XS), titlePaint)
        subText()
        mono.textAlign = Paint.Align.LEFT
        canvas.drawText(subLine, textX, cy + mono.textSize + dp(Space.S), mono)
        mono.textAlign = Paint.Align.CENTER
        val progressY = box.bottom
        stroke.color = palette.line
        canvas.drawLine(box.left, progressY, box.right, progressY, stroke)
        val fraction =
            if (state.duration > 0) {
                state.position.coerceAtLeast(0).toFloat() / state.duration
            } else {
                (state.level(ControlId.MEDIA_POSITION) ?: 0) / Dial.MAX_LEVEL
            }
        stroke.color = palette.ink
        canvas.drawLine(box.left, progressY, box.left + box.width() * fraction, progressY, stroke)
    }

    private fun subText() {
        mono.textSize = sp(SUB_SP)
        mono.letterSpacing = SUB_TRACKING
        mono.color = palette.dim
    }

    private fun drawHints(canvas: Canvas) {
        mono.textAlign = Paint.Align.CENTER
        mono.textSize = sp(HINT_SP)
        mono.letterSpacing = HINT_TRACKING
        mono.color = palette.dim
        val y = height / 2f - (hintLines.size - 1) * dp(HINT_GAP_DP) / 2
        hintLines.forEachIndexed { i, line -> canvas.drawText(line, width / 2f, y + i * dp(HINT_GAP_DP), mono) }
    }

    private fun drawTransport(canvas: Canvas) {
        fill.color = palette.ink
        drawSkip(canvas, prevHit.centerX(), prevHit.centerY(), forward = false)
        drawSkip(canvas, nextHit.centerX(), nextHit.centerY(), forward = true)
        val cx = playHit.centerX()
        val cy = playHit.centerY()
        canvas.drawCircle(cx, cy, dp(PLAY_DP) / 2, fill)
        fill.color = palette.background
        if (state.flag(ControlId.MEDIA_POSITION)) {
            val bar = dp(PAUSE_BAR_W_DP)
            val tall = dp(PAUSE_BAR_H_DP)
            val gap = dp(PAUSE_GAP_DP)
            canvas.drawRect(cx - gap / 2 - bar, cy - tall / 2, cx - gap / 2, cy + tall / 2, fill)
            canvas.drawRect(cx + gap / 2, cy - tall / 2, cx + gap / 2 + bar, cy + tall / 2, fill)
        } else {
            val size = dp(PLAY_TRIANGLE_DP)
            glyph.reset()
            glyph.moveTo(cx - size * TRIANGLE_BACK, cy - size / 2)
            glyph.lineTo(cx + size * TRIANGLE_FRONT, cy)
            glyph.lineTo(cx - size * TRIANGLE_BACK, cy + size / 2)
            glyph.close()
            canvas.drawPath(glyph, fill)
        }
    }

    /** A skip mark: a triangle pointing the way, with a bar at its far end. */
    private fun drawSkip(
        canvas: Canvas,
        cx: Float,
        cy: Float,
        forward: Boolean,
    ) {
        val w = dp(SKIP_W_DP)
        val h = dp(SKIP_H_DP)
        val bar = dp(SKIP_BAR_DP)
        val gap = dp(SKIP_BAR_GAP_DP)
        val dir = if (forward) 1f else -1f
        val start = cx - dir * (w + gap + bar) / 2
        glyph.reset()
        glyph.moveTo(start, cy - h / 2)
        glyph.lineTo(start + dir * w, cy)
        glyph.lineTo(start, cy + h / 2)
        glyph.close()
        canvas.drawPath(glyph, fill)
        val barStart = start + dir * (w + gap)
        canvas.drawRect(
            minOf(barStart, barStart + dir * bar),
            cy - h / 2,
            maxOf(barStart, barStart + dir * bar),
            cy + h / 2,
            fill,
        )
    }

    /** A keyboard: a rounded outline with two rows of keys and a space bar. */
    private fun drawKeyboard(canvas: Canvas) {
        val cx = keyboardHit.centerX()
        val cy = keyboardHit.centerY()
        val w = dp(KEYBOARD_W_DP)
        val h = dp(KEYBOARD_H_DP)
        stroke.color = if (keyboardShown) palette.ink else palette.dim
        stroke.strokeWidth = dp(GEAR_STROKE_DP)
        canvas.drawRoundRect(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2, dp(Space.XS), dp(Space.XS), stroke)
        val key = dp(KEY_DP)
        for (row in 0 until 2) {
            val y = cy - h / 2 + dp(KEY_INSET_DP) + row * key * 2
            var x = cx - w / 2 + dp(KEY_INSET_DP) + row * key
            while (x + key <= cx + w / 2 - dp(KEY_INSET_DP)) {
                canvas.drawPoint(x + key / 2, y, stroke)
                x += key * 2
            }
        }
        val bar = cy + h / 2 - dp(KEY_INSET_DP)
        canvas.drawLine(cx - w * SPACE_BAR, bar, cx + w * SPACE_BAR, bar, stroke)
        stroke.strokeWidth = dp(Space.HAIR)
    }

    /** Shows or hides the phone's keyboard; what is typed goes to the laptop as text. */
    private fun toggleKeyboard() {
        val manager = context.getSystemService(InputMethodManager::class.java) ?: return
        keyboardShown = !keyboardShown
        if (keyboardShown) {
            requestFocus()
            manager.showSoftInput(this, 0)
        } else {
            manager.hideSoftInputFromWindow(windowToken, 0)
        }
        invalidate()
    }

    override fun onCheckIsTextEditor(): Boolean = true

    override fun onCreateInputConnection(outAttrs: EditorInfo): InputConnection {
        outAttrs.inputType = EditorInfo.TYPE_CLASS_TEXT or EditorInfo.TYPE_TEXT_FLAG_NO_SUGGESTIONS
        outAttrs.imeOptions = EditorInfo.IME_FLAG_NO_FULLSCREEN or EditorInfo.IME_FLAG_NO_EXTRACT_UI
        return object : BaseInputConnection(this, false) {
            override fun commitText(
                text: CharSequence?,
                newCursorPosition: Int,
            ): Boolean {
                if (!text.isNullOrEmpty()) send(Frame.Text(LaptopState.TYPE, text.toString()))
                return true
            }

            override fun deleteSurroundingText(
                beforeLength: Int,
                afterLength: Int,
            ): Boolean {
                repeat(beforeLength) { send(Frame.Text(LaptopState.TYPE, BACKSPACE)) }
                return true
            }

            override fun sendKeyEvent(event: KeyEvent?): Boolean {
                if (event?.action != KeyEvent.ACTION_DOWN) return true
                when (event.keyCode) {
                    KeyEvent.KEYCODE_DEL -> {
                        send(Frame.Text(LaptopState.TYPE, BACKSPACE))
                    }

                    KeyEvent.KEYCODE_ENTER -> {
                        send(Frame.Text(LaptopState.TYPE, NEWLINE))
                    }

                    else -> {
                        val c = event.unicodeChar
                        if (c != 0) send(Frame.Text(LaptopState.TYPE, c.toChar().toString()))
                    }
                }
                return true
            }
        }
    }

    /** A gear: a ring with eight teeth. */
    private fun drawGear(canvas: Canvas) {
        val cx = gearHit.centerX()
        val cy = gearHit.centerY()
        val r = dp(GEAR_DP) / 2
        stroke.color = palette.dim
        stroke.strokeWidth = dp(GEAR_STROKE_DP)
        canvas.drawCircle(cx, cy, r * GEAR_RING, stroke)
        for (i in 0 until GEAR_TEETH) {
            val a = Math.toRadians(i * FULL_TURN / GEAR_TEETH)
            val ux = cos(a).toFloat()
            val uy = sin(a).toFloat()
            canvas.drawLine(
                cx + ux * r * GEAR_TOOTH_FROM,
                cy + uy * r * GEAR_TOOTH_FROM,
                cx + ux * r,
                cy + uy * r,
                stroke,
            )
        }
        stroke.strokeWidth = dp(Space.HAIR)
    }

    private fun drawDial(
        canvas: Canvas,
        i: Int,
    ) {
        val dial = dials[i]
        val centre = centres[i]
        val notch = dp(Dial.NOTCH_DP)
        val half = dp(RULER_HALF_DP)
        val ruler = dp(dial.rulerDp)
        val grow = if (dial.armed) ARMED_GROWTH else 1f
        var first = ceil((-half - ruler) / notch).toInt()
        var last = floor((half - ruler) / notch).toInt()
        if (dial.control != null) {
            first = maxOf(first, -floor(dial.rulerLengthDp / Dial.NOTCH_DP).toInt())
            last = minOf(last, 0)
        }
        for (n in first..last) {
            val major = n % MAJOR_EVERY == 0
            perimeter.point(centre + ruler + n * notch, pt)
            val depth = dp(if (major) MAJOR_TICK_DP else MINOR_TICK_DP) * grow
            tick.strokeWidth = dp(if (major) MAJOR_STROKE_DP else MINOR_STROKE_DP)
            tick.alpha =
                when {
                    major -> MAJOR_ALPHA
                    dial.armed -> ARMED_MINOR_ALPHA
                    else -> MINOR_ALPHA
                }
            canvas.drawLine(pt[0], pt[1], pt[0] + pt[2] * depth, pt[1] + pt[3] * depth, tick)
        }

        perimeter.point(centre, pt)
        tick.alpha = OPAQUE
        tick.strokeWidth = dp(INDICATOR_STROKE_DP)
        val reach = dp(INDICATOR_DP) * grow
        canvas.drawLine(pt[0], pt[1], pt[0] + pt[2] * reach, pt[1] + pt[3] * reach, tick)

        // The label and the number sit inside the corner, on the diagonal.
        val depth = dp(LABEL_DP)
        val ax = pt[0] + pt[2] * depth
        val ay = pt[1] + pt[3] * depth
        val labelSize = sp(DIAL_LABEL_SP)
        val valueSize = sp(if (dial.armed) ARMED_VALUE_SP else DIAL_VALUE_SP)
        val number = valueText(dial)
        val block = if (number.isEmpty()) labelSize else labelSize + dp(Space.XS) + valueSize
        val top = ay - block / 2
        mono.textAlign = Paint.Align.CENTER
        mono.letterSpacing = Type.TRACKING_WIDE
        mono.textSize = labelSize
        mono.color = palette.dim
        canvas.drawText(dial.label, ax, top + labelSize * BASELINE, mono)
        if (number.isNotEmpty()) {
            mono.letterSpacing = 0f
            mono.textSize = valueSize
            mono.color = palette.ink
            canvas.drawText(number, ax, top + labelSize + dp(Space.XS) + valueSize * BASELINE, mono)
        }
    }

    private fun valueText(dial: Dial): String =
        when (dial.kind) {
            DialKind.VOLUME, DialKind.MIC -> {
                when {
                    !dial.known -> unknownText
                    dial.flag -> muteText
                    else -> dial.value.toString()
                }
            }

            DialKind.BRIGHTNESS -> {
                if (dial.known) dial.value.toString() else unknownText
            }

            DialKind.MEDIA -> {
                when {
                    state.duration > 0 && dial.armed -> {
                        Clock.format(
                            (dial.level / Dial.MAX_LEVEL * state.duration).roundToInt(),
                        )
                    }

                    state.duration > 0 -> {
                        Clock.format(state.position)
                    }

                    dial.known -> {
                        "${dial.value}%"
                    }

                    else -> {
                        unknownText
                    }
                }
            }

            DialKind.ZOOM -> {
                if (dial.steps > 0) "+${dial.steps}" else dial.steps.toString()
            }

            DialKind.APP_SWITCHER -> {
                ""
            }
        }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                requestUnbufferedDispatch(event)
                down(event)
            }

            MotionEvent.ACTION_POINTER_DOWN -> {
                if (onTrackpad()) feed(TrackpadRecognizer.Action.DOWN, event, exclude = -1)
            }

            MotionEvent.ACTION_MOVE -> {
                move(event)
            }

            MotionEvent.ACTION_POINTER_UP -> {
                if (onTrackpad()) feed(TrackpadRecognizer.Action.UP, event, exclude = event.actionIndex)
            }

            MotionEvent.ACTION_UP -> {
                val openSettings = gearDown && gearHit.contains(event.x, event.y)
                val toggleKeys = keyboardDown && keyboardHit.contains(event.x, event.y)
                if (up(event)) performClick()
                if (openSettings) onOpenSettings()
                if (toggleKeys) toggleKeyboard()
            }

            MotionEvent.ACTION_CANCEL -> {
                cancel(event)
            }

            else -> {
                return super.onTouchEvent(event)
            }
        }
        invalidate()
        return true
    }

    private fun down(event: MotionEvent) {
        val x = event.x
        val y = event.y
        val button = transportAt(x, y)
        when {
            gearHit.contains(x, y) -> {
                gearDown = true
            }

            keyboardHit.contains(x, y) -> {
                keyboardDown = true
            }

            button != null -> {
                buttonDown = button
                haptic()
                send(button.frame())
            }

            else -> {
                activeDial = dialAt(x, y)
                if (activeDial >= 0) {
                    dials[activeDial].down()
                    lastS = hit[0]
                } else {
                    feed(TrackpadRecognizer.Action.DOWN, event, exclude = -1)
                    finger(x, y)
                }
            }
        }
    }

    private fun move(event: MotionEvent) {
        if (activeDial >= 0) {
            for (h in 0 until event.historySize) slideTo(event.getHistoricalX(h), event.getHistoricalY(h))
            slideTo(event.x, event.y)
        } else if (onTrackpad()) {
            for (h in 0 until event.historySize) feedHistorical(event, h)
            feed(TrackpadRecognizer.Action.MOVE, event, exclude = -1)
            finger(event.x, event.y)
        }
    }

    private fun slideTo(
        x: Float,
        y: Float,
    ) {
        perimeter.project(x, y, hit)
        val moved = perimeter.delta(lastS, hit[0])
        lastS = hit[0]
        // Deep inside the screen the nearest edge can flip from one side of a corner to the other; that is a
        // jump along the path, not a slide, so it moves nothing.
        if (abs(moved) > dp(JUMP_DP)) return
        dials[activeDial].slide(moved / density, TrackpadRecognizer.SLOP_DP)
    }

    /** Ends the touch. True when it was a click: the gear, or a tap on a dial. */
    private fun up(event: MotionEvent): Boolean {
        when {
            gearDown -> {
                gearDown = false
                return gearHit.contains(event.x, event.y)
            }

            keyboardDown -> {
                keyboardDown = false
                return keyboardHit.contains(event.x, event.y)
            }

            buttonDown != null -> {
                buttonDown = null
            }

            activeDial >= 0 -> {
                val dial = dials[activeDial]
                activeDial = -1
                return dial.up()
            }

            else -> {
                feed(TrackpadRecognizer.Action.UP, event, exclude = event.actionIndex)
                fingerDown = false
            }
        }
        return false
    }

    private fun cancel(event: MotionEvent) {
        if (activeDial >= 0) dials[activeDial].cancel()
        activeDial = -1
        buttonDown = null
        gearDown = false
        keyboardDown = false
        fingerDown = false
        trackpad.handle(TrackpadRecognizer.Action.CANCEL, FloatArray(0), FloatArray(0), event.eventTime)
    }

    private fun onTrackpad(): Boolean = activeDial < 0 && buttonDown == null && !gearDown && !keyboardDown

    private fun finger(
        x: Float,
        y: Float,
    ) {
        fingerDown = true
        fingerX = x
        fingerY = y
    }

    private fun transportAt(
        x: Float,
        y: Float,
    ): ActionId? =
        when {
            prevHit.contains(x, y) -> ActionId.PREVIOUS_TRACK
            playHit.contains(x, y) -> ActionId.PLAY_PAUSE
            nextHit.contains(x, y) -> ActionId.NEXT_TRACK
            else -> null
        }

    /** The dial under ([x], [y]), or -1. Leaves the nearest point on the edge in [hit]. */
    private fun dialAt(
        x: Float,
        y: Float,
    ): Int {
        perimeter.project(x, y, hit)
        if (hit[1] > dp(CORNER_HIT_DP)) return -1
        var best = -1
        var bestGap = dp(RULER_HALF_DP + HIT_SLACK_DP)
        for (i in dials.indices) {
            val gap = abs(perimeter.delta(centres[i], hit[0]))
            if (gap < bestGap) {
                best = i
                bestGap = gap
            }
        }
        return best
    }

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

    override fun onInitializeAccessibilityNodeInfo(info: AccessibilityNodeInfo) {
        super.onInitializeAccessibilityNodeInfo(info)
        for (action in accessibilityActions.keys) info.addAction(action)
    }

    override fun performAccessibilityAction(
        action: Int,
        arguments: Bundle?,
    ): Boolean {
        val run = accessibilityActions.entries.firstOrNull { it.key.id == action }?.value
        if (run == null) return super.performAccessibilityAction(action, arguments)
        run()
        return true
    }

    private fun action(resId: Int) =
        AccessibilityNodeInfo.AccessibilityAction(View.generateViewId(), context.getString(resId))

    private fun haptic() {
        if (hapticsOn) performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
    }

    private fun dp(value: Float): Float = value * density

    private fun sp(value: Float): Float =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, resources.displayMetrics)

    companion object {
        const val CORNERS = 4

        /** The pieces' sizes in dp; the layout screen draws them at these too. */
        const val PLAY_DP = 64f
        const val SKIP_GAP_DP = 56f
        const val NOW_PLAYING_WIDTH_DP = 320f
        const val NOW_PLAYING_HEIGHT_DP = 56f
        const val MARK_DP = 36f

        private val CORNER_POSITIONS =
            intArrayOf(
                RoundedCorner.POSITION_TOP_LEFT,
                RoundedCorner.POSITION_TOP_RIGHT,
                RoundedCorner.POSITION_BOTTOM_RIGHT,
                RoundedCorner.POSITION_BOTTOM_LEFT,
            )
        private const val MIN_BEND_DP = 24f
        private const val RULER_HALF_DP = 190f
        private const val MAJOR_TICK_DP = 34f
        private const val MINOR_TICK_DP = 22f
        private const val MAJOR_STROKE_DP = 2f
        private const val MINOR_STROKE_DP = 1.2f
        private const val MAJOR_EVERY = 5
        private const val MAJOR_ALPHA = 230
        private const val MINOR_ALPHA = 128
        private const val ARMED_MINOR_ALPHA = 190
        private const val OPAQUE = 255
        private const val ARMED_GROWTH = 1.25f
        private const val INDICATOR_DP = 48f
        private const val INDICATOR_STROKE_DP = 2.5f
        private const val LABEL_DP = 96f
        private const val DIAL_LABEL_SP = 11f
        private const val DIAL_VALUE_SP = 26f
        private const val ARMED_VALUE_SP = 32f
        private const val BASELINE = 0.8f
        private const val CORNER_HIT_DP = 96f
        private const val HIT_SLACK_DP = 12f
        private const val JUMP_DP = 64f
        private const val SAMPLE_DP = 8f
        private const val STATUS_SP = 9f
        private const val STATUS_TRACKING = 0.16f
        private const val STATUS_DOT_DP = 5f
        private const val STATUS_GAP_DP = 7f
        private const val TITLE_SP = 14f
        private const val SUB_SP = 9f
        private const val SUB_TRACKING = 0.16f
        private const val PROGRESS_BELOW_DP = 16f
        private const val HINT_SP = 9f
        private const val HINT_TRACKING = 0.16f
        private const val HINT_GAP_DP = 16f
        private const val FINGER_DP = 10f
        private const val GEAR_TOP_DP = 40f
        private const val KEYBOARD_BOTTOM_DP = 40f
        private const val KEYBOARD_W_DP = 28f
        private const val KEYBOARD_H_DP = 18f
        private const val KEY_DP = 3f
        private const val KEY_INSET_DP = 4f
        private const val SPACE_BAR = 0.25f
        private const val BACKSPACE = "\b"
        private const val NEWLINE = "\n"
        private const val GEAR_DP = 22f
        private const val GEAR_STROKE_DP = 1.5f
        private const val GEAR_RING = 0.55f
        private const val GEAR_TOOTH_FROM = 0.75f
        private const val GEAR_TEETH = 8
        private const val FULL_TURN = 360.0
        private const val SKIP_W_DP = 9f
        private const val SKIP_H_DP = 12f
        private const val SKIP_BAR_DP = 2f
        private const val SKIP_BAR_GAP_DP = 2f
        private const val PAUSE_BAR_W_DP = 4f
        private const val PAUSE_BAR_H_DP = 18f
        private const val PAUSE_GAP_DP = 6f
        private const val PLAY_TRIANGLE_DP = 18f
        private const val TRIANGLE_BACK = 0.4f
        private const val TRIANGLE_FRONT = 0.6f
        private const val CAP_CENTRE = 0.35f
    }
}
