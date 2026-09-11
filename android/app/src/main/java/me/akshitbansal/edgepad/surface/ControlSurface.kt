package me.akshitbansal.edgepad.surface

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.Rect
import android.graphics.RectF
import android.os.Bundle
import android.text.Layout
import android.text.StaticLayout
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
import kotlin.math.ceil
import kotlin.math.floor
import kotlin.math.roundToInt

/**
 * The control surface, after the owner's design: ruler dials along the edges (an L round a corner, a
 * straight ruler along an edge), what is playing at the top, a bordered trackpad in the middle, and the
 * transport with a way into Settings at the bottom. Portrait and landscape lay the middle out differently;
 * the dials keep their corners.
 *
 * Everything is drawn here rather than built from child views: a touch reaches the recogniser with no view
 * hierarchy in between, dispatch is unbuffered so samples arrive as they happen, and every historical
 * sample in a MOVE is fed through, so nothing the finger did is skipped. What the laptop reported lives in
 * [state], which outlives this view, so a rebuilt surface starts from the laptop's real values.
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
    private val trackpad = TrackpadRecognizer(density, settings.naturalScroll, send)
    private val hapticsOn = settings.haptics
    private val showHints = settings.hints
    private val dials: List<Dial> =
        DialKind.entries.mapNotNull { kind ->
            settings.placement(kind)?.let { placement ->
                val unitsPerDp = Dial.BASE_UNITS_PER_DP * settings.sensitivity
                kind.dial(placement, context.getString(kind.shortRes), unitsPerDp, settings.snap, send, ::haptic)
            }
        }

    private val muteText = context.getString(R.string.surface_mute)
    private val unknownText = context.getString(R.string.surface_unknown)
    private val settingsText = context.getString(R.string.settings_link)
    private val nothingPlaying = context.getString(R.string.surface_nothing_playing)
    private val hintLines = context.getString(R.string.surface_hints).split('\n')
    private val hintLine = context.getString(R.string.surface_hints_short)
    private val accessibilityActions: Map<AccessibilityNodeInfo.AccessibilityAction, () -> Unit> =
        mapOf(
            action(R.string.surface_play_pause) to { send(ActionId.PLAY_PAUSE.frame()) },
            action(R.string.surface_next) to { send(ActionId.NEXT_TRACK.frame()) },
            action(R.string.surface_previous) to { send(ActionId.PREVIOUS_TRACK.frame()) },
            action(R.string.surface_open_settings) to onOpenSettings,
        )

    // Geometry, all set in onSizeChanged so nothing is measured or allocated while drawing.
    private var perimeter = Perimeter(1f, 1f, 1f)
    private val centres = FloatArray(dials.size)
    private val exclusions = List(dials.size) { Rect() }
    private var landscape = false
    private val box = RectF()
    private val monogram = RectF()
    private val prevHit = RectF()
    private val playHit = RectF()
    private val nextHit = RectF()
    private val settingsHit = RectF()
    private var playRadius = 0f
    private var transportY = 0f
    private var statusY = 0f
    private var titleX = 0f
    private var titleY = 0f
    private var titleWidth = 1
    private var subX = 0f
    private var subY = 0f
    private var subWidth = 1f
    private var subAlign = Paint.Align.CENTER
    private var progressX = 0f
    private var progressY = 0f
    private var progressWidth = 0f
    private var titleLayout: StaticLayout? = null
    private var subLine = ""
    private var statusLine = laptopName.uppercase()

    // The touch in progress.
    private var activeDial = -1
    private var lastS = 0f
    private var mediaDown = false
    private var settingsDown = false
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
        dials.forEachIndexed { i, dial -> centres[i] = perimeter.lengthAt(dial.placement.at) }
        layoutFurniture(w.toFloat(), h.toFloat())
        excludeBackGesture()
        rebuildText()
    }

    private fun layoutFurniture(
        w: Float,
        h: Float,
    ) {
        landscape = w > h
        // Everything that is not a dial sits in one block at the top, clear of the top dials' arms.
        val corner = minOf(dp(CORNER_DP), (if (landscape) h else w) * CORNER_FRACTION)
        val blockWidth = minOf(dp(BLOCK_WIDTH_DP), w - 2 * (if (landscape) corner else dp(EDGE_HIT_DP) + dp(Space.L)))
        val left = (w - blockWidth) / 2
        statusY = (if (landscape) dp(LAND_STATUS_DP) else corner * STATUS_AT)
        val size = dp(MONOGRAM_DP)
        val rowTop = statusY + dp(Space.L)
        monogram.set(left, rowTop, left + size, rowTop + size)
        titleX = monogram.right + dp(Space.M)
        titleWidth = (left + blockWidth - titleX).toInt().coerceAtLeast(1)
        titleY = monogram.top
        subAlign = Paint.Align.LEFT
        subX = titleX
        subWidth = titleWidth.toFloat()
        subY = monogram.bottom + dp(SUB_BELOW_DP)
        progressX = left
        progressWidth = blockWidth
        progressY = subY + dp(Space.L)
        transportY = progressY + dp(Space.XL) + dp(PLAY_DP) / 2
        playRadius = dp(PLAY_DP) / 2
        val touch = dp(Space.TOUCH) / 2
        val gap = dp(TRANSPORT_GAP_DP)
        prevHit.set(w / 2 - gap - touch, transportY - touch, w / 2 - gap + touch, transportY + touch)
        nextHit.set(w / 2 + gap - touch, transportY - touch, w / 2 + gap + touch, transportY + touch)
        playHit.set(w / 2 - playRadius, transportY - playRadius, w / 2 + playRadius, transportY + playRadius)
        val link = h - dp(if (landscape) LAND_SETTINGS_BOTTOM_DP else SETTINGS_BOTTOM_DP)
        settingsHit.set(
            w / 2 - dp(SETTINGS_WIDTH_DP) / 2,
            link - touch,
            w / 2 + dp(SETTINGS_WIDTH_DP) / 2,
            link + touch,
        )
        box.set(0f, transportY + playRadius, w, link - touch)
    }

    /** Keeps Android's back gesture off each dial on a side edge; the bottom edge (home) cannot be claimed. */
    private fun excludeBackGesture() {
        val half = dp(RULER_HALF_DP)
        val step = dp(SAMPLE_DP)
        dials.forEachIndexed { i, dial ->
            val rect = exclusions[i]
            perimeter.point(centres[i] - half, pt)
            rect.set(pt[0].toInt(), pt[1].toInt(), pt[0].toInt(), pt[1].toInt())
            var s = centres[i] - half + step
            while (s <= centres[i] + half) {
                perimeter.point(s, pt)
                rect.union(pt[0].toInt(), pt[1].toInt())
                s += step
            }
            val depth = dp(if (dial.placement.atCorner) CORNER_HIT_DP else EDGE_HIT_DP).toInt()
            rect.inset(-depth, -depth)
        }
        systemGestureExclusionRects = exclusions
    }

    private fun rebuildText() {
        val playing = state.nowPlaying
        val title = playing.ifEmpty { nothingPlaying }
        titlePaint.color = if (playing.isEmpty()) palette.dim else palette.ink
        titleLayout =
            StaticLayout.Builder
                .obtain(title, 0, title.length, titlePaint, titleWidth)
                .setAlignment(Layout.Alignment.ALIGN_NORMAL)
                .setMaxLines(1)
                .setEllipsize(TextUtils.TruncateAt.END)
                .build()
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
        subLine = TextUtils.ellipsize(sub, mono, subWidth, TextUtils.TruncateAt.END).toString()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawColor(palette.background)
        drawStatus(canvas)
        drawNowPlaying(canvas)
        drawTrackpad(canvas)
        drawTransport(canvas)
        drawSettingsLink(canvas)
        for (i in dials.indices) {
            drawBoundary(canvas, i)
            drawDial(canvas, i)
        }
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
        fill.color = palette.ink
        canvas.drawCircle(start + dot / 2, statusY - mono.textSize * CAP_CENTRE, dot / 2, fill)
        canvas.drawText(statusLine, start + dot + gap, statusY, mono)
        mono.textAlign = Paint.Align.CENTER
    }

    private fun drawNowPlaying(canvas: Canvas) {
        stroke.color = palette.line
        canvas.drawRect(monogram, stroke)
        mono.textAlign = Paint.Align.CENTER
        mono.letterSpacing = 0f
        mono.textSize = sp(MONOGRAM_SP)
        mono.color = palette.ink
        canvas.drawText(
            state.app.take(1).uppercase(),
            monogram.centerX(),
            monogram.centerY() + mono.textSize * CAP_CENTRE,
            mono,
        )
        titleLayout?.let { layout ->
            canvas.save()
            canvas.translate(titleX, titleY)
            layout.draw(canvas)
            canvas.restore()
        }
        subText()
        mono.textAlign = subAlign
        canvas.drawText(subLine, subX, subY, mono)
        mono.textAlign = Paint.Align.CENTER
        stroke.color = palette.line
        canvas.drawLine(progressX, progressY, progressX + progressWidth, progressY, stroke)
        val fraction =
            if (state.duration > 0) {
                state.position.coerceAtLeast(0).toFloat() / state.duration
            } else {
                (state.level(ControlId.MEDIA_POSITION) ?: 0) / Dial.MAX_LEVEL
            }
        stroke.color = palette.ink
        canvas.drawLine(progressX, progressY, progressX + progressWidth * fraction, progressY, stroke)
    }

    private fun subText() {
        mono.textSize = sp(SUB_SP)
        mono.letterSpacing = SUB_TRACKING
        mono.color = palette.dim
    }

    private fun drawTrackpad(canvas: Canvas) {
        if (showHints) {
            mono.textAlign = Paint.Align.CENTER
            mono.textSize = sp(HINT_SP)
            mono.letterSpacing = HINT_TRACKING
            mono.color = palette.dim
            val bottom = box.bottom - dp(HINT_BOTTOM_DP)
            if (landscape) {
                canvas.drawText(hintLine, box.centerX(), bottom, mono)
            } else {
                hintLines.forEachIndexed { i, line ->
                    canvas.drawText(line, box.centerX(), bottom - (hintLines.size - 1 - i) * dp(HINT_GAP_DP), mono)
                }
            }
        }
        if (fingerDown) {
            fill.color = palette.ink
            canvas.drawCircle(fingerX, fingerY, dp(FINGER_DP) / 2, fill)
        }
    }

    /** A faint line just inside each dial's touch zone: start a finger outside it for the trackpad. */
    private fun drawBoundary(
        canvas: Canvas,
        i: Int,
    ) {
        val dial = dials[i]
        val depth = dp(if (dial.placement.atCorner) CORNER_HIT_DP else EDGE_HIT_DP)
        val half = dp(RULER_HALF_DP + HIT_SLACK_DP)
        val step = dp(SAMPLE_DP)
        stroke.color = palette.line
        glyph.reset()
        perimeter.point(centres[i] - half, pt)
        glyph.moveTo(pt[0] + pt[2] * depth, pt[1] + pt[3] * depth)
        var s = centres[i] - half + step
        while (s <= centres[i] + half) {
            perimeter.point(s, pt)
            glyph.lineTo(pt[0] + pt[2] * depth, pt[1] + pt[3] * depth)
            s += step
        }
        canvas.drawPath(glyph, stroke)
    }

    private fun drawTransport(canvas: Canvas) {
        val scale = 1f
        fill.color = palette.ink
        drawSkip(canvas, prevHit.centerX(), scale, forward = false)
        drawSkip(canvas, nextHit.centerX(), scale, forward = true)
        val cx = playHit.centerX()
        val cy = transportY
        canvas.drawCircle(cx, cy, playRadius, fill)
        fill.color = palette.background
        if (state.flag(ControlId.MEDIA_POSITION)) {
            val bar = dp(PAUSE_BAR_W_DP) * scale
            val tall = dp(PAUSE_BAR_H_DP) * scale
            val gap = dp(PAUSE_GAP_DP) * scale
            canvas.drawRect(cx - gap / 2 - bar, cy - tall / 2, cx - gap / 2, cy + tall / 2, fill)
            canvas.drawRect(cx + gap / 2, cy - tall / 2, cx + gap / 2 + bar, cy + tall / 2, fill)
        } else {
            val size = dp(PLAY_TRIANGLE_DP) * scale
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
        scale: Float,
        forward: Boolean,
    ) {
        val cy = transportY
        val w = dp(SKIP_W_DP) * scale
        val h = dp(SKIP_H_DP) * scale
        val bar = dp(SKIP_BAR_DP) * scale
        val gap = dp(SKIP_GAP_DP) * scale
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

    private fun drawSettingsLink(canvas: Canvas) {
        mono.textAlign = Paint.Align.CENTER
        mono.textSize = sp(SETTINGS_SP)
        mono.letterSpacing = Type.TRACKING_WIDE
        mono.color = palette.dim
        canvas.drawText(settingsText, settingsHit.centerX(), settingsHit.centerY() + mono.textSize * CAP_CENTRE, mono)
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
        // Tick n sits at ruler position -n notches. A level's ruler has ends, from 0 to its full length;
        // a stepper's runs on for ever.
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

        // The fixed indicator: whatever is under it is the value.
        perimeter.point(centre, pt)
        tick.alpha = OPAQUE
        tick.strokeWidth = dp(INDICATOR_STROKE_DP)
        val reach = dp(INDICATOR_DP) * grow
        canvas.drawLine(pt[0], pt[1], pt[0] + pt[2] * reach, pt[1] + pt[3] * reach, tick)

        // The label and the number ride inside the ruler, aligned away from the edge they sit on.
        val depth = dp(if (dial.placement.atCorner) CORNER_LABEL_DP else EDGE_LABEL_DP)
        val ax = pt[0] + pt[2] * depth
        val ay = pt[1] + pt[3] * depth
        mono.textAlign =
            when {
                pt[2] > SIDEWAYS -> Paint.Align.LEFT
                pt[2] < -SIDEWAYS -> Paint.Align.RIGHT
                else -> Paint.Align.CENTER
            }
        val labelSize = sp(DIAL_LABEL_SP)
        val valueSize = sp(if (dial.armed) ARMED_VALUE_SP else DIAL_VALUE_SP)
        val block = labelSize + dp(Space.XS) + valueSize
        val top =
            when {
                pt[3] > SIDEWAYS -> ay
                pt[3] < -SIDEWAYS -> ay - block
                else -> ay - block / 2
            }
        mono.letterSpacing = Type.TRACKING_WIDE
        mono.textSize = labelSize
        mono.color = palette.dim
        canvas.drawText(dial.label, ax, top + labelSize * BASELINE, mono)
        mono.letterSpacing = 0f
        mono.textSize = valueSize
        mono.color = palette.ink
        canvas.drawText(valueText(dial), ax, top + labelSize + dp(Space.XS) + valueSize * BASELINE, mono)
        mono.textAlign = Paint.Align.CENTER
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
                // Lint's accessibility check wants the click reported here, in onTouchEvent itself.
                val openSettings = settingsDown && settingsHit.contains(event.x, event.y)
                if (up(event)) performClick()
                if (openSettings) onOpenSettings()
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
            settingsHit.contains(x, y) -> {
                settingsDown = true
            }

            button != null -> {
                mediaDown = true
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

    /** Ends the touch. True when it was a click: the Settings link, or a tap on a dial. */
    private fun up(event: MotionEvent): Boolean {
        when {
            settingsDown -> {
                settingsDown = false
                return settingsHit.contains(event.x, event.y)
            }

            mediaDown -> {
                mediaDown = false
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
        mediaDown = false
        settingsDown = false
        fingerDown = false
        trackpad.handle(TrackpadRecognizer.Action.CANCEL, FloatArray(0), FloatArray(0), event.eventTime)
    }

    private fun onTrackpad(): Boolean = activeDial < 0 && !mediaDown && !settingsDown

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
        var best = -1
        var bestGap = Float.MAX_VALUE
        dials.forEachIndexed { i, dial ->
            val depth = dp(if (dial.placement.atCorner) CORNER_HIT_DP else EDGE_HIT_DP)
            val gap = abs(perimeter.delta(centres[i], hit[0]))
            if (hit[1] <= depth && gap <= dp(RULER_HALF_DP + HIT_SLACK_DP) && gap < bestGap) {
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

    private companion object {
        val CORNER_POSITIONS =
            intArrayOf(
                RoundedCorner.POSITION_TOP_LEFT,
                RoundedCorner.POSITION_TOP_RIGHT,
                RoundedCorner.POSITION_BOTTOM_RIGHT,
                RoundedCorner.POSITION_BOTTOM_LEFT,
            )

        // Rulers. Sized up from the design at the owner's request: longer arms, deeper ticks, bigger numbers.
        const val MIN_BEND_DP = 24f
        const val RULER_HALF_DP = 190f
        const val MAJOR_TICK_DP = 34f
        const val MINOR_TICK_DP = 22f
        const val MAJOR_STROKE_DP = 2f
        const val MINOR_STROKE_DP = 1.2f
        const val MAJOR_EVERY = 5
        const val MAJOR_ALPHA = 230

        // Half the ink: the design's lighter ticks would fall under 3:1 on the light panel.
        const val MINOR_ALPHA = 128
        const val ARMED_MINOR_ALPHA = 190
        const val OPAQUE = 255
        const val ARMED_GROWTH = 1.25f
        const val INDICATOR_DP = 48f
        const val INDICATOR_STROKE_DP = 2.5f
        const val CORNER_LABEL_DP = 96f
        const val EDGE_LABEL_DP = 64f
        const val DIAL_LABEL_SP = 11f
        const val DIAL_VALUE_SP = 26f
        const val ARMED_VALUE_SP = 32f
        const val BASELINE = 0.8f
        const val SIDEWAYS = 0.3f
        const val CORNER_HIT_DP = 96f
        const val EDGE_HIT_DP = 36f
        const val HIT_SLACK_DP = 12f
        const val JUMP_DP = 64f
        const val SAMPLE_DP = 8f

        // Portrait furniture, from the design's 390 by 844 phone.
        const val CORNER_DP = 176f
        const val CORNER_FRACTION = 0.45f
        const val STATUS_AT = 0.74f
        const val BLOCK_WIDTH_DP = 320f
        const val SUB_BELOW_DP = 6f
        const val LAND_SETTINGS_BOTTOM_DP = 28f
        const val MONOGRAM_DP = 36f
        const val MONOGRAM_SP = 14f
        const val TITLE_SP = 14f
        const val SUB_SP = 9f
        const val SUB_TRACKING = 0.16f
        const val STATUS_SP = 9f
        const val STATUS_TRACKING = 0.16f
        const val STATUS_DOT_DP = 5f
        const val STATUS_GAP_DP = 7f
        const val PLAY_DP = 64f
        const val TRANSPORT_GAP_DP = 56f
        const val SETTINGS_BOTTOM_DP = 54f
        const val SETTINGS_WIDTH_DP = 128f
        const val SETTINGS_SP = 9.5f

        // Landscape furniture, from the design's 844 by 390 phone.
        const val LAND_STATUS_DP = 34f

        // Trackpad.
        const val HINT_SP = 9f
        const val HINT_TRACKING = 0.16f
        const val HINT_BOTTOM_DP = 18f
        const val HINT_GAP_DP = 16f
        const val FINGER_DP = 10f

        // Transport marks.
        const val SKIP_W_DP = 9f
        const val SKIP_H_DP = 12f
        const val SKIP_BAR_DP = 2f
        const val SKIP_GAP_DP = 2f
        const val PAUSE_BAR_W_DP = 4f
        const val PAUSE_BAR_H_DP = 18f
        const val PAUSE_GAP_DP = 6f
        const val PLAY_TRIANGLE_DP = 18f
        const val TRIANGLE_BACK = 0.4f
        const val TRIANGLE_FRONT = 0.6f
        const val CAP_CENTRE = 0.35f
    }
}
