package me.akshitbansal.edgepad.surface

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.drawable.Drawable
import android.os.Bundle
import android.os.SystemClock
import android.text.TextPaint
import android.text.TextUtils
import android.util.TypedValue
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.RoundedCorner
import android.view.View
import android.view.ViewConfiguration
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
 * The control surface: a ruler at each corner that holds a dial, the media pieces wherever the user put
 * them, gear, keyboard, lock, gamepad and macro buttons at the top, and everything else is the trackpad. A
 * touch that starts inside a corner's zone is that dial's; one that starts on a media piece or a button is
 * a button press; any other is the trackpad. The lock button walks the surface through [PadMode], which is
 * the only thing that can take a piece of it away — and which a drawn shape can walk too, since a locked
 * pad is one of the things a shape may be bound to.
 *
 * A single finger held still on the trackpad becomes a stroke; [TrackpadRecognizer] decides that, [Shapes]
 * reads it, and [runShape] runs whatever it matched. A locked pad refuses shapes the way it refuses
 * everything else, because the touch never reaches the recogniser at all.
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
    private val onOpenMacros: () -> Unit,
) : View(context) {
    /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
    constructor(context: Context) : this(context, Settings(context), LaptopState(), {}, {}, {}, {}, {})

    private val density = resources.displayMetrics.density
    private val palette = Palette.of(context)
    private val backdrop = Backdrop(settings, density, palette.background)

    // The one colour every control is drawn in, and its dimmed forms; the user may override the theme's.
    private val ink = settings.controlColor ?: if (backdrop.isDark) Color.WHITE else Color.BLACK
    private val dim = ink and RGB_MASK or DIM_ALPHA
    private val faint = ink and RGB_MASK or FAINT_ALPHA

    /**
     * The shapes the user has drawn, decoded once here rather than on every touch. A stored set that
     * failed to decode is no set at all, which is [Shapes.decode]'s whole contract, and leaves the
     * trackpad exactly as it was before the feature existed.
     */
    private val shapes = Shapes.decode(settings.shapes).orEmpty()

    // The recogniser is handed a shape sink only when there is something to recognise: with nothing
    // bound, a press and hold must stay the nothing it has always been rather than quietly stop the
    // pointer for a third of a second on a phone whose owner never drew a shape.
    private val trackpad =
        TrackpadRecognizer(
            density,
            settings.naturalScroll,
            settings.pointerSpeed,
            settings.scrollSpeed,
            settings::gesture,
            if (shapes.isEmpty()) null else ::runShape,
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
    // The lock button gets a second copy of its own icon in the full ink, drawn whenever the pad is out of
    // [PadMode.NORMAL]. Focus announces itself — the dials and the media are simply gone — but a locked pad
    // looks exactly like a working one, and a surface that silently swallows every touch reads as a crash
    // rather than as a mode. Both are tinted here because onDraw may not make a Drawable.
    private val lockIconOn = icon(R.drawable.ic_lock, ink)
    private val lockButton = TopButton(icon(R.drawable.ic_lock, dim), side = 0f, open = { tapLock() })
    private val topButtons =
        listOf(
            TopButton(icon(R.drawable.ic_settings, dim), side = -2f, open = onOpenSettings),
            TopButton(icon(R.drawable.ic_keyboard, dim), side = -1f, open = onOpenKeyboard),
            lockButton,
            TopButton(icon(R.drawable.ic_gamepad_2, dim), side = 1f, open = onOpenGamepad),
            TopButton(icon(R.drawable.ic_macro, dim), side = 2f, open = onOpenMacros),
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
            action(R.id.action_pad_lock, R.string.surface_lock) to { tapLock() },
            action(R.id.action_gamepad, R.string.surface_gamepad) to onOpenGamepad,
            action(R.id.action_macros, R.string.surface_macros) to onOpenMacros,
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

    /**
     * What the surface is showing and answering, and when the lock button last took a tap.
     *
     * The mode lives in the view and nowhere else: it is not written to [Settings] and it is not offered on
     * the settings screen. The surface is pinned to one orientation, so it is never rebuilt underneath the
     * user while they are standing on it, and a mode that only has to outlive the view needs no storage. It
     * must not outlive it either: a phone that came back up silently locked, with the user having no memory
     * of locking it, would read as broken rather than as obedient.
     */
    private var mode = PadMode.NORMAL
    private var lastLockTap = Long.MIN_VALUE

    // The touch in progress.
    private var activeDial = -1
    private var lastS = 0f
    private var buttonDown: ActionId? = null

    /** The top button the finger went down on, until it lifts or the touch is cancelled. */
    private var pressedTop: TopButton? = null
    private var scrubbing = false
    private var scrubFraction = 0f

    /** What the surface last saw of [TrackpadRecognizer.shaping]; the tick fires on the change, not on the state. */
    private var drawingShape = false

    // The fingers on the trackpad, drawn as dots, with a tail behind a single finger and a ring between two.
    private var fingerCount = 0
    private val fingerXs = FloatArray(MAX_POINTERS)
    private val fingerYs = FloatArray(MAX_POINTERS)

    // Handed to the recogniser with a count, and refilled per sample. Separate from fingerXs/fingerYs,
    // which hold what onDraw paints: feedHistorical walks positions the finger has already left, and
    // those must not reach the screen. requestUnbufferedDispatch means samples arrive as fast as the
    // digitiser makes them, so a fresh pair of arrays here is garbage on the one path built to be fast.
    private val touchXs = FloatArray(MAX_POINTERS)
    private val touchYs = FloatArray(MAX_POINTERS)
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
        // The mode rides on the state description rather than on announceForAccessibility, which API 36
        // deprecated and which an app targeting 36 or later has ignored ever since. A polite live region
        // is the replacement Android points at: setting stateDescription raises a state-changed event and
        // the screen reader reads it. Nothing else on this view ever changes it, so nothing else speaks.
        accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
        stateDescription = modeLabel()
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
            // The refresh dial's range is the laptop's list of rates, which only arrives once it connects.
            if (dial.kind == DialKind.REFRESH) {
                dial.maxLevel = (state.refreshRates.size - 1).coerceAtLeast(0).toFloat()
            }
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
            // Upright, only the top corners have the screen to themselves and keep the deep zone: the
            // bottom ones sit near the media pieces, an edge midpoint sits where the thumb swipes, and
            // sideways every slot is near the middle. The rest grab less of the trackpad.
            val roomy = dial.corner < FIRST_BOTTOM_CORNER
            depths[i] = dp(if (w > h || !roomy) CORNER_HIT_NEAR_DP else CORNER_HIT_DP)
        }
        backdrop.resize(w, h)
        layoutPieces(w.toFloat(), h.toFloat())
        // No ruler runs under the buttons at the top, and neighbours stop short of each other — which is
        // what keeps two dials on the same edge clear of each other.
        // A dial the user puts in the top-middle slot is the one exception: it is centred inside the
        // buttons' zone, so the cut cannot push it out of its own place, and it shares the space.
        // The zone is read off the row rather than fixed, so the spacing and the button count are tied
        // together through it: four buttons reaching ±1.5 at 64dp claimed 132dp either side of the centre,
        // and five reaching ±2 at the same spacing would claim 164dp — more than a 360dp phone has to give,
        // leaving 16dp of edge at each end and both top dials nowhere to sit. See [TOP_BUTTON_OFFSET_DP].
        val topCentre = perimeter.lengthAt(TOP_CENTRE)
        val reach = topButtons.maxOf { abs(it.side) } * TOP_BUTTON_OFFSET_DP
        val zone = dp(reach + Space.TOUCH / 2 + Space.M)
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

    /** Keeps Android's back gesture off each dial; the bottom edge (home) cannot be claimed. */
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
            // Both tints were made in the constructor; picking between them here allocates nothing.
            val glyph = if (button === lockButton && mode != PadMode.NORMAL) lockIconOn else button.icon
            drawIcon(canvas, glyph, button.hit.centerX(), button.hit.centerY(), dp(ICON_DP))
        }
        if (showHints) drawHints(canvas)
        drawFingers(canvas)
        // Focus hides the dials; [dialAt] stops answering for them in the same breath, so nothing is left
        // taking touches where there is nothing drawn.
        if (mode != PadMode.FOCUS) {
            for (i in dials.indices) drawDial(canvas, i)
        }
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
                // Index 0 is the newest sample; each older segment is thinner and fainter. While a shape
                // is being drawn the tail is solid instead: a stroke that fades out behind the finger
                // reads as the pointer moving, which is the one thing shape mode has just stopped doing.
                val fade = if (drawingShape) 1f else 1f - i.toFloat() / trailLength
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
        // The progress line only when no dial scrubs; then it can be slid itself.
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

    /**
     * The media pieces exist only while the laptop has a player open; otherwise their room is trackpad.
     * Focus takes them away as well, and because this is the one gate [down] hit-tests through, they stop
     * answering the finger at the same moment they stop being drawn.
     */
    private fun mediaShown(): Boolean =
        mode != PadMode.FOCUS && (state.app.isNotEmpty() || state.nowPlaying.isNotEmpty())

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

            DialKind.REFRESH -> {
                // The level is an index; the rate it stands for is only knowable from the laptop's list.
                val rate = if (dial.known) state.refreshRates.getOrNull(dial.value) else null
                if (rate != null) context.getString(R.string.dial_refresh_value, rate) else unknownText
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
                cancelTouches(event.eventTime)
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
                when {
                    activeDial >= 0 -> {
                        dials[activeDial].down()
                        lastS = hit[0]
                    }

                    // A locked pad refuses the touch here too, not only on the samples that follow: the
                    // finger never reaches the recogniser and no dot is drawn for it, so the surface does
                    // not spend the gesture pretending to listen.
                    onTrackpad() -> {
                        feed(TrackpadRecognizer.Action.DOWN, event, exclude = -1)
                        trailLength = 0
                        fingers(event, exclude = -1)
                    }
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
                shapeFeedback()
            }
        }
    }

    /**
     * The tick that tells the finger its press has become a stroke, and the flag [drawFingers] draws the
     * trail solid on. The recogniser owns the decision; this only notices the moment it changes, so one
     * hold ticks once rather than once per touch sample.
     */
    private fun shapeFeedback() {
        if (trackpad.shaping == drawingShape) return
        drawingShape = trackpad.shaping
        if (drawingShape) haptic()
    }

    /**
     * The finger lifted after drawing something. Recognise it, and run whatever it was bound to — or, more
     * often than not, nothing at all. Silence is the right answer for a stroke that matched nothing: the
     * alternative is the nearest binding firing on a scrawl that was never meant to be one.
     */
    private fun runShape(
        xs: FloatArray,
        ys: FloatArray,
        count: Int,
    ) {
        drawingShape = false
        val points = Shapes.normalise(xs, ys, count, density) ?: return
        val shape = Shapes.match(points, shapes) ?: return
        haptic()
        when (val target = shape.target) {
            is ShapeTarget.Run -> {
                trackpad.oneShot(target.action)
            }

            is ShapeTarget.Macro -> {
                // The index and nothing else, exactly as the macro grid sends it: the laptop's own list
                // decides what a slot launches, and that is the whole security model behind macros.
                send(Frame.RunAction(ActionId.MACRO_BASE.id + target.slot))
            }

            is ShapeTarget.Pad -> {
                // Phone-local: nothing crosses the link. The lock button's own live region carries it,
                // because a surface that rearranges itself under a finger is otherwise silent.
                mode = target.mode
                stateDescription = modeLabel()
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
                drawingShape = false
            }
        }
        return false
    }

    /**
     * Drops everything in flight: the gesture the recogniser is holding, the dial being slid, the button
     * under the finger. The system calls this when it takes the touch away, and [tapLock] calls it when the
     * mode changes underneath one. That second caller is the one that matters — going to [PadMode.PAD_LOCKED]
     * means [onTrackpad] is about to refuse every further sample, so a tap-and-hold drag with the left
     * button down would never see its UP and the laptop would be left holding the button forever.
     */
    private fun cancelTouches(time: Long) {
        if (activeDial >= 0) dials[activeDial].cancel()
        activeDial = -1
        buttonDown = null
        pressedTop = null
        scrubbing = false
        fingerCount = 0
        trailLength = 0
        drawingShape = false
        trackpad.handle(TrackpadRecognizer.Action.CANCEL, touchXs, touchYs, time, count = 0)
    }

    private fun onTrackpad(): Boolean =
        mode != PadMode.PAD_LOCKED && activeDial < 0 && buttonDown == null && pressedTop == null && !scrubbing

    /**
     * A tap on the lock button, from a finger or from the accessibility action. [SystemClock.uptimeMillis]
     * is the clock [MotionEvent.getEventTime] is stamped from, so both paths share one double-tap window,
     * and the handful of milliseconds between the finger lifting and this running are nothing against it.
     */
    private fun tapLock() {
        val now = SystemClock.uptimeMillis()
        val next = mode.next(now, lastLockTap, LOCK_SECOND_TAP_MS)
        lastLockTap = now
        mode = next
        cancelTouches(now)
        haptic()
        // The other four buttons open a screen, which is its own answer; this one changes the surface in
        // place, so the tick, the lit icon and the live region are all the confirmation there is.
        stateDescription = modeLabel()
        invalidate()
    }

    private fun modeLabel(): String =
        context.getString(
            when (mode) {
                PadMode.NORMAL -> R.string.surface_mode_normal
                PadMode.FOCUS -> R.string.surface_mode_focus
                PadMode.PAD_LOCKED -> R.string.surface_mode_locked
            },
        )

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
        // Focus does not draw them, so nothing here may claim a touch for them either.
        if (mode == PadMode.FOCUS) return -1
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

    /** Fills [touchXs]/[touchYs] with every finger but [exclude], capped as [fingers] already caps. */
    private fun feed(
        action: TrackpadRecognizer.Action,
        event: MotionEvent,
        exclude: Int,
    ) {
        var n = 0
        for (i in 0 until event.pointerCount) {
            if (i == exclude || n == MAX_POINTERS) continue
            touchXs[n] = event.getX(i)
            touchYs[n] = event.getY(i)
            n++
        }
        trackpad.handle(action, touchXs, touchYs, event.eventTime, count = n)
    }

    private fun feedHistorical(
        event: MotionEvent,
        h: Int,
    ) {
        val n = minOf(event.pointerCount, MAX_POINTERS)
        for (i in 0 until n) {
            touchXs[i] = event.getHistoricalX(i, h)
            touchYs[i] = event.getHistoricalY(i, h)
        }
        trackpad.handle(TrackpadRecognizer.Action.MOVE, touchXs, touchYs, event.getHistoricalEventTime(h), count = n)
    }

    override fun onInitializeAccessibilityNodeInfo(info: AccessibilityNodeInfo) {
        // super fills the node's state description from the view's own, which [tapLock] keeps current: a
        // screen reader landing on a locked surface is told why the pad will not answer before it tries.
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
     * One of the buttons floating at the top of the surface. [side] places it across the centre of the top
     * edge in steps of [TOP_BUTTON_OFFSET_DP]: -1 one step left of it, 0 on it, 1 one step right. An
     * odd-sized row sits on whole steps — the five buttons are at 0 and ±1 and ±2, with the lock on the
     * centre — where an even-sized one needs half steps to stay centred.
     */
    private class TopButton(
        val icon: Drawable,
        val side: Float,
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

        /** Corners 0 and 1 are the top two, where the hit zone may reach further down the screen. */
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

        /**
         * The step between neighbouring top buttons, and not only spacing: [onSizeChanged] multiplies the
         * outermost button's side by it to work out how much of the top edge the row keeps to itself, so
         * this number and the number of buttons move together. Five buttons at the old 64dp would have
         * reserved 164dp either side of the centre — a 360dp phone's whole top edge and then some — and
         * pushed both top dials off it. 56dp brings that back to 148dp.
         */
        private const val TOP_BUTTON_OFFSET_DP = 56f
        private const val TOP_CENTRE = 0.5f
        private const val DIAL_GAP_DP = 16f

        /**
         * How long after the tap that focused the surface a second tap still means "and lock the pad"
         * rather than "put the dials back". The owner asked for a second; the platform's own double-tap
         * window is used instead because a full second is long enough that an immediate corrective tap —
         * the one meaning "no, undo that" — would land inside it and lock the pad rather than unfocus it.
         * One constant, and nothing else reads the window: raise it here if a second really is wanted.
         */
        private val LOCK_SECOND_TAP_MS = ViewConfiguration.getDoubleTapTimeout().toLong()
    }
}

