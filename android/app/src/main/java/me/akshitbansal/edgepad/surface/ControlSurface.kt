package me.akshitbansal.edgepad.surface

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.drawable.Drawable
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
import kotlin.math.hypot
import kotlin.math.roundToInt

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

    private val trackpad =
        TrackpadRecognizer(
            density,
            settings.naturalScroll,
            settings.pointerSpeed,
            settings.scrollSpeed,
            settings::gesture,
            send,
        )
    private val hapticsOn = settings.haptics
    private val showHints = settings.hints
    private val scrubOnADial = settings.hasDial(DialKind.MEDIA)
    private val dials: List<Dial> =
        (0 until Perimeter.CORNERS).mapNotNull { corner ->
            settings.corner(corner)?.let { kind ->
                kind.dial(
                    corner,
                    context.getString(kind.shortRes),
                    Dial.BASE_UNITS_PER_DP * settings.sensitivityOf(kind),
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
    private val logo = AppLogo(context, backdrop.isDark)

    // Lucide icons, tinted once: the buttons in the dim ink, skips in ink, play and pause cut out of the disc.
    private val topButtons =
        listOf(
            TopButton(icon(R.drawable.ic_settings, dim), side = -1, open = onOpenSettings),
            TopButton(icon(R.drawable.ic_keyboard, dim), side = 0, open = onOpenKeyboard),
            TopButton(icon(R.drawable.ic_gamepad_2, dim), side = 1, open = onOpenGamepad),
        )
    private val skipBackIcon = icon(R.drawable.ic_skip_back, ink)
    private val skipForwardIcon = icon(R.drawable.ic_skip_forward, ink)
    private val playIcon = icon(R.drawable.ic_play, if (backdrop.isDark) Color.BLACK else Color.WHITE)
    private val pauseIcon = icon(R.drawable.ic_pause, if (backdrop.isDark) Color.BLACK else Color.WHITE)

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
    private val depths = FloatArray(dials.size)
    private val after = FloatArray(dials.size)
    private val before = FloatArray(dials.size)
    private val keepOut = FloatArray(2)
    private val exclusions = List(dials.size) { Rect() }
    private val nowPlayingBox = RectF()
    private val progressHit = RectF()
    private val prevHit = RectF()
    private val playHit = RectF()
    private val nextHit = RectF()
    private var titleLine = ""
    private var subLine = ""

    // The touch in progress.
    private var activeDial = -1
    private var lastS = 0f
    private var buttonDown: ActionId? = null

    /** The top button the finger went down on, until it lifts or the touch is cancelled. */
    private var pressedTop: TopButton? = null
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

    private val stroke =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = dp(Space.HAIR)
        }
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val mono =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
            textAlign = Paint.Align.CENTER
        }
    private val titlePaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
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
        dials.forEachIndexed { i, dial ->
            centres[i] = perimeter.lengthAt(dial.corner.toFloat())
            // Bottom corners sit near the media pieces, and sideways every corner is near the middle:
            // those grab less of the trackpad. Upright, the top corners keep the deep zone.
            val near = w > h || dial.corner >= FIRST_BOTTOM_CORNER
            depths[i] = dp(if (near) CORNER_HIT_NEAR_DP else CORNER_HIT_DP)
        }
        backdrop.resize(w, h)
        layoutPieces(w.toFloat(), h.toFloat())
        // No ruler runs under the buttons at the top, and neighbours stop short of each other.
        val topCentre = perimeter.lengthAt(TOP_CENTRE)
        val zone = dp(TOP_BUTTON_OFFSET_DP + Space.TOUCH / 2 + Space.M)
        keepOut[0] = topCentre - zone
        keepOut[1] = topCentre + zone
        DialSpan.compute(centres, dp(painter.halfLengthDp), perimeter.length, keepOut, dp(DIAL_GAP_DP), after, before)
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
        for (button in topButtons) {
            val cx = w / 2 + button.side * offset
            button.hit.set(cx - touch, top - touch, cx + touch, top + touch)
        }
    }

    /** Keeps Android's back gesture off each corner dial; the bottom edge (home) cannot be claimed. */
    private fun excludeBackGesture() {
        val step = dp(SAMPLE_DP)
        for (i in dials.indices) {
            val rect = exclusions[i]
            perimeter.point(centres[i] - before[i], pt)
            rect.set(pt[0].toInt(), pt[1].toInt(), pt[0].toInt(), pt[1].toInt())
            var s = centres[i] - before[i] + step
            while (s <= centres[i] + after[i]) {
                perimeter.point(s, pt)
                rect.union(pt[0].toInt(), pt[1].toInt())
                s += step
            }
            val depth = depths[i].toInt()
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
        // On a narrow screen the dials leave less than the minimum; the minimum wins and the box overlaps them.
        val roomMax = maxOf(w - 2 * dp(painter.halfLengthDp + Space.L) - fixed, media(NOW_PLAYING_MIN_TEXT_DP))
        subText()
        val text = maxOf(titlePaint.measureText(titleLine), mono.measureText(subLine))
        val half = (fixed + text.coerceIn(media(NOW_PLAYING_MIN_TEXT_DP), roomMax)) / 2
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
        val room =
            maxOf(
                width - 2 * dp(painter.halfLengthDp + Space.L) - media(LOGO_DP) - dp(Space.M),
                media(NOW_PLAYING_MIN_TEXT_DP),
            )
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
        if (mediaShown()) {
            drawNowPlaying(canvas)
            drawTransport(canvas)
        }
        for (button in topButtons) {
            drawIcon(canvas, button.icon, button.hit.centerX(), button.hit.centerY(), dp(ICON_DP))
        }
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

    /** The media pieces exist only while the laptop has a player open; otherwise their room is trackpad. */
    private fun mediaShown(): Boolean = state.app.isNotEmpty() || state.nowPlaying.isNotEmpty()

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
        drawIcon(canvas, skipBackIcon, prevHit.centerX(), prevHit.centerY(), media(SKIP_DP))
        drawIcon(canvas, skipForwardIcon, nextHit.centerX(), nextHit.centerY(), media(SKIP_DP))
        val cx = playHit.centerX()
        val cy = playHit.centerY()
        fill.color = ink
        canvas.drawCircle(cx, cy, media(PLAY_DP) / 2, fill)
        // Cut out of the disc in the background's colour: a pause while playing, else a play.
        val glyph = if (state.flag(ControlId.MEDIA_POSITION)) pauseIcon else playIcon
        drawIcon(canvas, glyph, cx, cy, media(PLAY_ICON_DP))
    }

    private fun drawIcon(
        canvas: Canvas,
        icon: Drawable,
        cx: Float,
        cy: Float,
        size: Float,
    ) {
        val half = (size / 2).toInt()
        icon.setBounds(cx.toInt() - half, cy.toInt() - half, cx.toInt() + half, cy.toInt() + half)
        icon.draw(canvas)
    }

    private fun icon(
        id: Int,
        color: Int,
    ): Drawable = checkNotNull(context.getDrawable(id)).mutate().apply { setTint(color) }

    private fun drawDial(
        canvas: Canvas,
        i: Int,
    ) {
        val dial = dials[i]
        painter.draw(
            canvas,
            perimeter,
            centres[i],
            after[i],
            before[i],
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
                val top = pressedTop
                pressedTop = null
                when {
                    // performClick() is called from here rather than from up(): Android lint wants it
                    // lexically inside onTouchEvent. A finger that slid off a button opens nothing.
                    top != null -> {
                        if (top.hit.contains(event.x, event.y)) {
                            performClick()
                            top.open()
                        }
                    }

                    up(event) -> {
                        performClick()
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
        val media = mediaShown()
        val button = if (media) transportAt(x, y) else null
        val top = topButtons.firstOrNull { it.hit.contains(x, y) }
        when {
            top != null -> {
                pressedTop = top
            }

            button != null -> {
                buttonDown = button
                haptic()
                send(button.frame())
            }

            media && !scrubOnADial && progressHit.contains(x, y) -> {
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

    /** Ends the touch and says whether it tapped a dial; a top button is settled in [onTouchEvent]. */
    private fun up(event: MotionEvent): Boolean {
        when {
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
                return dial.up()
            }

            else -> {
                feed(TrackpadRecognizer.Action.UP, event, exclude = event.actionIndex)
                fingerCount = 0
                trailLength = 0
            }
        }
        return false
    }

    private fun cancel(event: MotionEvent) {
        if (activeDial >= 0) dials[activeDial].cancel()
        activeDial = -1
        buttonDown = null
        pressedTop = null
        scrubbing = false
        fingerCount = 0
        trailLength = 0
        trackpad.handle(TrackpadRecognizer.Action.CANCEL, FloatArray(0), FloatArray(0), event.eventTime)
    }

    private fun onTrackpad(): Boolean = activeDial < 0 && buttonDown == null && pressedTop == null && !scrubbing

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
        var best = -1
        var bestGap = Float.MAX_VALUE
        for (i in dials.indices) {
            if (hit[1] > depths[i]) continue
            val delta = perimeter.delta(centres[i], hit[0])
            val reach = if (delta >= 0) after[i] else before[i]
            val gap = abs(delta)
            if (gap < reach + dp(HIT_SLACK_DP) && gap < bestGap) {
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

    /**
     * One of the buttons floating at the top of the surface. [side] places it across the centre of the
     * top edge: -1 one step left of it, 0 on it, 1 one step right.
     */
    private class TopButton(
        val icon: Drawable,
        val side: Int,
        val open: () -> Unit,
    ) {
        val hit = RectF()
    }

    private fun haptic() {
        if (hapticsOn) performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
    }

    private fun dp(value: Float): Float = value * density

    /** A media piece's dp, at the user's media size. */
    private fun media(value: Float): Float = value * density * mediaScale

    private fun sp(value: Float): Float =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, resources.displayMetrics)

    companion object {
        /** The pieces' sizes in dp; the layout screen draws them at these too. */
        const val PLAY_DP = 52f
        const val SKIP_GAP_DP = 52f
        const val NOW_PLAYING_WIDTH_DP = 280f
        const val NOW_PLAYING_HEIGHT_DP = 52f
        const val LOGO_DP = 40f
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
        private const val CORNER_HIT_NEAR_DP = 56f
        private const val FIRST_BOTTOM_CORNER = 2
        private const val ICON_DP = 22f
        private const val SKIP_DP = 22f
        private const val PLAY_ICON_DP = 22f
        private const val HIT_SLACK_DP = 12f
        private const val JUMP_DP = 64f
        private const val SAMPLE_DP = 8f
        private const val TITLE_SP = 14f
        private const val SUB_SP = 10f
        private const val SUB_TRACKING = 0.12f
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
        private const val TOP_BUTTON_OFFSET_DP = 64f
        private const val TOP_CENTRE = 0.5f
        private const val DIAL_GAP_DP = 16f
    }
}
