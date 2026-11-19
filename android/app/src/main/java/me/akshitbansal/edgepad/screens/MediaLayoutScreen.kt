package me.akshitbansal.edgepad.screens

import android.content.Context
import android.content.res.Configuration
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.view.MotionEvent
import android.view.View
import android.view.accessibility.AccessibilityNodeProvider
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.drawFitted
import me.akshitbansal.edgepad.surface.ControlSurface
import me.akshitbansal.edgepad.surface.CornerSlots
import me.akshitbansal.edgepad.surface.MediaPiece
import me.akshitbansal.edgepad.surface.Perimeter
import me.akshitbansal.edgepad.surface.SurfaceWidget
import me.akshitbansal.edgepad.surface.WidgetPainter

/**
 * Drag the media pieces on a full-size canvas with a snapping grid; where they land is where the surface
 * draws them. A piece near the middle snaps to it, and the centre line it sits on turns accent to say so.
 */
object MediaLayoutScreen {
    private val scaleRange = StepRange(Settings.MIN_MEDIA_SCALE, Settings.MAX_MEDIA_SCALE, 0.1f)

    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View {
        val canvas = Editor(ui.context, settings)
        return EditorFrame.build(
            ui,
            ui.string(R.string.media_layout_title),
            canvas,
            onBack,
            backLabel = ui.string(R.string.settings_surface_title),
        ) { close ->
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(
                    ui.slider(
                        ui.string(R.string.media_scale),
                        scaleRange.steps,
                        scaleRange.stepOf(settings.mediaScale),
                        { step -> ui.string(R.string.multiplier_value, scaleRange.valueAt(step)) },
                    ) { step ->
                        settings.mediaScale = scaleRange.valueAt(step)
                        canvas.rescale()
                    },
                )
                addView(
                    ui
                        .secondary(
                            ui.string(
                                if (isSideways(ui.context)) {
                                    R.string.media_layout_hint_sideways
                                } else {
                                    R.string.media_layout_hint_upright
                                },
                            ),
                            Type.SMALL,
                            ui.palette.dim,
                        ).apply {
                            setLineSpacing(0f, Type.LEADING)
                            setPadding(0, ui.dp(Space.XS), 0, ui.dp(Space.S))
                        },
                )
                addView(
                    EditorFrame.resetAndDone(
                        ui,
                        ui.string(R.string.media_layout_reset),
                        ui.string(R.string.media_layout_reset_confirm),
                        {
                            canvas.reset()
                            settings.mediaScale = Settings.DEFAULT_MEDIA_SCALE
                            canvas.rescale()
                            close()
                        },
                        close,
                        Ui.Style.OUTLINED,
                    ),
                )
            }
        }
    }

    /** The surface's own proportions: a piece is dragged by its centre and stored as fractions of the size. */
    private class Editor(
        context: Context,
        private val settings: Settings,
    ) : LayoutCanvas(context) {
        // The media pieces, then the widgets that are switched on: all are dragged and nudged the same way.
        private val items: List<LayoutItem> =
            MediaPiece.entries.map { LayoutItem.Piece(it, context.getString(it.nameRes)) } +
                SurfaceWidget.entries
                    .filter(settings::widgetOn)
                    .map { LayoutItem.Widget(it, context.getString(it.nameRes)) }
        private val positions = items.associateWith { stored(it) }.toMutableMap()
        private var dragging: LayoutItem? = null
        private val corner = CORNER_DP * density
        private val drop = Space.HAIR * density
        private val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
        private val shadow =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.FILL
                color = palette.line
            }

        init {
            contentDescription = context.getString(R.string.media_layout_title)
        }

        // AND-A-25: what the surface keeps for itself, drawn as faint ghosts and kept clear of by a dragged piece.
        // Rebuilt when the size is known, so drawing and dragging allocate nothing.
        private val ghosts = ArrayList<RectF>()
        private val probe = RectF()
        private val ghostFill = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = palette.faint }
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

        // ANDU-6: each piece is a node a screen reader can move a cell at a time; a piece has no menu to open.
        private val nodes =
            ControlNodes(
                this,
                items.size,
                label = { i -> items[i].label },
                bounds = { i, out -> bounds(items[i], out) },
                clickable = { false },
                click = {},
                actions = { i -> moveActions(context) { dx, dy -> nudge(items[i], dx, dy) } },
            )

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
            invalidate()
        }

        fun reset() {
            settings.resetPieces()
            settings.resetWidgets()
            for (item in items) positions[item] = stored(item)
            invalidate()
        }

        private fun bounds(
            item: LayoutItem,
            out: RectF,
            at: Pair<Float, Float> = positions.getValue(item),
        ) {
            val (fx, fy) = at
            val cx = fx * width
            val cy = fy * height
            when (item) {
                is LayoutItem.Widget -> {
                    // A widget is not scaled with the media; the surface sizes its box from the same numbers.
                    val (bw, bh) = WidgetPainter.boxSize(item.widget, width.toFloat(), height.toFloat(), density)
                    out.set(cx - bw / 2, cy - bh / 2, cx + bw / 2, cy + bh / 2)
                }

                is LayoutItem.Piece -> {
                    val (w, h) =
                        when (item.piece) {
                            MediaPiece.NOW_PLAYING -> {
                                ControlSurface.NOW_PLAYING_WIDTH_DP to
                                    ControlSurface.NOW_PLAYING_HEIGHT_DP
                            }

                            MediaPiece.TRANSPORT -> {
                                ControlSurface.SKIP_GAP_DP * 2 + Space.TOUCH to ControlSurface.PLAY_DP
                            }
                        }
                    val px = density * scale
                    out.set(cx - w * px / 2, cy - h * px / 2, cx + w * px / 2, cy + h * px / 2)
                }
            }
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            for (ghost in ghosts) canvas.drawRoundRect(ghost, corner, corner, ghostFill)
            for (item in items) {
                bounds(item, box)
                // A piece at rest is a card, with a soft line under it on the light ground; the one being
                // dragged turns accent and lifts off the grid on a shadow.
                val lifted = item == dragging
                if (lifted) {
                    canvas.drawRoundRect(box, corner, corner, lift)
                } else if (!palette.dark) {
                    box.offset(0f, drop)
                    canvas.drawRoundRect(box, corner, corner, shadow)
                    box.offset(0f, -drop)
                }
                fill.color = if (lifted) palette.accent else palette.card
                canvas.drawRoundRect(box, corner, corner, fill)
                text.color = if (lifted) palette.onAccent else palette.dim
                val room = box.width() - 2 * Space.S * density
                val y = box.centerY() + text.textSize * Type.CAP_CENTRE
                text.drawFitted(canvas, item.label, box.centerX(), y, room)
            }
        }

        // Lint's ClickableViewAccessibility wants this on the same class that overrides onTouchEvent;
        // inheriting it from LayoutCanvas does not satisfy the rule, so both editors carry their own.
        override fun performClick(): Boolean {
            super.performClick()
            return true
        }

        override fun onTouchEvent(event: MotionEvent): Boolean {
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    dragging =
                        items.lastOrNull {
                            bounds(it, box)
                            box.contains(event.x, event.y)
                        } ?: return false
                }

                MotionEvent.ACTION_MOVE -> {
                    val item = dragging ?: return false
                    val x = snap(event.x, width)
                    val y = snap(event.y, height)
                    // A move onto a ghost is not taken: the piece waits where it was until the finger is clear.
                    if (!blocked(item, x to y)) {
                        positions[item] = x to y
                        setOnCentre(x, y)
                    }
                }

                MotionEvent.ACTION_UP -> {
                    val item = dragging ?: return false
                    val (x, y) = positions.getValue(item)
                    save(item, x, y)
                    dragging = null
                    clearOnCentre()
                    performClick()
                }

                MotionEvent.ACTION_CANCEL -> {
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
            const val CORNER_DP = 12f
            const val RULER_DEPTH_DP = 44f
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
    ) : LayoutItem
}

/** Positions are kept per orientation, so the hint names the one being edited. */
private fun isSideways(context: Context): Boolean =
    context.resources.configuration.orientation == Configuration.ORIENTATION_LANDSCAPE
