package me.akshitbansal.edgepad.surface

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.Rect
import android.graphics.RectF
import android.os.Bundle
import android.text.TextPaint
import android.text.TextUtils
import android.util.TypedValue
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.RoundedCorner
import android.view.View
import android.view.accessibility.AccessibilityNodeInfo
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
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.roundToInt
import kotlin.math.sin

/**
 * The control surface: a ruler wrapped round each corner that holds a dial, the media pieces wherever the
 * user put them, gear, keyboard and gamepad buttons at the top, and everything else is the trackpad. A touch
 * that starts inside a corner's zone is that dial's; one that starts on a media piece or a button is a
 * button press; any other is the trackpad.
 *
 * Everything is drawn here rather than built from child views: a touch reaches the recogniser with no view
 * hierarchy in between, dispatch is unbuffered so samples arrive as they happen, and every historical
 * sample in a MOVE is fed through. What the laptop reported lives in [state], which outlives this view.
 */
class ControlSurface(
    context: Context,
    settings: Settings,
    private val state: LaptopState,
    private val send: (Frame) -> Unit,
    private val onOpenSettings: () -> Unit,
    private val onOpenKeyboard: () -> Unit,
    private val onOpenGamepad: () -> Unit,
) : View(context) {
    /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
    constructor(context: Context) : this(context, Settings(context), LaptopState(), {}, {}, {}, {})

    private val density = resources.displayMetrics.density
    private val palette = Palette.of(context)
    private val backdrop = Backdrop(settings, density, palette.background)

    // The one colour every control is drawn in, and its dimmed forms; the user may override the theme's.
    private val ink = settings.controlColor ?: if (backdrop.isDark) Color.WHITE else Color.BLACK
    private val dim = ink and RGB_MASK or DIM_ALPHA
    private val faint = ink and RGB_MASK or FAINT_ALPHA

    private val trackpad = TrackpadRecognizer(density, settings.naturalScroll, settings::gesture, send)
    private val hapticsOn = settings.haptics
    private val showHints = settings.hints
    private val scrubOnADial = settings.hasDial(DialKind.MEDIA)
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
    private val painter =
        RulerPainter(resources.displayMetrics, settings.dialLength, settings.dialHeight)
    private val pieces = MediaPiece.entries.associateWith { settings.piece(it) }
    private val mediaScale = settings.mediaScale
    private val logo = AppLogo(resources, backdrop.isDark)

    private val muteText = context.getString(R.string.surface_mute)
    private val unknownText = context.getString(R.string.surface_unknown)
    private val nothingPlaying = context.getString(R.string.surface_nothing_playing)
    private val hintLines = context.getString(R.string.surface_hints).split('\n')
    private val accessibilityActions: Map<AccessibilityNodeInfo.AccessibilityAction, () -> Unit> =
        mapOf(
            action(R.id.action_play_pause, R.string.surface_play_pause) to { send(ActionId.PLAY_PAUSE.frame()) },
            action(R.id.action_next_track, R.string.surface_next) to { send(ActionId.NEXT_TRACK.frame()) },
            action(R.id.action_previous_track, R.string.surface_previous) to { send(ActionId.PREVIOUS_TRACK.frame()) },
            action(R.id.action_open_settings, R.string.surface_open_settings) to onOpenSettings,
            action(R.id.action_keyboard, R.string.surface_keyboard) to onOpenKeyboard,
            action(R.id.action_gamepad, R.string.surface_gamepad) to onOpenGamepad,
        )

    // Geometry, all set in onSizeChanged so nothing is measured or allocated while drawing.
    private var perimeter = Perimeter(1f, 1f, 1f)
    private val centres = FloatArray(dials.size)
    private val exclusions = List(dials.size) { Rect() }
    private val nowPlayingBox = RectF()
    private val progressHit = RectF()
    private val prevHit = RectF()
    private val playHit = RectF()
    private val nextHit = RectF()
    private val gearHit = RectF()
    private val keyboardHit = RectF()
    private val gamepadHit = RectF()
    private var titleLine = ""
    private var subLine = ""

    // The touch in progress.
    private var activeDial = -1
    private var lastS = 0f
    private var buttonDown: ActionId? = null
    private var gearDown = false
    private var keyboardDown = false
    private var gamepadDown = false
    private var scrubbing = false
    private var scrubFraction = 0f

    // The fingers on the trackpad, drawn as dots, with a tail behind a single finger and a ring between two.
    private var fingerCount = 0
    private val fingerXs = FloatArray(MAX_POINTERS)
    private val fingerYs = FloatArray(MAX_POINTERS)
    private val trail = FloatArray(TRAIL * 2)
    private var trailHead = 0
    private var trailLength = 0
    private val hit = FloatArray(2)
    private val pt = FloatArray(4)
    private val glyph = Path()

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
            textSize = sp(TITLE_SP) * mediaScale
        }

    init {
        keepScreenOn = true
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
        backdrop.resize(w, h)
        layoutPieces(w.toFloat(), h.toFloat())
        excludeBackGesture()
        rebuildText()
    }

    private fun layoutPieces(
        w: Float,
        h: Float,
    ) {
        val touch = dp(Space.TOUCH) / 2
        val (tx, ty) = pieces.getValue(MediaPiece.TRANSPORT)
        val play = media(PLAY_DP) / 2
        playHit.set(tx * w - play, ty * h - play, tx * w + play, ty * h + play)
        val gap = media(SKIP_GAP_DP)
        prevHit.set(tx * w - gap - touch, ty * h - touch, tx * w - gap + touch, ty * h + touch)
        nextHit.set(tx * w + gap - touch, ty * h - touch, tx * w + gap + touch, ty * h + touch)
        layoutNowPlaying(w, h)
        val offset = dp(TOP_BUTTON_OFFSET_DP)
        val top = dp(TOP_DP)
        keyboardHit.set(w / 2 - touch, top - touch, w / 2 + touch, top + touch)
        gearHit.set(w / 2 - offset - touch, top - touch, w / 2 - offset + touch, top + touch)
        gamepadHit.set(w / 2 + offset - touch, top - touch, w / 2 + offset + touch, top + touch)
    }

    /** Keeps Android's back gesture off each corner dial; the bottom edge (home) cannot be claimed. */
    private fun excludeBackGesture() {
        val half = dp(painter.halfLengthDp)
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

    /**
     * The now-playing box is as wide as its text needs, centred on the piece's position, and no wider than
     * the screen minus the corner dials' reach; longer text is cut with an ellipsis.
     */
    private fun layoutNowPlaying(
        w: Float,
        h: Float,
    ) {
        val (nx, ny) = pieces.getValue(MediaPiece.NOW_PLAYING)
        val fixed = media(LOGO_DP) + dp(Space.M)
        val roomMax = w - 2 * dp(painter.halfLengthDp + Space.L) - fixed
        subText()
        val text = maxOf(titlePaint.measureText(titleLine), mono.measureText(subLine))
        val half = (fixed + text.coerceIn(media(NOW_PLAYING_MIN_TEXT_DP), roomMax.coerceAtLeast(0f))) / 2
        val tall = media(NOW_PLAYING_HEIGHT_DP) / 2
        nowPlayingBox.set(nx * w - half, ny * h - tall, nx * w + half, ny * h + tall)
        val touch = dp(Space.TOUCH) / 2
        progressHit.set(
            nowPlayingBox.left,
            nowPlayingBox.bottom - touch,
            nowPlayingBox.right,
            nowPlayingBox.bottom + touch,
        )
    }

    private fun rebuildText() {
        val playing = state.nowPlaying
        titlePaint.color = if (playing.isEmpty()) dim else ink
        val room = width - 2 * dp(painter.halfLengthDp + Space.L) - media(LOGO_DP) - dp(Space.M)
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
        if (width > 0) layoutNowPlaying(width.toFloat(), height.toFloat())
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        backdrop.draw(canvas)
        drawNowPlaying(canvas)
        drawTransport(canvas)
        drawGear(canvas)
        drawKeyboard(canvas)
        drawGamepad(canvas)
        if (showHints) drawHints(canvas)
        drawFingers(canvas)
        for (i in dials.indices) drawDial(canvas, i)
    }

    /**
     * One finger: a cone-shaped tail from faint and thin at the back to solid at the finger. Two: both
     * dots, the line between them and a ring on their span, so a pinch reads as one. More: the dots.
     */
    private fun drawFingers(canvas: Canvas) {
        if (fingerCount == 1 && trailLength > 1) {
            stroke.strokeCap = Paint.Cap.ROUND
            var i = 1
            while (i < trailLength) {
                // Index 0 is the newest sample; each older segment is thinner and fainter.
                val fade = 1f - i.toFloat() / trailLength
                stroke.color = ink and RGB_MASK or ((fade * TRAIL_ALPHA).roundToInt() shl ALPHA_SHIFT)
                stroke.strokeWidth = dp(FINGER_DP) * fade
                canvas.drawLine(trailX(i - 1), trailY(i - 1), trailX(i), trailY(i), stroke)
                i++
            }
            stroke.strokeCap = Paint.Cap.BUTT
            stroke.strokeWidth = dp(Space.HAIR)
        }
        if (fingerCount == 2) {
            val mx = (fingerXs[0] + fingerXs[1]) / 2
            val my = (fingerYs[0] + fingerYs[1]) / 2
            val span = hypot(fingerXs[1] - fingerXs[0], fingerYs[1] - fingerYs[0])
            stroke.color = faint
            canvas.drawLine(fingerXs[0], fingerYs[0], fingerXs[1], fingerYs[1], stroke)
            stroke.color = dim
            canvas.drawCircle(mx, my, span / 2, stroke)
        }
        fill.color = ink
        for (i in 0 until fingerCount) canvas.drawCircle(fingerXs[i], fingerYs[i], dp(FINGER_DP) / 2, fill)
    }

    private fun trailX(back: Int): Float = trail[((trailHead - back + TRAIL) % TRAIL) * 2]

    private fun trailY(back: Int): Float = trail[((trailHead - back + TRAIL) % TRAIL) * 2 + 1]

    private fun drawNowPlaying(canvas: Canvas) {
        val box = nowPlayingBox
        val size = media(LOGO_DP)
        val cx = box.left + size / 2
        val cy = box.centerY() - dp(PROGRESS_BELOW_DP) / 2
        if (state.app.isEmpty()) {
            stroke.color = faint
            canvas.drawRect(cx - size / 2, cy - size / 2, cx + size / 2, cy + size / 2, stroke)
        } else {
            logo.draw(canvas, state.app, cx, cy, size, ink)
        }
        val textX = box.left + size + dp(Space.M)
        canvas.drawText(titleLine, textX, cy - dp(Space.XS), titlePaint)
        subText()
        mono.textAlign = Paint.Align.LEFT
        canvas.drawText(subLine, textX, cy + mono.textSize + dp(Space.S), mono)
        mono.textAlign = Paint.Align.CENTER
        // The progress line only when no corner scrubs; then it can be slid itself.
        if (scrubOnADial) return
        val progressY = box.bottom
        stroke.color = faint
        stroke.strokeWidth = dp(PROGRESS_STROKE_DP)
        canvas.drawLine(box.left, progressY, box.right, progressY, stroke)
        stroke.color = ink
        val fraction = if (scrubbing) scrubFraction else playedFraction()
        canvas.drawLine(box.left, progressY, box.left + box.width() * fraction, progressY, stroke)
        stroke.strokeWidth = dp(Space.HAIR)
        fill.color = ink
        canvas.drawCircle(
            box.left + box.width() * fraction,
            progressY,
            dp(if (scrubbing) THUMB_HELD_DP else THUMB_DP) / 2,
            fill,
        )
    }

    private fun playedFraction(): Float =
        if (state.duration > 0) {
            state.position.coerceAtLeast(0).toFloat() / state.duration
        } else {
            (state.level(ControlId.MEDIA_POSITION) ?: 0) / Dial.MAX_LEVEL
        }

    private fun subText() {
        mono.textSize = sp(SUB_SP) * mediaScale
        mono.letterSpacing = SUB_TRACKING
        mono.color = dim
    }

    private fun drawHints(canvas: Canvas) {
        mono.textAlign = Paint.Align.CENTER
        mono.textSize = sp(HINT_SP)
        mono.letterSpacing = HINT_TRACKING
        mono.color = dim
        val y = height / 2f - (hintLines.size - 1) * dp(HINT_GAP_DP) / 2
        hintLines.forEachIndexed { i, line -> canvas.drawText(line, width / 2f, y + i * dp(HINT_GAP_DP), mono) }
    }

    private fun drawTransport(canvas: Canvas) {
        fill.color = ink
        drawSkip(canvas, prevHit.centerX(), prevHit.centerY(), forward = false)
        drawSkip(canvas, nextHit.centerX(), nextHit.centerY(), forward = true)
        val cx = playHit.centerX()
        val cy = playHit.centerY()
        canvas.drawCircle(cx, cy, media(PLAY_DP) / 2, fill)
        // The glyph is cut out of the disc in the background's colour: a pause while playing, else a play.
        fill.color = if (backdrop.isDark) Color.BLACK else Color.WHITE
        if (state.flag(ControlId.MEDIA_POSITION)) {
            val bar = media(PAUSE_BAR_W_DP)
            val tall = media(PAUSE_BAR_H_DP)
            val gap = media(PAUSE_GAP_DP)
            canvas.drawRect(cx - gap / 2 - bar, cy - tall / 2, cx - gap / 2, cy + tall / 2, fill)
            canvas.drawRect(cx + gap / 2, cy - tall / 2, cx + gap / 2 + bar, cy + tall / 2, fill)
        } else {
            val size = media(PLAY_TRIANGLE_DP)
            glyph.reset()
            glyph.moveTo(cx - size * TRIANGLE_BACK, cy - size / 2)
            glyph.lineTo(cx + size * TRIANGLE_FRONT, cy)
            glyph.lineTo(cx - size * TRIANGLE_BACK, cy + size / 2)
            glyph.close()
            canvas.drawPath(glyph, fill)
        }
        fill.color = ink
    }

    /** A skip mark: a triangle pointing the way, with a bar at its far end. */
    private fun drawSkip(
        canvas: Canvas,
        cx: Float,
        cy: Float,
        forward: Boolean,
    ) {
        val w = media(SKIP_W_DP)
        val h = media(SKIP_H_DP)
        val bar = media(SKIP_BAR_DP)
        val gap = media(SKIP_BAR_GAP_DP)
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

    /** A gear: eight square teeth round a rim, with a hole in the middle. */
    private fun drawGear(canvas: Canvas) {
        val cx = gearHit.centerX()
        val cy = gearHit.centerY()
        val outer = dp(GEAR_DP) / 2
        val inner = outer * GEAR_RIM
        glyph.reset()
        val step = FULL_TURN / (GEAR_TEETH * 2)
        for (i in 0 until GEAR_TEETH * 2) {
            val r = if (i % 2 == 0) outer else inner
            val a0 = Math.toRadians(i * step - step * GEAR_TOOTH)
            val a1 = Math.toRadians(i * step + step * GEAR_TOOTH)
            val x0 = cx + cos(a0).toFloat() * r
            val y0 = cy + sin(a0).toFloat() * r
            if (i == 0) glyph.moveTo(x0, y0) else glyph.lineTo(x0, y0)
            glyph.lineTo(cx + cos(a1).toFloat() * r, cy + sin(a1).toFloat() * r)
        }
        glyph.close()
        stroke.color = dim
        stroke.strokeWidth = dp(BUTTON_STROKE_DP)
        stroke.strokeJoin = Paint.Join.ROUND
        canvas.drawPath(glyph, stroke)
        canvas.drawCircle(cx, cy, outer * GEAR_HOLE, stroke)
        stroke.strokeWidth = dp(Space.HAIR)
    }

    /** A keyboard: a rounded outline with two rows of keys and a space bar. */
    private fun drawKeyboard(canvas: Canvas) {
        val cx = keyboardHit.centerX()
        val cy = keyboardHit.centerY()
        val w = dp(KEYBOARD_W_DP)
        val h = dp(KEYBOARD_H_DP)
        stroke.color = dim
        stroke.strokeWidth = dp(BUTTON_STROKE_DP)
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

    /** A gamepad: a wide rounded body with a small cross on the left and two dots on the right. */
    private fun drawGamepad(canvas: Canvas) {
        val cx = gamepadHit.centerX()
        val cy = gamepadHit.centerY()
        val w = dp(GAMEPAD_W_DP)
        val h = dp(GAMEPAD_H_DP)
        stroke.color = dim
        stroke.strokeWidth = dp(BUTTON_STROKE_DP)
        canvas.drawRoundRect(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2, h / 2, h / 2, stroke)
        val arm = dp(GAMEPAD_CROSS_DP)
        val lx = cx - w * GAMEPAD_SIDE
        canvas.drawLine(lx - arm, cy, lx + arm, cy, stroke)
        canvas.drawLine(lx, cy - arm, lx, cy + arm, stroke)
        val rx = cx + w * GAMEPAD_SIDE
        val dot = dp(GAMEPAD_DOT_DP)
        fill.color = dim
        canvas.drawCircle(rx - dot, cy + dot, dot / 2, fill)
        canvas.drawCircle(rx + dot, cy - dot, dot / 2, fill)
        stroke.strokeWidth = dp(Space.HAIR)
    }

    private fun drawDial(
        canvas: Canvas,
        i: Int,
    ) {
        val dial = dials[i]
        painter.draw(
            canvas,
            perimeter,
            centres[i],
            dial.rulerDp,
            if (dial.control != null) dial.rulerLengthDp else null,
            dial.armed,
            ink,
            dim,
            dial.label,
            valueText(dial),
            pt,
        )
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
                if (onTrackpad()) {
                    feed(TrackpadRecognizer.Action.DOWN, event, exclude = -1)
                    fingers(event, exclude = -1)
                }
            }

            MotionEvent.ACTION_MOVE -> {
                move(event)
            }

            MotionEvent.ACTION_POINTER_UP -> {
                if (onTrackpad()) {
                    feed(TrackpadRecognizer.Action.UP, event, exclude = event.actionIndex)
                    fingers(event, exclude = event.actionIndex)
                }
            }

            MotionEvent.ACTION_UP -> {
                when (up(event)) {
                    Tapped.GEAR -> {
                        performClick()
                        onOpenSettings()
                    }

                    Tapped.KEYBOARD -> {
                        performClick()
                        onOpenKeyboard()
                    }

                    Tapped.GAMEPAD -> {
                        performClick()
                        onOpenGamepad()
                    }

                    Tapped.DIAL -> {
                        performClick()
                    }

                    Tapped.NOTHING -> {
                        Unit
                    }
                }
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

            gamepadHit.contains(x, y) -> {
                gamepadDown = true
            }

            button != null -> {
                buttonDown = button
                haptic()
                send(button.frame())
            }

            !scrubOnADial && progressHit.contains(x, y) -> {
                scrubbing = true
                scrubTo(x)
            }

            else -> {
                activeDial = dialAt(x, y)
                if (activeDial >= 0) {
                    dials[activeDial].down()
                    lastS = hit[0]
                } else {
                    feed(TrackpadRecognizer.Action.DOWN, event, exclude = -1)
                    trailLength = 0
                    fingers(event, exclude = -1)
                }
            }
        }
    }

    private fun move(event: MotionEvent) {
        when {
            scrubbing -> {
                scrubTo(event.x)
            }

            activeDial >= 0 -> {
                for (h in 0 until event.historySize) slideTo(event.getHistoricalX(h), event.getHistoricalY(h))
                slideTo(event.x, event.y)
            }

            onTrackpad() -> {
                for (h in 0 until event.historySize) feedHistorical(event, h)
                feed(TrackpadRecognizer.Action.MOVE, event, exclude = -1)
                fingers(event, exclude = -1)
            }
        }
    }

    private fun scrubTo(x: Float) {
        scrubFraction = ((x - nowPlayingBox.left) / nowPlayingBox.width()).coerceIn(0f, 1f)
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

    private enum class Tapped { NOTHING, GEAR, KEYBOARD, GAMEPAD, DIAL }

    /** Ends the touch and says what, if anything, it tapped. */
    private fun up(event: MotionEvent): Tapped {
        when {
            gearDown -> {
                gearDown = false
                return if (gearHit.contains(event.x, event.y)) Tapped.GEAR else Tapped.NOTHING
            }

            keyboardDown -> {
                keyboardDown = false
                return if (keyboardHit.contains(event.x, event.y)) Tapped.KEYBOARD else Tapped.NOTHING
            }

            gamepadDown -> {
                gamepadDown = false
                return if (gamepadHit.contains(event.x, event.y)) Tapped.GAMEPAD else Tapped.NOTHING
            }

            buttonDown != null -> {
                buttonDown = null
            }

            scrubbing -> {
                scrubbing = false
                send(ControlId.MEDIA_POSITION.set((scrubFraction * Dial.MAX_LEVEL).roundToInt()))
            }

            activeDial >= 0 -> {
                val dial = dials[activeDial]
                activeDial = -1
                return if (dial.up()) Tapped.DIAL else Tapped.NOTHING
            }

            else -> {
                feed(TrackpadRecognizer.Action.UP, event, exclude = event.actionIndex)
                fingerCount = 0
                trailLength = 0
            }
        }
        return Tapped.NOTHING
    }

    private fun cancel(event: MotionEvent) {
        if (activeDial >= 0) dials[activeDial].cancel()
        activeDial = -1
        buttonDown = null
        gearDown = false
        keyboardDown = false
        gamepadDown = false
        scrubbing = false
        fingerCount = 0
        trailLength = 0
        trackpad.handle(TrackpadRecognizer.Action.CANCEL, FloatArray(0), FloatArray(0), event.eventTime)
    }

    private fun onTrackpad(): Boolean =
        activeDial < 0 && buttonDown == null && !gearDown && !keyboardDown && !gamepadDown && !scrubbing

    /** Records where every finger still down is, and extends the tail when there is just one. */
    private fun fingers(
        event: MotionEvent,
        exclude: Int,
    ) {
        var n = 0
        for (i in 0 until event.pointerCount) {
            if (i == exclude || n == MAX_POINTERS) continue
            fingerXs[n] = event.getX(i)
            fingerYs[n] = event.getY(i)
            n++
        }
        fingerCount = n
        if (n != 1) {
            trailLength = 0
            return
        }
        trailHead = (trailHead + 1) % TRAIL
        trail[trailHead * 2] = fingerXs[0]
        trail[trailHead * 2 + 1] = fingerYs[0]
        trailLength = minOf(trailLength + 1, TRAIL)
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
        var bestGap = dp(painter.halfLengthDp + HIT_SLACK_DP)
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

    private fun action(
        id: Int,
        labelRes: Int,
    ) = AccessibilityNodeInfo.AccessibilityAction(id, context.getString(labelRes))

    private fun haptic() {
        if (hapticsOn) performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
    }

    private fun dp(value: Float): Float = value * density

    /** A media piece's dp, at the user's media size. */
    private fun media(value: Float): Float = value * density * mediaScale

    private fun sp(value: Float): Float =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, resources.displayMetrics)

    companion object {
        const val CORNERS = 4

        /** The pieces' sizes in dp; the layout screen draws them at these too. */
        const val PLAY_DP = 48f
        const val SKIP_GAP_DP = 52f
        const val NOW_PLAYING_WIDTH_DP = 280f
        const val NOW_PLAYING_HEIGHT_DP = 52f
        const val LOGO_DP = 32f
        private const val NOW_PLAYING_MIN_TEXT_DP = 120f

        private val CORNER_POSITIONS =
            intArrayOf(
                RoundedCorner.POSITION_TOP_LEFT,
                RoundedCorner.POSITION_TOP_RIGHT,
                RoundedCorner.POSITION_BOTTOM_RIGHT,
                RoundedCorner.POSITION_BOTTOM_LEFT,
            )
        private const val RGB_MASK = 0x00FFFFFF
        private const val DIM_ALPHA = 0x8C000000.toInt()
        private const val FAINT_ALPHA = 0x40000000
        private const val MIN_BEND_DP = 24f
        private const val CORNER_HIT_DP = 96f
        private const val HIT_SLACK_DP = 12f
        private const val JUMP_DP = 64f
        private const val SAMPLE_DP = 8f
        private const val TITLE_SP = 14f
        private const val SUB_SP = 9f
        private const val SUB_TRACKING = 0.16f
        private const val PROGRESS_BELOW_DP = 16f
        private const val PROGRESS_STROKE_DP = 2f
        private const val THUMB_DP = 8f
        private const val THUMB_HELD_DP = 14f
        private const val HINT_SP = 9f
        private const val HINT_TRACKING = 0.16f
        private const val HINT_GAP_DP = 16f
        private const val FINGER_DP = 10f
        private const val MAX_POINTERS = 10
        private const val TRAIL = 18
        private const val TRAIL_ALPHA = 200f
        private const val ALPHA_SHIFT = 24
        private const val TOP_DP = 40f
        private const val TOP_BUTTON_OFFSET_DP = 32f
        private const val BUTTON_STROKE_DP = 1.5f
        private const val KEYBOARD_W_DP = 28f
        private const val KEYBOARD_H_DP = 18f
        private const val KEY_DP = 3f
        private const val KEY_INSET_DP = 4f
        private const val SPACE_BAR = 0.25f
        private const val GAMEPAD_W_DP = 30f
        private const val GAMEPAD_H_DP = 16f
        private const val GAMEPAD_CROSS_DP = 3f
        private const val GAMEPAD_DOT_DP = 2.5f
        private const val GAMEPAD_SIDE = 0.25f
        private const val GEAR_DP = 22f
        private const val GEAR_RIM = 0.72f
        private const val GEAR_TOOTH = 0.42f
        private const val GEAR_HOLE = 0.28f
        private const val GEAR_TEETH = 8
        private const val FULL_TURN = 360.0
        private const val SKIP_W_DP = 9f
        private const val SKIP_H_DP = 12f
        private const val SKIP_BAR_DP = 2f
        private const val SKIP_BAR_GAP_DP = 2f
        private const val PAUSE_BAR_W_DP = 4f
        private const val PAUSE_BAR_H_DP = 14f
        private const val PAUSE_GAP_DP = 5f
        private const val PLAY_TRIANGLE_DP = 14f
        private const val TRIANGLE_BACK = 0.4f
        private const val TRIANGLE_FRONT = 0.6f
    }
}
