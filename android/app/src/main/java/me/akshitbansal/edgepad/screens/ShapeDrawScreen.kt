package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.drawFitted
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.surface.GestureAction
import me.akshitbansal.edgepad.surface.PadMode
import me.akshitbansal.edgepad.surface.ShapeGroup
import me.akshitbansal.edgepad.surface.Shape
import me.akshitbansal.edgepad.surface.ShapeTarget
import me.akshitbansal.edgepad.surface.Shapes
import me.akshitbansal.edgepad.surface.TrackpadRecognizer

/**
 * Drawing one new shape, in two steps on one screen: a full-bleed canvas to draw the stroke on, then the
 * list of things it could run. The canvas is the layout editors' own — [EditorFrame] round the outside,
 * [LayoutCanvas]'s dot grid underneath — because a shape is drawn where it will be drawn again, at the
 * size of the whole screen, and the editors already look like that.
 *
 * A stroke is judged the moment the finger lifts rather than when Save is pressed. Too small, or too close
 * to a shape already saved, and the message lands under the finger that drew it, where the next attempt is
 * the next thing that happens.
 */
object ShapeDrawScreen {
    private const val TIP_BOTTOM_DP = 72f

    fun build(
        ui: Ui,
        settings: Settings,
        macros: List<String>,
        onSaved: () -> Unit,
        onBack: () -> Unit,
        stroke: FloatArray? = null,
    ): View {
        val existing = Shapes.decode(settings.shapes).orEmpty()
        val targets = vocabulary(macros)
        val root = FrameLayout(ui.context).apply { setBackgroundColor(ui.palette.background) }
        var drawn: FloatArray? = stroke
        var chosen: ShapeTarget? = null
        var group: ShapeGroup? = null

        // One function for both steps, picked between by whether a stroke has been accepted yet. Only the
        // two steps call it: going back to redraw needs a fresh canvas, and accepting a stroke needs the
        // target list. Picking a target deliberately does not -- the check and the Save button move
        // themselves, because rebuilding here would hand the list a new ScrollView starting at the top.
        fun render() {
            root.removeAllViews()
            val points = drawn
            if (points == null) {
                val sketch =
                    Sketch(ui.context, existing) { stroke ->
                        drawn = stroke
                        render()
                    }
                root.addView(
                    EditorFrame.build(
                        ui,
                        ui.string(R.string.shapes_draw_title),
                        sketch,
                        onBack,
                        backLabel = ui.string(R.string.shapes_title),
                    ) { close ->
                        ui.button(ui.string(R.string.shapes_redraw), Ui.Style.QUIET) {
                            sketch.clear()
                            close()
                        }
                    },
                )
                // Above the line that reports a stroke that came out wrong, which sits at the bottom.
                TipCard.show(root, settings, "shapes", StencilIcon.Kind.ANNOTATE, R.string.tip_shapes, TIP_BOTTOM_DP)
                return
            }
            val redraw = {
                drawn = null
                group = null
                render()
            }
            val (row, arm) =
                actions(ui, chosen != null, redraw) {
                    chosen?.let { target ->
                        settings.shapes = Shapes.encode(existing + Shape(points, target))
                        onSaved()
                    }
                }
            val open = group
            // Back from the list returns to the canvas, so the link is named for it rather than for Shapes. Back
            // from one group returns to the groups.
            val bar =
                if (open == null) {
                    ui.bar(
                        ui.string(R.string.shapes_target_title),
                        redraw,
                        backLabel = ui.string(R.string.shapes_draw_back),
                    )
                } else {
                    ui.bar(
                        ui.string(open.nameRes),
                        {
                            group = null
                            render()
                        },
                        backLabel = ui.string(R.string.shapes_target_title),
                    )
                }
            // Redraw and Save are pinned below the scrolling list, so they are reachable without scrolling.
            root.addView(
                ui.page(bar, footer = { add(row, Space.S) }) {
                    card(Space.M) {
                        if (open == null) {
                            groups(ui, targets, chosen, macros) {
                                group = it
                                render()
                            }
                        } else {
                            val inGroup = targets.filter { ShapeGroup.of(it) == open }
                            add(
                                ui.choices(
                                    inGroup.map { ShapesScreen.targetName(ui, it, macros) },
                                    inGroup.indexOfFirst { it == chosen },
                                ) { i ->
                                    chosen = inGroup[i]
                                    arm(true)
                                },
                            )
                        }
                    }
                },
            )
        }

        render()
        return root
    }

