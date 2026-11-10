package me.akshitbansal.edgepad.surface

import android.app.Activity
import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.drawable.Drawable
import android.os.Bundle
import android.os.SystemClock
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
import android.widget.Toast
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.Output
import me.akshitbansal.edgepad.link.OutputName
import me.akshitbansal.edgepad.protocol.OutputKind
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import kotlin.math.abs
import kotlin.math.hypot
import kotlin.math.roundToInt

/**
 * The control surface: a ruler at each corner that holds a dial, the media pieces wherever the user put
 * them, gear, lock and Controls buttons at the top, and everything else is the trackpad. A
 * touch that starts inside a corner's zone is that dial's; one that starts on a media piece or a button is
 * a button press; any other is the trackpad. The lock button locks the trackpad and the Controls popup hides the
 * dials, which is the only thing that can take a piece of it away — and which a drawn shape can walk too, since a
 * locked pad is one of the things a shape may be bound to.
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
    private val settings: Settings,
    private val state: LaptopState,
    private val send: (Frame) -> Unit,
    private val onOpenSettings: () -> Unit,
    private val onOpenControls: () -> Unit,
    /** The connected laptops, for a Devices dial; a surface with none has no such dial to serve. */
    private val devices: DeviceSwitch = DeviceSwitch.None,
    /** What the battery strip shows; read again on every [refreshBatteries]. */
    private val batteries: BatterySource = BatterySource.None,
    /** A tap on the battery strip, or its accessibility action. */
    private val onOpenBatteries: () -> Unit = {},
    /**
     * Only the trackpad: no dials, buttons, media, hints or caption. The Capture screen wants a pointer and
     * nothing else, and the recogniser, the trail and the unbuffered dispatch are the surface's own, so this
     * is a mode of the one view rather than a second recogniser.
     */
    private val trackpadOnly: Boolean = false,
) : View(context) {
    /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
    constructor(context: Context) : this(context, Settings(context), LaptopState(), {}, {}, {})

    private val density = resources.displayMetrics.density
    private val palette = Palette.of(context)
    private val backdrop =
        Backdrop(settings, density, palette.background).also { it.load(this) { (context as? Activity)?.recreate() } }

    // The colour the controls are drawn in, its dimmed forms, and the accent for what is live. On the theme's
    // own background they are the theme's; on a colour, gradient or image, whichever of white or black and of
    // the two themes' accents reads on it. A control colour the user chose is both ink and accent.
    private val controlColor = settings.controlColor
    private val ink =
        controlColor ?: when {
            backdrop.themed -> palette.ink
            backdrop.isDark -> Color.WHITE
            else -> Color.BLACK
        }
    private val accent =
        controlColor ?: when {
            backdrop.themed -> palette.accent
            backdrop.isDark -> DARK_ACCENT
            else -> LIGHT_ACCENT
        }
    private val onAccent =
        when {
            controlColor != null -> if (backdrop.isDark) Color.BLACK else Color.WHITE
            backdrop.themed -> palette.onAccent
            backdrop.isDark -> DARK_ON_ACCENT
            else -> Color.WHITE
        }
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
    private val showHints = settings.hints && !trackpadOnly
    private val hintFade = HintFade(settings.hintsSeen) { settings.hintsSeen = it }
    private var peakFingers = 0
    private var touchStart = 0L
    private val dials: List<Dial> =
        run {
            if (trackpadOnly) return@run emptyList()

            fun build(
                kind: DialKind,
                corner: Int,
                slot: Int,
            ) = kind.dial(
                corner,
                context.getString(kind.shortRes),
                Dial.BASE_UNITS_PER_DP * settings.sensitivityOf(kind),
                settings.snap,
                send,
                ::haptic,
                slot,
                devices,
            )
            if (settings.splitCorners) {
                (0 until Perimeter.SLOTS).mapNotNull { slot ->
                    settings.slot(slot)?.let { build(it, CornerSlots.corner(slot), slot) }
                }
            } else {
                (0 until Perimeter.CORNERS).mapNotNull { corner ->
                    settings.corner(corner)?.let { build(it, corner, Dial.WHOLE) }
                }
            }
        }

    // Derived from what was built, so the progress line goes away exactly when a Scrub dial is drawn.
    private val scrubOnADial = dials.any { it.kind == DialKind.MEDIA }
    private val painter =
        RulerPainter(resources.displayMetrics, settings.dialLength, settings.dialHeight)
    private val pieces = MediaPiece.entries.associateWith { settings.piece(it) }
    private val mediaScale = settings.mediaScale
    private val logo = AppLogo(context, backdrop.isDark)

    // Lucide icons, tinted once: the buttons in the dim ink, skips in ink, play and pause on the accent disc.
    // The lock button gets a second copy of its own icon in the accent, drawn on a round accent tint whenever
    // the pad is out of normal. Focus announces itself — the dials and the media are simply gone —
    // but a locked pad looks exactly like a working one, and a surface that silently swallows every touch
    // reads as a crash rather than as a mode. Both are tinted here because onDraw may not make a Drawable.
    private val lockIconOn = icon(R.drawable.ic_lock, accent)

    // AND-A-16: hiding the dials wears its own icon, so the one lock button no longer looks the same in both modes.
    private val focusIconOn = icon(R.drawable.ic_eye_off, accent)
    private val lockButton = TopButton(icon(R.drawable.ic_lock, dim), side = 0f, open = { tapLock() })
    private val topButtons =
        if (trackpadOnly) {
            emptyList()
        } else {
            listOf(
                TopButton(icon(R.drawable.ic_settings, dim), side = -1f, open = onOpenSettings),
                lockButton,
                TopButton(icon(R.drawable.ic_controls, dim), side = 1f, open = onOpenControls),
            )
        }
    private val speakerIcon = icon(R.drawable.ic_battery_speaker, accent)
    private val headphonesIcon = icon(R.drawable.ic_battery_headphones, accent)
    private val otherOutputIcon = icon(R.drawable.ic_battery_other, accent)

    // The volume dial's output line, cut and chosen in rebuildText so onDraw only reads them.
    private var shownOutput: Output? = null
    private var outputName: String? = null
    private var outputIcon: Drawable? = null
    private val skipBackIcon = icon(R.drawable.ic_skip_back, ink)
    private val skipForwardIcon = icon(R.drawable.ic_skip_forward, ink)
    private val playIcon = icon(R.drawable.ic_play, onAccent)
    private val pauseIcon = icon(R.drawable.ic_pause, onAccent)

    private val muteText = context.getString(R.string.surface_mute)
    private val unknownText = context.getString(R.string.surface_unknown)
    private val unavailableText = context.getString(R.string.surface_unavailable)
    private val openedAt = SystemClock.elapsedRealtime()
    private val nothingPlaying = context.getString(R.string.surface_nothing_playing)
    private var hintLines = hintsToShow()
    private var blockedLayout: StaticLayout? = null
    private var blockedName = ""
    private var blockedWidth = 0
    private val accessibilityActions: Map<AccessibilityNodeInfo.AccessibilityAction, () -> Unit> =
        if (trackpadOnly) {
            emptyMap()
        } else {
            mapOf(
                action(R.id.action_play_pause, R.string.surface_play_pause) to { send(ActionId.PLAY_PAUSE.frame()) },
                action(R.id.action_next_track, R.string.surface_next) to { send(ActionId.NEXT_TRACK.frame()) },
                action(R.id.action_previous_track, R.string.surface_previous) to {
                    send(ActionId.PREVIOUS_TRACK.frame())
                },
                action(R.id.action_open_settings, R.string.surface_open_settings) to onOpenSettings,
                action(R.id.action_controls, R.string.surface_controls) to onOpenControls,
                action(R.id.action_pad_lock, R.string.surface_lock) to { tapLock() },
                // AND-A-8: the two levels a dial sets are reachable without sliding one.
                action(R.id.action_volume_up, R.string.surface_volume_up) to { nudge(ControlId.VOLUME, LEVEL_STEP) },
                action(R.id.action_volume_down, R.string.surface_volume_down) to {
                    nudge(ControlId.VOLUME, -LEVEL_STEP)
                },
                action(R.id.action_brightness_up, R.string.surface_brightness_up) to {
                    nudge(ControlId.BRIGHTNESS, LEVEL_STEP)
                },
                action(R.id.action_brightness_down, R.string.surface_brightness_down) to {
                    nudge(ControlId.BRIGHTNESS, -LEVEL_STEP)
                },
                action(R.id.action_mute, R.string.surface_mute_toggle) to { send(ActionId.MUTE_TOGGLE.frame()) },
            )
        }

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
     * The line that says which mode the surface is in, empty in normal because a normal surface
     * has nothing to explain. Held here rather than built while drawing: both halves of making it — the
     * string lookup and the ellipsis — allocate, and onDraw may not. [rebuildCaption] is what fills it.
     */
    private var captionLine = ""

    /** Half the caption pill's width, measured with [captionLine] because measuring is not free. */
    private var captionHalfWidth = 0f

    /**
     * What the surface is showing and answering, and when the lock button last took a tap.
     *
     * The mode lives in the view and nowhere else: it is not written to [Settings] and it is not offered on
     * the settings screen. The surface is pinned to one orientation, so it is never rebuilt underneath the
     * user while they are standing on it, and a mode that only has to outlive the view needs no storage. It
     * must not outlive it either: a phone that came back up silently locked, with the user having no memory
     * of locking it, would read as broken rather than as obedient.
     */
    private var locked = false

    /** AND-A-16: the dials and media are hidden. Set from the Controls popup, or by a shape bound to it. */
    private var dialsHidden = false

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

    // The play disc casts a soft accent glow downward, the one control on the surface that is lifted.
    private val playPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = accent
            setShadowLayer(media(PLAY_GLOW_DP), 0f, media(PLAY_GLOW_DROP_DP), withAlpha(accent, PLAY_GLOW_ALPHA))
        }
    private val titlePaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.bold
            textSize = sp(TITLE_SP) * mediaScale
        }
    private val subPaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
            textSize = sp(Type.MICRO) * mediaScale
            color = dim
        }
    private val hintPaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
            textAlign = Paint.Align.CENTER
            textSize = sp(Type.MICRO)
            color = dim
        }
    private val blockedPaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
            textSize = sp(Type.MICRO)
            color = accent
        }
    private val captionPaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.bold
            textAlign = Paint.Align.CENTER
            textSize = sp(Type.SMALL)
            color = accent
        }
    private val lockTint = withAlpha(accent, LOCK_TINT_ALPHA)
    private val captionTint = withAlpha(accent, CAPTION_TINT_ALPHA)

    // Low is the theme's red even under a control colour the user chose, so it is never tinted through it.
    private val strip = BatteryStrip(context, ink, accent, faint, palette.danger)

    /** How far a display cutout pushed the battery strip and the button row down, in px; set in onSizeChanged. */
    private var topShift = 0f

    /** The finger went down on the battery strip, until it lifts or the touch is cancelled. */
    private var pressedStrip = false

    init {
        keepScreenOn = true
        contentDescription = context.getString(R.string.surface_description)
        // The mode rides on the state description rather than on announceForAccessibility, which API 36
        // deprecated and which an app targeting 36 or later has ignored ever since. A polite live region
        // is the replacement Android points at: setting stateDescription raises a state-changed event and
        // the screen reader reads it. Nothing else on this view ever changes it, so nothing else speaks.
        accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
        if (!trackpadOnly) strip.refresh(BatteryModel.chips(batteries.groups()))
        stateDescription = modeLabel()
        applyState()
        postDelayed({ invalidate() }, REPORT_WAIT_MS)
    }

    /**
     * A battery changed somewhere: a laptop's list, the laptop in use, or this phone. The activity calls this;
     * the strip remakes its labels and summary here, where allocating is legal.
     */
    fun refreshBatteries() {
        if (trackpadOnly) return
        // Not on the state description: that is a polite live region, and a level ticking down is not news.
        // The summary is the strip's action label instead, read when the user looks for it.
        strip.refresh(BatteryModel.chips(batteries.groups()))
        invalidate()
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
        // The rulers bend round the display's own rounded corners exactly, so they sit in the phone's real
        // corners; a square display gets a small bend anyway. A dial taller than the rounding shortens its marks
        // in the corner rather than widening the bend, which is what keeps them from crossing. See [RulerGeometry].
        val insets = rootWindowInsets
        var corner = 0
        if (insets != null) {
            for (position in CORNER_POSITIONS) corner = maxOf(corner, insets.getRoundedCorner(position)?.radius ?: 0)
        }
        val displayDp = maxOf(corner / density, MIN_BEND_DP)
        perimeter = Perimeter(w.toFloat(), h.toFloat(), dp(displayDp))
        dials.forEachIndexed { i, dial ->
            // A half of a split corner sits off the corner on its own edge, clear of the bend; [DialSpan] below
            // trims it where it would meet its neighbour.
            centres[i] =
                if (dial.slot == Dial.WHOLE) {
                    perimeter.lengthAt(dial.corner.toFloat())
                } else {
                    perimeter.lengthAtSlot(dial.slot, dp(painter.halfLengthDp) / 2f)
                }
            // Upright, only the top corners have the screen to themselves and keep the deep zone: the
            // bottom ones sit near the media pieces, an edge midpoint sits where the thumb swipes, and
            // sideways every slot is near the middle. The rest grab less of the trackpad.
            val roomy = dial.corner < FIRST_BOTTOM_CORNER
            depths[i] = dp(if (w > h || !roomy) CORNER_HIT_NEAR_DP else CORNER_HIT_DP)
        }
        backdrop.resize(w, h)
        // A punch-hole or notch over the middle of the top edge pushes the strip below it, and the button row
        // the same distance with it, so neither sits under the camera.
        topShift = strip.cutoutShift(w, insets?.displayCutout?.boundingRects.orEmpty(), dp(Space.XS))
        layoutPieces(w.toFloat(), h.toFloat())
        strip.layout(w, topShift)
        // No ruler runs under the buttons at the top, and neighbours stop short of each other — which is
        // what keeps two dials on the same edge clear of each other.
        // A dial the user puts in the top-middle slot is the one exception: it is centred inside the
        // buttons' zone, so the cut cannot push it out of its own place, and it shares the space.
        // The zone is read off the row rather than fixed, so the spacing and the button count are tied
        // together through it: four buttons reaching ±1.5 at 64dp claimed 132dp either side of the centre,
        // and five reaching ±2 at the same spacing would claim 164dp — more than a 360dp phone has to give,
        // leaving 16dp of edge at each end and both top dials nowhere to sit. The step is per orientation,
        // so the zone is too: 148dp either side sideways, 132dp upright, where the screen is narrowest and
        // the 16dp the tighter step saves at each end goes back to the top corner dials. See
        // [TOP_BUTTON_OFFSET_PORTRAIT_DP].
        val topCentre = perimeter.lengthAt(TOP_CENTRE)
        val reach = (topButtons.maxOfOrNull { abs(it.side) } ?: 0f) * topButtonOffsetDp(w.toFloat(), h.toFloat())
        // The battery strip above the row is centred on the same edge and must stay inside the same zone: at
        // 144dp it fits the upright row's 72dp either side exactly, and the zone widens only if it ever does not.
        val zone = dp(maxOf(reach + Space.TOUCH / 2, BatteryStrip.WIDTH_DP / 2) + Space.M)
        keepOut[0] = topCentre - zone
        keepOut[1] = topCentre + zone
        // A split corner's text stays out of the same zone, which is centred on the top edge.
        painter.clearOf(w / 2f - zone, w / 2f + zone)
        DialSpan.compute(centres, dp(painter.halfLengthDp), perimeter.length, keepOut, dp(DIAL_GAP_DP), after, before)
        excludeBackGesture()
        rebuildText()
        // The caption is cut to the width it has to fit in, so a new width is a new cut.
        rebuildCaption()
    }

    /**
     * The step between neighbouring top buttons, which the surface's two orientations do not share. It is a
     * function of the size rather than a field because [layoutPieces] and [onSizeChanged] both need it and
     * [onSizeChanged] is the only caller of either. See [TOP_BUTTON_OFFSET_PORTRAIT_DP] for why 48 is a
     * floor rather than a preference.
     */
    private fun topButtonOffsetDp(
        w: Float,
        h: Float,
    ): Float = if (w > h) TOP_BUTTON_OFFSET_LANDSCAPE_DP else TOP_BUTTON_OFFSET_PORTRAIT_DP

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
        val offset = dp(topButtonOffsetDp(w, h))
        val top = dp(TOP_DP) + topShift
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
        val text = maxOf(titlePaint.measureText(titleLine), subPaint.measureText(subLine))
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
        val output = state.output
        if (output != shownOutput) {
            shownOutput = output
            outputName = output?.let { OutputName.short(it.name) }
            outputIcon =
                output?.let {
                    when (it.kind) {
                        OutputKind.SPEAKER -> speakerIcon
                        OutputKind.HEADPHONES -> headphonesIcon
                        else -> otherOutputIcon
                    }
                }
            // The volume dial's output is read with the surface, since the dials are not views of their own.
            contentDescription =
                if (output == null) {
                    context.getString(R.string.surface_description)
                } else {
                    context.getString(R.string.surface_description_output, output.name)
                }
        }
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
        // The app's name as the laptop spells it: sentence case, never shouted.
        val app = state.app
        val sub =
            when (subLine(app, state.duration, scrubOnADial)) {
                SubLine.APP_AND_TIME -> {
                    context.getString(
                        R.string.surface_app_time,
                        app,
                        Clock.format(state.position),
                        Clock.format(state.duration),
                    )
                }

                SubLine.TIME -> {
                    context.getString(R.string.surface_time, Clock.format(state.position), Clock.format(state.duration))
                }

                SubLine.APP -> {
                    app
                }
            }
        subLine = TextUtils.ellipsize(sub, subPaint, room, TextUtils.TruncateAt.END).toString()
        rebuildBlocked()
        if (width > 0) layoutNowPlaying(width.toFloat(), height.toFloat())
    }

    /** TEXT 18: the laptop discards our input, so say so on the surface. Built here, where allocating is legal. */
    private fun rebuildBlocked() {
        val name =
            if (state.inputBlocked && !trackpadOnly) {
                state.laptopName?.takeIf { it.isNotEmpty() } ?: context.getString(R.string.capture_save_laptop)
            } else {
                ""
            }
        // Called on every frame the laptop sends; the layout is remade only when its name or the width changed.
        if (name == blockedName && width == blockedWidth) return
        blockedName = name
        blockedWidth = width
        blockedLayout = null
        if (name.isEmpty() || width == 0) return
        val room = (width - 2 * dp(Space.L)).toInt()
        val text = context.getString(R.string.input_blocked, name)
        blockedLayout =
            StaticLayout
                .Builder
                .obtain(text, 0, text.length, blockedPaint, room)
                .setAlignment(Layout.Alignment.ALIGN_CENTER)
                .build()
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
            val lit = button === lockButton && locked
            if (lit) {
                fill.color = lockTint
                canvas.drawCircle(button.hit.centerX(), button.hit.centerY(), dp(LOCK_TINT_DP) / 2, fill)
            }
            drawIcon(
                canvas,
                if (lit) lockIconOn else button.icon,
                button.hit.centerX(),
                button.hit.centerY(),
                dp(ICON_DP),
            )
        }
        strip.draw(canvas)
        // AND-A-16: the eye-off mark sits under the lock button for as long as the dials are hidden.
        if (dialsHidden && !trackpadOnly) {
            drawIcon(canvas, focusIconOn, lockButton.hit.centerX(), lockButton.hit.bottom + dp(Space.M), dp(EYE_DP))
        }
        if (showHints) drawHints(canvas)
        // Non-modal and drawn from a layout made when the laptop's word arrived, so the frame allocates nothing.
        blockedLayout?.let {
            canvas.save()
            canvas.translate(dp(Space.L), hintTop() + hintLines.size * dp(HINT_GAP_DP) + dp(Space.M))
            it.draw(canvas)
            canvas.restore()
        }
        // Above the hints and before the fingers: a mode the user has just chosen is worth more of the
        // middle of the screen than the hints are, and the trail belongs over it rather than under it.
        if (captionLine.isNotEmpty()) drawCaption(canvas)
        drawFingers(canvas)
        // Focus hides the dials; [dialAt] stops answering for them in the same breath, so nothing is left
        // taking touches where there is nothing drawn.
        if (!dialsHidden) {
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
        val cy = box.centerY() - (if (scrubOnADial) 0f else dp(PROGRESS_BELOW_DP) / 2)
        if (state.app.isEmpty()) {
            stroke.color = faint
            val corner = size * LOGO_ROUNDING
            canvas.drawRoundRect(cx - size / 2, cy - size / 2, cx + size / 2, cy + size / 2, corner, corner, stroke)
        } else {
            logo.draw(canvas, state.app, cx, cy, size, ink)
        }
        val textX = box.left + size + dp(Space.M)
        canvas.drawText(titleLine, textX, cy - dp(Space.XS), titlePaint)
        canvas.drawText(subLine, textX, cy + subPaint.textSize + dp(Space.S), subPaint)
        // The progress line only when no dial scrubs; then it can be slid itself.
        if (scrubOnADial) return
        val progressY = box.bottom
        stroke.color = faint
        stroke.strokeWidth = dp(PROGRESS_STROKE_DP)
        canvas.drawLine(box.left, progressY, box.right, progressY, stroke)
        // What has played is live, so it takes the accent, as a level dial's held notches do.
        stroke.color = accent
        val fraction = if (scrubbing) scrubFraction else playedFraction()
        canvas.drawLine(box.left, progressY, box.left + box.width() * fraction, progressY, stroke)
        stroke.strokeWidth = dp(Space.HAIR)
        fill.color = accent
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
        !trackpadOnly && !dialsHidden && (state.app.isNotEmpty() || state.nowPlaying.isNotEmpty())

    private fun playedFraction(): Float =
        if (state.duration > 0) {
            state.position.coerceAtLeast(0).toFloat() / state.duration
        } else {
            (state.level(ControlId.MEDIA_POSITION) ?: 0) / Dial.MAX_LEVEL
        }

    /** AND-A-17: one line per gesture not yet used; a three or four finger line goes when its swipes are remapped. */
    private fun hintsToShow(): List<String> {
        fun stock(
            a: Gesture,
            b: Gesture,
        ) = settings.gesture(a) == a.default && settings.gesture(b) == b.default
        val slots =
            listOf(
                R.string.surface_hint_one,
                R.string.surface_hint_two,
                R.string.surface_hint_three.takeIf { stock(Gesture.THREE_LEFT, Gesture.THREE_RIGHT) },
                R.string.surface_hint_four.takeIf { stock(Gesture.FOUR_LEFT, Gesture.FOUR_RIGHT) },
                R.string.surface_hint_hold,
            )
        return slots.mapIndexedNotNull { slot, res -> res?.takeIf { hintFade.visible(slot) }?.let(context::getString) }
    }

    private fun drawHints(canvas: Canvas) {
        val y = hintTop()
        hintLines.forEachIndexed { i, line -> canvas.drawText(line, width / 2f, y + i * dp(HINT_GAP_DP), hintPaint) }
    }

    /**
     * The mode caption, a small accent pill centred one gap above where the first hint line sits — above it
     * whether or not the hints are switched on, so the caption never moves between two phones that differ
     * only in that.
     *
     * The band just above the middle is the part of the surface nothing else claims. Higher up is taken: the
     * battery strip and the button row under it end 84dp down (more under a top cutout), and a top corner
     * dial's label and number sit around 60dp in from both the top and the side at the default height, which
     * is exactly where a line under the buttons would cross them in a locked pad, where the dials are still drawn. Lower down is the media's: both pieces default
     * to the bottom fifth of the screen, and a locked pad still draws those too. The finger trail
     * has no fixed place to avoid, so it is drawn after the caption and crosses over it, which is the right
     * way round — the caption is what the surface is, the trail is what the finger is doing to it.
     */
    private fun drawCaption(canvas: Canvas) {
        val baseline = hintTop() - dp(CAPTION_GAP_DP)
        val middle = baseline - captionPaint.textSize * Type.CAP_CENTRE
        val halfHeight = captionPaint.textSize / 2 + dp(CAPTION_PAD_Y_DP)
        val centre = width / 2f
        fill.color = captionTint
        canvas.drawRoundRect(
            centre - captionHalfWidth,
            middle - halfHeight,
            centre + captionHalfWidth,
            middle + halfHeight,
            halfHeight,
            halfHeight,
            fill,
        )
        canvas.drawText(captionLine, centre, baseline, captionPaint)
    }

    /** The touch ended on the trackpad: the gestures it used no longer need their hint. */
    private fun learnHints(event: MotionEvent) {
        val held = peakFingers == 1 && event.eventTime - touchStart >= HOLD_HINT_MS
        val changed = hintFade.used(peakFingers - 1) or (held && hintFade.used(HintFade.HOLD))
        peakFingers = 0
        if (changed) hintLines = hintsToShow()
    }

    /** Where the first hint line sits; the caption is placed off it, so the two move together. */
    private fun hintTop(): Float = height / 2f - (hintLines.size - 1) * dp(HINT_GAP_DP) / 2

    /**
     * Rebuilds [captionLine] for the mode the surface is in now. Called from [enterMode] and from
     * [onSizeChanged], which are between them every moment either half of it can change: what it says comes
     * from the mode, and how much of it fits comes from the width.
     *
     * A caption that ran off both edges would be worse than none, and 45 characters of bold text in a pill
     * is more than a narrow phone at a large font scale has room for, so it is cut here the way the
     * now-playing lines are — with the ellipsis made while it is legal to allocate one. The pill's width is
     * measured here too, for the same reason.
     */
    private fun rebuildCaption() {
        val res =
            when {
                locked -> R.string.surface_locked_caption
                dialsHidden -> R.string.surface_focus_caption
                else -> null
            }
        if (res == null || width == 0) {
            captionLine = ""
            return
        }
        captionLine =
            TextUtils
                .ellipsize(
                    context.getString(res),
                    captionPaint,
                    width - 2 * dp(Space.L + CAPTION_PAD_X_DP),
                    TextUtils.TruncateAt.END,
                ).toString()
        captionHalfWidth = captionPaint.measureText(captionLine) / 2 + dp(CAPTION_PAD_X_DP)
    }

    private fun drawTransport(canvas: Canvas) {
        drawIcon(canvas, skipBackIcon, prevHit.centerX(), prevHit.centerY(), media(SKIP_DP))
        drawIcon(canvas, skipForwardIcon, nextHit.centerX(), nextHit.centerY(), media(SKIP_DP))
        val cx = playHit.centerX()
        val cy = playHit.centerY()
        canvas.drawCircle(cx, cy, media(PLAY_DP) / 2, playPaint)
        // On the accent disc in its own on-accent colour: a pause while playing, else a play.
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
            accent,
            dim,
            dial.label,
            valueText(dial),
            pt,
            if (dial.kind == DialKind.VOLUME) outputName else null,
            outputIcon,
        )
    }

    /** AND-A-26: unreported after [REPORT_WAIT_MS] means never, so a dash replaces the loading ellipsis. */
    private fun blank(): String =
        if (SystemClock.elapsedRealtime() - openedAt > REPORT_WAIT_MS) unavailableText else unknownText

    /** AND-A-3 / AND-A-26: a level dial with no report ignores the slide, so say why when it is touched. */
    private fun hintIfUnreported(dial: Dial) {
        if (dial.control == null || dial.known) return
        val text = if (blank() === unavailableText) R.string.surface_not_reported else R.string.surface_waiting
        Toast.makeText(context, text, Toast.LENGTH_SHORT).show()
    }

    private fun valueText(dial: Dial): String =
        when (dial.kind) {
            DialKind.VOLUME -> {
                when {
                    !dial.known -> blank()
                    dial.flag -> muteText
                    else -> dial.value.toString()
                }
            }

            DialKind.BRIGHTNESS -> {
                if (dial.known) dial.value.toString() else blank()
            }

            DialKind.REFRESH -> {
                // The level is an index; the rate it stands for is only knowable from the laptop's list.
                val rate = if (dial.known) state.refreshRates.getOrNull(dial.value) else null
                if (rate != null) context.getString(R.string.dial_refresh_value, rate) else blank()
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
                        blank()
                    }
                }
            }

            DialKind.ZOOM -> {
                if (dial.steps > 0) "+${dial.steps}" else dial.steps.toString()
            }

            DialKind.DEVICES -> {
                // Cut short: the dial is a corner wide and a laptop's name is whatever its owner typed.
                devices.names.getOrNull(devices.activeIndex)?.let { DeviceSwitch.fit(it) } ?: unknownText
            }

            DialKind.APP_SWITCHER, DialKind.SCROLL, DialKind.SCROLL_H, DialKind.DESKTOPS, DialKind.TABS -> {
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
                val onStrip = pressedStrip
                pressedStrip = false
                when {
                    onStrip -> {
                        if (strip.hit.contains(event.x, event.y)) {
                            performClick()
                            onOpenBatteries()
                        }
                    }

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
        touchStart = event.eventTime
        val x = event.x
        val y = event.y
        val media = mediaShown()
        val button = if (media) transportAt(x, y) else null
        val top = topButtons.firstOrNull { it.hit.contains(x, y) }
        when {
            top != null -> {
                pressedTop = top
            }

            // Empty when there are no chips, so a hidden strip takes no touch.
            strip.hit.contains(x, y) -> {
                pressedStrip = true
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
                        hintIfUnreported(dials[activeDial])
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
                when (target.mode) {
                    PadMode.FOCUS -> dialsHidden = true
                    PadMode.PAD_LOCKED -> locked = true
                    PadMode.NORMAL -> Unit
                }
                enterMode()
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
                if (showHints) learnHints(event)
                trailLength = 0
                drawingShape = false
            }
        }
        return false
    }

    /**
     * Drops everything in flight: the gesture the recogniser is holding, the dial being slid, the button
     * under the finger. The system calls this when it takes the touch away, and [tapLock] calls it when the
     * mode changes underneath one. That second caller is the one that matters — going to a locked pad
     * means [onTrackpad] is about to refuse every further sample, so a tap-and-hold drag with the left
     * button down would never see its UP and the laptop would be left holding the button forever.
     */
    private fun cancelTouches(time: Long) {
        if (activeDial >= 0) dials[activeDial].cancel()
        activeDial = -1
        buttonDown = null
        pressedTop = null
        pressedStrip = false
        scrubbing = false
        fingerCount = 0
        trailLength = 0
        drawingShape = false
        trackpad.handle(TrackpadRecognizer.Action.CANCEL, touchXs, touchYs, time, count = 0)
    }

    private fun onTrackpad(): Boolean =
        !locked && activeDial < 0 && buttonDown == null && pressedTop == null && !pressedStrip && !scrubbing

    /** A tap on the lock button, by finger or accessibility action: it locks the trackpad and nothing else. */
    private fun tapLock() {
        locked = !locked
        enterMode()
        cancelTouches(SystemClock.uptimeMillis())
        haptic()
        invalidate()
    }

    /** AND-A-16: the Controls popup's Hide dials switch. */
    val isDialsHidden: Boolean get() = dialsHidden

    fun setDialsHidden(hidden: Boolean) {
        if (dialsHidden == hidden) return
        dialsHidden = hidden
        enterMode()
        cancelTouches(SystemClock.uptimeMillis())
        invalidate()
    }

    /**
     * The one place the mode is written. Three things follow it — what the surface draws, what a screen
     * reader is told, and the caption saying so — and a writer that moved the mode and forgot one of them
     * is exactly how a locked pad came to look identical to a working one.
     */
    private fun enterMode() {
        stateDescription = modeLabel()
        rebuildCaption()
    }

    private fun modeLabel(): String =
        context.getString(
            when {
                locked && dialsHidden -> R.string.surface_mode_both
                locked -> R.string.surface_mode_locked
                dialsHidden -> R.string.surface_mode_focus
                else -> R.string.surface_mode_normal
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
        if (n > peakFingers) peakFingers = n
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
        if (dialsHidden) return -1
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
        // Labelled with what the strip shows, so the levels are read when the user looks, never announced.
        if (!trackpadOnly) {
            val label = strip.summary.ifEmpty { context.getString(R.string.battery_title) }
            info.addAction(AccessibilityNodeInfo.AccessibilityAction(R.id.action_batteries, label))
        }
    }

    override fun performAccessibilityAction(
        action: Int,
        arguments: Bundle?,
    ): Boolean {
        if (action == R.id.action_batteries && !trackpadOnly) {
            onOpenBatteries()
            return true
        }
        val run = accessibilityActions.entries.firstOrNull { it.key.id == action }?.value
        if (run == null) return super.performAccessibilityAction(action, arguments)
        run()
        return true
    }

    /** Moves [control] by [step] percent from what the laptop last said, and does nothing while it has said nothing. */
    private fun nudge(
        control: ControlId,
        step: Int,
    ) {
        val level = state.level(control) ?: return
        send(control.set((level + step).coerceIn(0, Dial.MAX_LEVEL.toInt())))
    }

    private fun action(
        id: Int,
        labelRes: Int,
    ) = AccessibilityNodeInfo.AccessibilityAction(id, context.getString(labelRes))

    /**
     * One of the buttons floating at the top of the surface. [side] places it across the centre of the top
     * edge in steps of [topButtonOffsetDp]: -1 one step left of it, 0 on it, 1 one step right. The
     * row of three is centred on the lock: settings to its left, Controls to its right.
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

    /** [color] with its alpha replaced by [fraction] of opaque. */
    private fun withAlpha(
        color: Int,
        fraction: Float,
    ): Int = color and RGB_MASK or ((fraction * MAX_ALPHA).roundToInt() shl ALPHA_SHIFT)

    /** A media piece's dp, at the user's media size. */
    private fun media(value: Float): Float = value * density * mediaScale

    private fun sp(value: Float): Float =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, resources.displayMetrics)

    companion object {
        /** How long a dial may stay unreported before it reads as never reported. */
        private const val REPORT_WAIT_MS = 4000L

        /** A one-finger touch this long counts as having tried press and hold. */
        private const val HOLD_HINT_MS = 500L

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
        private const val MAX_ALPHA = 255

        /**
         * The dark and light themes' accents, and the dark one's colour on it. The palette holds only the theme
         * in use, and a colour, gradient or image background picks by its own lightness, not the theme's.
         */
        private const val DARK_ACCENT = 0xFF4DA3FF.toInt()
        private const val LIGHT_ACCENT = 0xFF0068D6.toInt()
        private const val DARK_ON_ACCENT = 0xFF0B1A2E.toInt()

        /** The lit lock's round tint, and the caption pill's fill and padding. */
        private const val LOCK_TINT_DP = 44f
        private const val LOCK_TINT_ALPHA = 0.16f
        private const val CAPTION_TINT_ALPHA = 0.14f
        private const val CAPTION_PAD_X_DP = 14f
        private const val CAPTION_PAD_Y_DP = 7f

        /** The play disc's glow: how soft, how far down it falls, and how strong. */
        private const val PLAY_GLOW_DP = 14f
        private const val PLAY_GLOW_DROP_DP = 6f
        private const val PLAY_GLOW_ALPHA = 0.35f
        private const val DIM_ALPHA = 0x8C000000.toInt()
        private const val FAINT_ALPHA = 0x40000000
        private const val MIN_BEND_DP = 24f
        private const val CORNER_HIT_DP = 96f
        private const val CORNER_HIT_NEAR_DP = 56f

        /** Corners 0 and 1 are the top two, where the hit zone may reach further down the screen. */
        private const val FIRST_BOTTOM_CORNER = 2

        private const val ICON_DP = 24f
        private const val EYE_DP = 16f
        private const val SKIP_DP = 24f
        private const val PLAY_ICON_DP = 24f
        private const val HIT_SLACK_DP = 12f
        private const val JUMP_DP = 64f
        private const val SAMPLE_DP = 8f
        private const val TITLE_SP = 15f

        /** An empty logo slot's corners, as a fraction of its size: an app icon's rounding. */
        private const val LOGO_ROUNDING = 0.22f
        private const val PROGRESS_BELOW_DP = 16f
        private const val PROGRESS_STROKE_DP = 2f
        private const val THUMB_DP = 8f
        private const val THUMB_HELD_DP = 14f
        private const val HINT_GAP_DP = 16f

        /**
         * How far above the first hint line the mode caption sits. Wider than [HINT_GAP_DP] so the caption
         * reads as its own sentence rather than as a fifth hint, and wide enough that the two do not touch
         * when the user's font scale grows both of them.
         */
        private const val CAPTION_GAP_DP = 28f
        private const val FINGER_DP = 10f
        private const val MAX_POINTERS = 10
        private const val TRAIL = 18
        private const val TRAIL_ALPHA = 200f
        private const val ALPHA_SHIFT = 24
        /**
         * The button row's centre. 60dp rather than 40 so the battery strip above it, whose tap rect ends at
         * [BatteryStrip.HIT_BOTTOM_DP], stops where the row's own rects begin.
         */
        const val TOP_DP = 60f

        /** AND-A-8: how far one accessibility volume or brightness action moves the level, in percent. */
        private const val LEVEL_STEP = 10

        /**
         * The step between neighbouring top buttons, and not only spacing: [onSizeChanged] multiplies the
         * outermost button's side by it to work out how much of the top edge the row keeps to itself, so
         * this number and the number of buttons move together. Five buttons at the old 64dp would have
         * reserved 164dp either side of the centre — a 360dp phone's whole top edge and then some — and
         * pushed both top dials off it. Sideways, [TOP_BUTTON_OFFSET_LANDSCAPE_DP] brings that back to
         * 148dp; upright there is less edge to share, so the row closes up to 48dp and 132dp, which is
         * 16dp of top edge handed back to each of the top corner dials.
         *
         * 48dp is the floor, and the reason is the hit rect rather than the look: [Space.TOUCH] is 48, so
         * each button's rect is 48dp wide and a step of 48 leaves neighbours exactly touching. A tighter
         * step would have two rects claim the same pixels, and a tap in the overlap would open whichever
         * of the two [down] tests first rather than the one the finger is on.
         */
        const val TOP_BUTTON_OFFSET_PORTRAIT_DP = 48f
        const val TOP_BUTTON_OFFSET_LANDSCAPE_DP = 56f
        private const val TOP_CENTRE = 0.5f
        private const val DIAL_GAP_DP = 16f
    }
}

/**
 * What a shape bound to the surface can do to it. Two independent switches on the surface itself (the lock button
 * and the Controls popup's Hide dials); this names them for [ShapeTarget.Pad] and for what is stored, by name.
 */
enum class PadMode {
    /** Neither switch; never stored as a target. */
    NORMAL,

    /** Dials and media are neither drawn nor hit-tested; the trackpad and the four top buttons remain. */
    FOCUS,

    /** The trackpad refuses every touch, so a palm or a pocket does nothing. Dials and media stay live. */
    PAD_LOCKED,
}