/**
 * What the control surface is showing and answering, and the table the lock button walks.
 *
 * Pure, and outside the view deliberately: no Android type reaches it, so the transitions are tested on the
 * JVM the way [TrackpadRecognizer]'s gesture table is, on a machine with no emulator.
 */
enum class PadMode {
    /** Dials, media and trackpad, all live. */
    NORMAL,

    /** Dials and media are neither drawn nor hit-tested; the trackpad and the five top buttons remain. */
    FOCUS,

    /** Dials and media stay live, and the trackpad refuses every touch, so a palm or a pocket does nothing. */
    PAD_LOCKED,
    ;

    /**
     * Where a tap on the lock button at [now] lands, given the tap before it at [previousTap] and a window
     * of [windowMs]. One tap out of [NORMAL] focuses at once rather than waiting to see whether a second
     * arrives, because a mode that takes a double-tap timeout to appear feels like a button that missed;
     * a second tap inside the window then upgrades focus to a locked pad, and any later tap, from either
     * mode, puts the whole surface back.
     *
     * [windowMs] is added to [previousTap], never subtracted from [now]: the never-tapped sentinel is
     * Long.MIN_VALUE and subtracting from it would overflow into a window that swallows the first tap.
     */
    fun next(
        now: Long,
        previousTap: Long,
        windowMs: Long,
    ): PadMode =
        when (this) {
            NORMAL -> FOCUS
            FOCUS -> if (now <= previousTap + windowMs) PAD_LOCKED else NORMAL
            PAD_LOCKED -> NORMAL
        }
}