    /**
     * The sub-menus of what a shape can run, one link row each with a hairline between. A group's row names the
     * target chosen from it, so coming back to the groups shows where the choice is. A group with nothing in it,
     * such as Macros before the laptop has sent a list and the slot count is unknown, is not offered.
     */
    private fun Column.groups(
        ui: Ui,
        targets: List<ShapeTarget>,
        chosen: ShapeTarget?,
        macros: List<String>,
        onOpen: (ShapeGroup) -> Unit,
    ) {
        val shown = ShapeGroup.entries.filter { g -> targets.any { ShapeGroup.of(it) == g } }
        shown.forEachIndexed { i, g ->
            if (i > 0) hairline()
            val pick = chosen?.takeIf { ShapeGroup.of(it) == g }
            add(ui.link(ui.string(g.nameRes), pick?.let { ShapesScreen.targetName(ui, it, macros) }) { onOpen(g) })
        }
    }

    /**
     * Everything a shape may be bound to, in the order the list offers them.
     *
     * The four continuous gesture actions and NOTHING are left out: a drawn shape fires once, and an
     * action that means "a step per unit of travel" has no travel left once the stroke is finished.
     * Macro slots are named when the laptop has sent its list and numbered when it has not, so a shape can
     * still be bound to slot 3 on a phone that has never been connected.
     */
    private fun vocabulary(macros: List<String>): List<ShapeTarget> {
        val actions: List<ShapeTarget> =
            GestureAction.entries
                .filter { !it.continuous && it != GestureAction.NOTHING }
                .map(ShapeTarget::Run)
        val slots =
            if (macros.isEmpty()) {
                (0 until LaptopState.MACRO_SLOTS).toList()
            } else {
                macros.indices.filter { macros[it].isNotEmpty() }
            }
        val pad = listOf(ShapeTarget.Pad(PadMode.FOCUS), ShapeTarget.Pad(PadMode.PAD_LOCKED))
        return actions + slots.map(ShapeTarget::Macro) + pad
    }

