package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.content.Context
import android.graphics.Canvas
import android.graphics.DashPathEffect
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.drawable.Drawable
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.RoundedCorner
import android.view.View
import android.view.accessibility.AccessibilityNodeProvider
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.link.blob.MediaIconStore
import me.akshitbansal.edgepad.protocol.BatteryKind
import me.akshitbansal.edgepad.surface.BatteryChips
import me.akshitbansal.edgepad.surface.BatteryRow
import me.akshitbansal.edgepad.surface.BatteryStrip
import me.akshitbansal.edgepad.surface.Clock
import me.akshitbansal.edgepad.surface.ControlSurface
import me.akshitbansal.edgepad.surface.CornerSlots
import me.akshitbansal.edgepad.surface.Dial
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.DialSpan
import me.akshitbansal.edgepad.surface.MediaPiece
import me.akshitbansal.edgepad.surface.Perimeter
import me.akshitbansal.edgepad.surface.PieceGeometry
import me.akshitbansal.edgepad.surface.RulerGeometry
import me.akshitbansal.edgepad.surface.RulerPainter
import me.akshitbansal.edgepad.surface.SurfacePainter
import me.akshitbansal.edgepad.surface.SurfaceWidget
import me.akshitbansal.edgepad.surface.WidgetPainter
import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * Drag the media pieces, or the widgets that are switched on, on a full-size canvas with a snapping grid; where they
 * land is where the surface draws them. A piece near the middle snaps to it, and the centre line it sits on turns
 * accent to say so. Editing the widgets, the media pieces are ghosts nothing is dropped on, and a long press on a
 * widget picks its size.
 */
object MediaLayoutScreen {
    /** What the editor moves: the two media pieces, or the widgets. */
    enum class Scope { MEDIA, WIDGETS }

    private val scaleRange = StepRange(Settings.MIN_MEDIA_SCALE, Settings.MAX_MEDIA_SCALE, 0.1f)

    /** [developer]: whether the AI widget is offered, as the Widgets page offers it. */
    fun build(
        ui: Ui,
        settings: Settings,
        scope: Scope = Scope.MEDIA,
        developer: Boolean = false,
        onBack: () -> Unit,
    ): View {
        val canvas = Editor(ui.context, settings, scope, developer)
        val widgets = scope == Scope.WIDGETS
        val root =
            EditorFrame.build(
                ui,
                ui.string(if (widgets) R.string.widgets_layout_title else R.string.media_layout_title),
                canvas,
                onBack,
                backLabel = ui.string(if (widgets) R.string.widgets_title else R.string.settings_surface_title),
            ) { close ->
                EditorFrame.sizePanel(
                    ui,
                    if (widgets) null else ui.string(R.string.media_scale),
                    if (widgets) {
                        null
                    } else {
                        EditorFrame.Slider(
                            scaleRange.steps,
                            scaleRange.stepOf(settings.mediaScale),
                            { step -> ui.string(R.string.multiplier_value, scaleRange.valueAt(step)) },
                        ) { step ->
                            settings.mediaScale = scaleRange.valueAt(step)
                            canvas.rescale()
                        }
                    },
                    {
                        canvas.reset()
                        if (!widgets) {
                            settings.mediaScale = Settings.DEFAULT_MEDIA_SCALE
                            canvas.rescale()
                        }
                        close()
                    },
                    close,
                )
            }
        TipCard.show(
            root,
            settings,
            if (widgets) "layout_widgets" else "layout_media",
            if (widgets) StencilIcon.Kind.SYSTEM else StencilIcon.Kind.MACROS,
            if (widgets) R.string.widgets_layout_hint else R.string.tip_layout_media,
        )
        return root
    }

    /** The surface's own proportions: a piece is dragged by its centre and stored as fractions of the size. */
    private class Editor(
        context: Context,
        private val settings: Settings,
        private val scope: Scope,
        developer: Boolean,
    ) : LayoutCanvas(context) {
        // The media pieces, or the widgets that are switched on (AI only in developer mode): all are dragged and
        // nudged the same way.
        private val items: List<LayoutItem> =
            if (scope == Scope.MEDIA) {
                MediaPiece.entries.map { LayoutItem.Piece(it, context.getString(it.nameRes)) }
            } else {
                WidgetsScreen
                    .shown(developer)
                    .filter(settings::widgetOn)
                    .map { LayoutItem.Widget(it, context.getString(it.nameRes), settings.widgetScale(it)) }
            }
        private val positions = items.associateWith { stored(it) }.toMutableMap()

        // Editing the widgets, the media pieces stay where they are; read once so drawing reads no setting.
        private val mediaAt = MediaPiece.entries.associateWith { settings.piece(it) }
        private var dragging: LayoutItem? = null
        private var downX = 0f
        private var downY = 0f
        private val holdTick = Runnable { performHapticFeedback(HapticFeedbackConstants.LONG_PRESS) }
        private val corner = CORNER_DP * density
        private val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }

        // What can be moved wears a dashed accent outline; made once, so drawing allocates nothing.
        private val outline =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeWidth = OUTLINE_DP * density
                color = palette.accent
                pathEffect = DashPathEffect(floatArrayOf(DASH_DP * density, DASH_GAP_DP * density), 0f)
            }

        // The real surface behind the pieces, with made-up content: the same painters the surface draws with.
        private val ruler = RulerPainter(resources.displayMetrics, settings.dialLength, settings.dialHeight)
        private var surfacePainter = newPainter(false)

        // The part that cannot be moved is the same painter with every colour at 38 %: no layer, no bitmap.
        private var dimPainter = newPainter(true)
        private val icons = MediaIconStore(MediaIconStore.Companion::decodeBitmap)
        private var perimeter = Perimeter(1f, 1f, 1f)
        private val dials: List<Dial> =
            run {
                fun build(
                    kind: DialKind,
                    at: Int,
                    slot: Int,
                ) = kind
                    .dial(
                        at,
                        context.getString(kind.shortRes),
                        Dial.BASE_UNITS_PER_DP * settings.sensitivityOf(kind),
                        settings.snap,
                        {},
                        {},
                        slot,
                    ).also { if (it.control != null) it.fromLaptop(SAMPLE_LEVEL, false) }
                if (settings.splitCorners) {
                    (0 until Perimeter.SLOTS).mapNotNull { slot ->
                        settings.slot(slot)?.let { build(it, CornerSlots.corner(slot), slot) }
                    }
                } else {
                    (0 until Perimeter.CORNERS).mapNotNull { at ->
                        settings.corner(at)?.let { build(it, at, Dial.WHOLE) }
                    }
                }
            }
        private val scrubOnADial = dials.any { it.kind == DialKind.MEDIA }
        private val dialTexts =
            dials.map {
                when {
                    it.kind == DialKind.MEDIA -> Clock.format(SAMPLE_POSITION)
                    it.control != null -> it.value.toString()
                    else -> ""
                }
            }
        private val centres = FloatArray(dials.size)
        private val after = FloatArray(dials.size)
        private val before = FloatArray(dials.size)
        private val keepOut = FloatArray(2)
        private val pt = FloatArray(4)
        private val strip =
            BatteryStrip(
                context,
                faded(palette.ink),
                faded(palette.accent),
                faded(palette.dim),
                faded(palette.danger),
            ).also {
                it.refresh(
                    BatteryChips(
                        BatteryRow("", BatteryKind.LAPTOP, false, SAMPLE_LAPTOP_PERCENT, true, ""),
                        BatteryRow("", null, true, SAMPLE_PHONE_PERCENT, false, ""),
                        0,
                        false,
                    ),
                )
            }
        private val topIcons: List<Drawable> =
            listOf(R.drawable.ic_controls, R.drawable.ic_battery_keyboard, R.drawable.ic_lock, R.drawable.ic_settings)
                .map { checkNotNull(context.getDrawable(it)).mutate().apply { setTint(faded(palette.dim)) } }
        private val topHits = List(topIcons.size) { RectF() }
        private val rects = FloatArray(PieceGeometry.TRANSPORT_RECTS)
        private val playHit = RectF()
        private val prevHit = RectF()
        private val nextHit = RectF()
        private val sampleTitle = context.getString(R.string.piece_now_playing)
        private val sampleSub =
            context.getString(R.string.surface_time, Clock.format(SAMPLE_POSITION), Clock.format(SAMPLE_DURATION))

        /** [color] with its alpha scaled to [DIM]. */
        private fun faded(color: Int): Int =
            (color and RGB_MASK) or ((color ushr ALPHA_SHIFT) * DIM).roundToInt().shl(ALPHA_SHIFT)

        private fun newPainter(dimmed: Boolean): SurfacePainter {
            fun c(color: Int) = if (dimmed) faded(color) else color
            return SurfacePainter(
                context,
                c(palette.ink),
                c(palette.accent),
                c(palette.dim),
                c(palette.faint),
                c(palette.onAccent),
                palette.dark,
                settings.mediaScale,
                ruler,
            ).also {
                it.titlePaint.color = c(palette.ink)
                it.subPaint.color = c(palette.dim)
            }
        }

        // The widgets are drawn by the painter the surface uses, with made-up values, so what is dragged is what the
        // surface shows, at the size the chosen scale gives it.
        private val painter =
            if (scope == Scope.WIDGETS) {
                WidgetPainter(context, settings, palette.ink, palette.accent, true).also { it.sample() }
            } else {
                null
            }

        init {
            contentDescription =
                context.getString(
                    if (scope == Scope.WIDGETS) R.string.widgets_layout_title else R.string.media_layout_title,
                )
        }

        // AND-A-25: what the surface keeps for itself, drawn as faint ghosts and kept clear of by a dragged piece.
        // Rebuilt when the size is known, so drawing and dragging allocate nothing.
        private val ghosts = ArrayList<RectF>()
        private val probe = RectF()
        private val corners = BooleanArray(Perimeter.CORNERS) { hasDial(it) }

        private fun hasDial(corner: Int): Boolean =
            if (settings.splitCorners) {
                (0 until Perimeter.SLOTS).any { CornerSlots.corner(it) == corner && settings.slot(it) != null }
            } else {
                settings.corner(corner) != null
            }

        override fun onSizeChanged(
            w: Int,
            h: Int,
            oldw: Int,
            oldh: Int,
        ) {
            super.onSizeChanged(w, h, oldw, oldh)
            layoutSurface(w, h)
            ghosts.clear()
            val touch = Space.TOUCH * density
            val sideways = w > h
            val step =
                if (sideways) {
                    ControlSurface.TOP_BUTTON_OFFSET_LANDSCAPE_DP * density
                } else {
                    ControlSurface.TOP_BUTTON_OFFSET_PORTRAIT_DP * density
                }
            val top = ControlSurface.TOP_DP * density
            for (side in -1..1) {
                val cx = w / 2f + side * step
                ghosts.add(RectF(cx - touch / 2, top - touch / 2, cx + touch / 2, top + touch / 2))
            }
            val along = settings.dialLength * density
            val depth = RULER_DEPTH_DP * density
            for (at in 0 until Perimeter.CORNERS) {
                if (!corners[at]) continue
                val right = at == 1 || at == 2
                val bottom = at >= 2
                val x0 = if (right) w - along else 0f
                val x1 = if (right) w.toFloat() else along
                val y0 = if (bottom) h - along else 0f
                val y1 = if (bottom) h.toFloat() else along
                // The ruler bends round the corner: one strip along each of the two edges.
                ghosts.add(RectF(x0, if (bottom) h - depth else 0f, x1, if (bottom) h.toFloat() else depth))
                ghosts.add(RectF(if (right) w - depth else 0f, y0, if (right) w.toFloat() else depth, y1))
            }
            // Editing the widgets, the media pieces stay where they are and nothing is dropped on them.
            if (scope == Scope.WIDGETS) {
                for (piece in MediaPiece.entries) {
                    val ghost = RectF()
                    pieceBounds(piece, settings.piece(piece), ghost)
                    ghosts.add(ghost)
                }
            }
        }

        /** What the surface places when it is sized: the dial spans, the top row and the battery strip. */
        private fun layoutSurface(
            w: Int,
            h: Int,
        ) {
            var round = 0
            val insets = rootWindowInsets
            if (insets != null) {
                for (p in CORNER_POSITIONS) round = maxOf(round, insets.getRoundedCorner(p)?.radius ?: 0)
            }
            perimeter = Perimeter(w.toFloat(), h.toFloat(), RulerGeometry.bend(round.toFloat(), density))
            val along = ruler.halfLengthDp * density
            dials.forEachIndexed { i, dial ->
                centres[i] =
                    if (dial.slot == Dial.WHOLE) {
                        perimeter.lengthAt(dial.corner.toFloat())
                    } else {
                        perimeter.lengthAtSlot(dial.slot, along / 2f)
                    }
            }
            val offsetDp =
                if (w > h) {
                    ControlSurface.TOP_BUTTON_OFFSET_LANDSCAPE_DP
                } else {
                    ControlSurface.TOP_BUTTON_OFFSET_PORTRAIT_DP
                }
            val reachDp = TOP_REACH * offsetDp
            val roomDp = BatteryStrip.roomDp(reachDp)
            strip.layout(w, 0f, roomDp * density)
            val zone = (maxOf(reachDp + Space.TOUCH / 2, roomDp / 2) + Space.M) * density
            val topCentre = perimeter.lengthAt(TOP_CENTRE)
            keepOut[0] = topCentre - zone
            keepOut[1] = topCentre + zone
            ruler.clearOf(w / 2f - zone, w / 2f + zone)
            DialSpan.compute(centres, along, perimeter.length, keepOut, DIAL_GAP_DP * density, after, before)
            val touch = Space.TOUCH * density / 2
            val top = ControlSurface.TOP_DP * density
            topHits.forEachIndexed { i, hit ->
                val cx = w / 2f + (i - 1.5f) * offsetDp * density
                hit.set(cx - touch, top - touch, cx + touch, top + touch)
            }
        }

        /** Whether a piece centred at [at] would sit on something the surface keeps for itself. */
        private fun blocked(
            item: LayoutItem,
            at: Pair<Float, Float>,
        ): Boolean {
            bounds(item, probe, at)
            return ghosts.any { RectF.intersects(it, probe) }
        }

        private fun stored(item: LayoutItem): Pair<Float, Float> =
            when (item) {
                is LayoutItem.Piece -> settings.piece(item.piece)
                is LayoutItem.Widget -> settings.widget(item.widget)
            }

        private fun save(
            item: LayoutItem,
            x: Float,
            y: Float,
        ) {
            when (item) {
                is LayoutItem.Piece -> settings.setPiece(item.piece, x, y)
                is LayoutItem.Widget -> settings.setWidget(item.widget, x, y)
            }
        }

        // ANDU-6: each piece is a node a screen reader can move a cell at a time; a widget also offers its size.
        private val nodes =
            ControlNodes(
                this,
                items.size,
                label = { i -> items[i].label },
                bounds = { i, out -> bounds(items[i], out) },
                clickable = { false },
                click = {},
                actions = { i -> actions(items[i]) },
            )

        private fun actions(item: LayoutItem): List<NodeAction> {
            val moves = moveActions(context) { dx, dy -> nudge(item, dx, dy) }
            if (item !is LayoutItem.Widget) return moves
            val size =
                NodeAction(R.id.action_resize, context.getString(R.string.gamepad_size)) {
                    showSizePicker(item)
                    true
                }
            return moves + size
        }

        override fun getAccessibilityNodeProvider(): AccessibilityNodeProvider = nodes

        /** One grid cell on x ([dx]) or y ([dy]), stored as a drag's end is; false at the edge or onto a ghost. */
        private fun nudge(
            item: LayoutItem,
            dx: Int,
            dy: Int,
        ): Boolean {
            val (fx, fy) = positions.getValue(item)
            val x = if (dx == 0) fx else Nudge.step(fx, width, dx, density)
            val y = if (dy == 0) fy else Nudge.step(fy, height, dy, density)
            val at = x to y
            if ((x == fx && y == fy) || blocked(item, at)) return false
            positions[item] = at
            save(item, x, y)
            invalidate()
            return true
        }

        private var scale = settings.mediaScale

        fun rescale() {
            scale = settings.mediaScale
            surfacePainter = newPainter(false)
            dimPainter = newPainter(true)
            invalidate()
        }

        /** Puts back what this editor moves, and nothing else: the pieces, or the widgets with their sizes. */
        fun reset() {
            if (scope == Scope.MEDIA) settings.resetPieces() else settings.resetWidgets()
            for (item in items) {
                positions[item] = stored(item)
                if (item is LayoutItem.Widget) item.scale = settings.widgetScale(item.widget)
            }
            invalidate()
        }

        /** The size picker the gamepad editor uses, over [Settings.WIDGET_SCALES] as percents. */
        private fun showSizePicker(item: LayoutItem.Widget) {
            val scales = Settings.WIDGET_SCALES
            val current = scales.indexOfFirst { it == item.scale }.coerceAtLeast(0)
            val labels =
                Array(scales.size) { context.getString(R.string.percent_value, (scales[it] * PERCENT).roundToInt()) }
            AlertDialog
                .Builder(context)
                .setTitle(R.string.gamepad_size)
                .setSingleChoiceItems(labels, current) { dialog, which ->
                    dialog.dismiss()
                    settings.setWidgetScale(item.widget, scales[which])
                    item.scale = settings.widgetScale(item.widget)
                    invalidate()
                }.showOver(this)
        }

        private fun bounds(
            item: LayoutItem,
            out: RectF,
            at: Pair<Float, Float> = positions.getValue(item),
        ) {
            when (item) {
                is LayoutItem.Widget -> {
                    // A widget is not scaled with the media; the surface sizes its box from the same numbers.
                    val (fx, fy) = at
                    val cx = fx * width
                    val cy = fy * height
                    val (bw, bh) =
                        WidgetPainter.boxSize(item.widget, width.toFloat(), height.toFloat(), density, item.scale)
                    out.set(cx - bw / 2, cy - bh / 2, cx + bw / 2, cy + bh / 2)
                }

                is LayoutItem.Piece -> {
                    pieceBounds(item.piece, at, out)
                }
            }
        }

        private fun pieceBounds(
            piece: MediaPiece,
            at: Pair<Float, Float>,
            out: RectF,
        ) {
            val (fx, fy) = at
            val w = width.toFloat()
            val h = height.toFloat()
            when (piece) {
                MediaPiece.NOW_PLAYING -> {
                    PieceGeometry.nowPlaying(w, h, fx, fy, density, scale, rects)
                    out.set(rects[0], rects[1], rects[2], rects[3])
                }

                MediaPiece.TRANSPORT -> {
                    PieceGeometry.transport(w, h, fx, fy, density, scale, Space.TOUCH, rects)
                    out.set(rects[4], rects[1], rects[10], rects[3])
                }
            }
        }

        // Under the grid: the surface at 38 % where it cannot be moved, then the movable pieces at full strength.
        override fun drawBelow(canvas: Canvas) {
            for (i in dials.indices) {
                dimPainter.drawDial(
                    canvas,
                    perimeter,
                    dials[i],
                    centres[i],
                    after[i],
                    before[i],
                    dialTexts[i],
                    pt,
                    null,
                    null,
                )
            }
            dimPainter.drawBatteryStrip(canvas, strip, false)
            for (i in topHits.indices) dimPainter.drawTopButton(canvas, topHits[i], topIcons[i], false, false)
            // Editing the widgets, the media pieces are part of the surface that stays put.
            if (scope == Scope.WIDGETS) {
                for (piece in MediaPiece.entries) {
                    val at = mediaAt.getValue(piece)
                    pieceBounds(piece, at, box)
                    drawPiece(dimPainter, canvas, piece, at, box)
                }
            }
            for (item in items) {
                bounds(item, box)
                // The one being dragged lifts off the grid on a shadow and a soft accent plate.
                if (item == dragging) {
                    canvas.drawRoundRect(box, corner, corner, lift)
                    fill.color = palette.accentSoft
                    canvas.drawRoundRect(box, corner, corner, fill)
                }
                when (item) {
                    is LayoutItem.Piece -> {
                        drawPiece(surfacePainter, canvas, item.piece, positions.getValue(item), box)
                    }

                    is LayoutItem.Widget -> {
                        painter?.drawAt(
                            canvas,
                            item.widget,
                            box.centerX(),
                            box.centerY(),
                            width.toFloat(),
                            height.toFloat(),
                            item.scale,
                        )
                    }
                }
                canvas.drawRoundRect(box, corner, corner, outline)
            }
        }

        /** A media piece as the surface draws it, in [box], with a made-up track. */
        private fun drawPiece(
            painter: SurfacePainter,
            canvas: Canvas,
            piece: MediaPiece,
            at: Pair<Float, Float>,
            into: RectF,
        ) {
            when (piece) {
                MediaPiece.NOW_PLAYING -> {
                    painter.drawNowPlaying(
                        canvas,
                        into,
                        SAMPLE_APP,
                        sampleTitle,
                        sampleSub,
                        scrubOnADial,
                        false,
                        SAMPLE_PLAYED,
                        icons,
                    )
                }

                MediaPiece.TRANSPORT -> {
                    PieceGeometry.transport(
                        width.toFloat(),
                        height.toFloat(),
                        at.first,
                        at.second,
                        density,
                        scale,
                        Space.TOUCH,
                        rects,
                    )
                    playHit.set(rects[0], rects[1], rects[2], rects[3])
                    prevHit.set(rects[4], rects[5], rects[6], rects[7])
                    nextHit.set(rects[8], rects[9], rects[10], rects[11])
                    painter.drawTransport(canvas, prevHit, playHit, nextHit, false, null)
                }
            }
        }

        /** Whether a touch at [x], [y] is on [item], whose target is at least 48 dp each way. */
        private fun hit(
            item: LayoutItem,
            x: Float,
            y: Float,
        ): Boolean {
            bounds(item, box)
            val touch = Space.TOUCH * density
            box.inset(-maxOf(0f, (touch - box.width()) / 2), -maxOf(0f, (touch - box.height()) / 2))
            return box.contains(x, y)
        }

        // Lint's ClickableViewAccessibility wants this on the same class that overrides onTouchEvent;
        // inheriting it from LayoutCanvas does not satisfy the rule, so both editors carry their own.
        override fun performClick(): Boolean {
            super.performClick()
            return true
        }

        private fun movedFar(event: MotionEvent): Boolean =
            abs(event.x - downX) > LONG_PRESS_SLOP_DP * density || abs(event.y - downY) > LONG_PRESS_SLOP_DP * density

        override fun onTouchEvent(event: MotionEvent): Boolean {
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    downX = event.x
                    downY = event.y
                    val item = items.lastOrNull { hit(it, event.x, event.y) } ?: return false
                    dragging = item
                    if (item is LayoutItem.Widget) postDelayed(holdTick, LONG_PRESS_MS)
                }

                MotionEvent.ACTION_MOVE -> {
                    val item = dragging ?: return false
                    if (movedFar(event)) removeCallbacks(holdTick)
                    val x = snap(event.x, width)
                    val y = snap(event.y, height)
                    // A move onto a ghost is not taken: the piece waits where it was until the finger is clear.
                    if (!blocked(item, x to y)) {
                        positions[item] = x to y
                        setOnCentre(x, y)
                    }
                }

                MotionEvent.ACTION_UP -> {
                    removeCallbacks(holdTick)
                    val item = dragging ?: return false
                    dragging = null
                    clearOnCentre()
                    val held = !movedFar(event) && event.eventTime - event.downTime >= LONG_PRESS_MS
                    if (held && item is LayoutItem.Widget) {
                        // A hold is not a move: the widget stays where it was stored, and its size is asked for.
                        positions[item] = stored(item)
                        showSizePicker(item)
                    } else {
                        val (x, y) = positions.getValue(item)
                        save(item, x, y)
                    }
                    performClick()
                }

                MotionEvent.ACTION_CANCEL -> {
                    removeCallbacks(holdTick)
                    dragging = null
                    clearOnCentre()
                }

                else -> {
                    return false
                }
            }
            invalidate()
            return true
        }

        private companion object {
            const val CORNER_DP = 14f
            const val OUTLINE_DP = 2f
            const val DASH_DP = 6f
            const val DASH_GAP_DP = 4f
            const val DIM = 0.38f
            const val RGB_MASK = 0x00FFFFFF
            const val ALPHA_SHIFT = 24
            const val DIAL_GAP_DP = 16f
            const val TOP_CENTRE = 0.5f
            const val TOP_REACH = 1.5f
            const val SAMPLE_LEVEL = 50
            const val SAMPLE_POSITION = 84
            const val SAMPLE_DURATION = 210
            const val SAMPLE_PLAYED = 0.4f
            const val SAMPLE_LAPTOP_PERCENT = 97
            const val SAMPLE_PHONE_PERCENT = 52

            /** Any name the logo store has no icon for: the surface then draws its generic note. */
            const val SAMPLE_APP = "player"
            val CORNER_POSITIONS =
                intArrayOf(
                    RoundedCorner.POSITION_TOP_LEFT,
                    RoundedCorner.POSITION_TOP_RIGHT,
                    RoundedCorner.POSITION_BOTTOM_RIGHT,
                    RoundedCorner.POSITION_BOTTOM_LEFT,
                )
            const val RULER_DEPTH_DP = 44f
            const val LONG_PRESS_MS = 500L
            const val LONG_PRESS_SLOP_DP = 12f
            const val PERCENT = 100
        }
    }
}

/** Something the layout editor drags: a media piece, or a widget that is switched on. */
private sealed interface LayoutItem {
    val label: String

    class Piece(
        val piece: MediaPiece,
        override val label: String,
    ) : LayoutItem

    class Widget(
        val widget: SurfaceWidget,
        override val label: String,
        /** Its stored size, kept here so drawing reads no setting. */
        var scale: Float,
    ) : LayoutItem
}