    /**
     * Redraw and Save at the foot of the target list, and the one function that arms Save.
     *
     * Handed back rather than kept, because picking a target must not rebuild this page: the list of things
     * a shape can run is longer than the screen, and a rebuilt page brings a fresh ScrollView that starts at
     * the top, so every pick threw the user back to the first row. The check moves itself now, and this moves
     * the only other thing a pick changes.
     */
    private fun actions(
        ui: Ui,
        ready: Boolean,
        onRedraw: () -> Unit,
        onSave: () -> Unit,
    ): Pair<View, (Boolean) -> Unit> {
        val save =
            ui.button(ui.string(R.string.shapes_save), Ui.Style.FILLED, onSave)
        // A shape saved with nothing behind it would sit in the list looking like a binding and do nothing
        // when drawn, which reads as the recogniser having failed.
        val arm = { on: Boolean -> ui.enable(save, on) }
        arm(ready)
        val row =
            LinearLayout(ui.context).apply {
                gravity = Gravity.END
                addView(
                    ui.button(ui.string(R.string.shapes_redraw), Ui.Style.OUTLINED, onRedraw),
                    LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT),
                )
                addView(
                    save,
                    LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                        .apply { marginStart = ui.dp(Space.S) },
                )
            }
        return row to arm
    }

    /**
     * The canvas one stroke is drawn on: the editors' dot grid, the stroke over it in the accent, and a line
     * at the foot saying what to do — or, once a stroke has been refused, why. The line is painted, so a
     * screen reader would never hear it: a refusal is also set as the canvas's state, in a live region, which
     * is read out the moment it changes.
     *
     * Everything the stroke needs is allocated once. The samples go into two fixed arrays and the picture
     * into one Path the finger extends, so onDraw only ever draws.
     */
    private class Sketch(
        context: Context,
        private val existing: List<Shape>,
        private val onStroke: (FloatArray) -> Unit,
    ) : LayoutCanvas(context) {
        /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
        constructor(context: Context) : this(context, emptyList(), {})

        private val tooShort = context.getString(R.string.shapes_too_short)
        private val tooClose = context.getString(R.string.shapes_too_close)
        private val xs = FloatArray(TrackpadRecognizer.MAX_PATH)
        private val ys = FloatArray(TrackpadRecognizer.MAX_PATH)
        private val path = Path()

        /** The stroke's own paint, rather than the grid's: it is drawn thicker, rounded and in the accent. */
        private val ink =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeWidth = STROKE_DP * density
                strokeCap = Paint.Cap.ROUND
                strokeJoin = Paint.Join.ROUND
                color = palette.accent
            }
        private var count = 0
        private var message = ""

        init {
            contentDescription = context.getString(R.string.shapes_draw_title)
            accessibilityLiveRegion = ACCESSIBILITY_LIVE_REGION_POLITE
        }

        /** Wipes a stroke that came out wrong, from the options popup or from the next touch. */
        fun clear() {
            count = 0
            path.reset()
            message = ""
            invalidate()
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            canvas.drawPath(path, ink)
            text.color = palette.dim
            val y = height - MESSAGE_BOTTOM_DP * density
            text.drawFitted(canvas, message, width / 2f, y, width - 2 * Space.L * density)
        }

        // Lint's ClickableViewAccessibility wants this on the same class that overrides onTouchEvent;
        // inheriting it from LayoutCanvas does not satisfy the rule, so this canvas carries its own.
        override fun performClick(): Boolean {
            super.performClick()
            return true
        }

        override fun onTouchEvent(event: MotionEvent): Boolean {
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    clear()
                    add(event.x, event.y)
                }

                MotionEvent.ACTION_MOVE -> {
                    // Every historical sample too: a fast stroke reports most of its corners in them, and
                    // a shape built from the leftovers is not the shape the finger drew.
                    for (h in 0 until event.historySize) add(event.getHistoricalX(h), event.getHistoricalY(h))
                    add(event.x, event.y)
                }

                MotionEvent.ACTION_UP -> {
                    add(event.x, event.y)
                    performClick()
                    finish()
                }

                MotionEvent.ACTION_CANCEL -> {
                    clear()
                }

                else -> {
                    return false
                }
            }
            invalidate()
            return true
        }

        /** Judges the finished stroke: too small, too close to one already saved, or good to bind. */
        private fun finish() {
            val points = Shapes.normalise(xs, ys, count, density)
            if (points == null) {
                refuse(tooShort)
                return
            }
            if (Shapes.tooClose(points, existing)) {
                refuse(tooClose)
                return
            }
            onStroke(points)
        }

        private fun refuse(reason: String) {
            message = reason
            // Cleared first, so the same refusal twice running is still a change, and is read out again.
            stateDescription = null
            stateDescription = reason
        }

        private fun add(
            x: Float,
            y: Float,
        ) {
            if (count >= xs.size) return
            xs[count] = x
            ys[count] = y
            if (count == 0) path.moveTo(x, y) else path.lineTo(x, y)
            count++
        }

        private companion object {
            const val STROKE_DP = 3f

            /** How far above the bottom edge the message sits, clear of the finger drawing above it. */
            const val MESSAGE_BOTTOM_DP = 28f
        }
    }
}
