package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.surface.GestureAction
import me.akshitbansal.edgepad.surface.PadMode
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
 * A stroke is judged the moment the finger lifts rather than when SAVE is pressed. Too small, or too close
 * to a shape already saved, and the message lands under the finger that drew it, where the next attempt is
 * the next thing that happens.
 */
object ShapeDrawScreen {
    private const val DISABLED_ALPHA = 0.4f

    fun build(
        ui: Ui,
        settings: Settings,
        macros: List<String>,
        onSaved: () -> Unit,
        onBack: () -> Unit,
    ): View {
        val existing = Shapes.decode(settings.shapes).orEmpty()
        val targets = vocabulary(macros)
        val root = FrameLayout(ui.context).apply { setBackgroundColor(ui.palette.background) }
        var drawn: FloatArray? = null
        var chosen: ShapeTarget? = null

        // One function for both steps, picked between by whether a stroke has been accepted yet. Only the
        // two steps call it: going back to redraw needs a fresh canvas, and accepting a stroke needs the
        // target list. Picking a target deliberately does not -- the dot and the SAVE button move
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
                val title = ui.string(R.string.shapes_draw_title).uppercase()
                root.addView(
                    EditorFrame.build(ui, title, sketch, onBack) { close ->
                        ui.button(ui.string(R.string.shapes_redraw), Ui.Style.QUIET) {
                            sketch.clear()
                            close()
                        }
                    },
                )
                return
            }
            val redraw = {
                drawn = null
                render()
            }
            val (row, arm) =
                actions(ui, chosen != null, redraw) {
                    chosen?.let { target ->
                        settings.shapes = Shapes.encode(existing + Shape(points, target))
                        onSaved()
                    }
                }
            root.addView(
                ui.page(ui.bar(ui.string(R.string.shapes_title), redraw)) {
                    add(
                        ui.choices(
                            targets.map { ShapesScreen.targetName(ui, it, macros) },
                            targets.indexOfFirst { it == chosen },
                        ) { i ->
                            chosen = targets[i]
                            arm(true)
                        },
                    )
                    add(
                        row,
                        Space.L,
                    )
                },
            )
        }

        render()
        return root
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
     * REDRAW and SAVE at the foot of the target list, and the one function that arms SAVE.
     *
     * Handed back rather than kept, because picking a target must not rebuild this page: the list of things
     * a shape can run is longer than the screen, and a rebuilt page brings a fresh ScrollView that starts at
     * the top, so every pick threw the user back to the first row. The dot moves itself now, and this moves
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
        val arm = { on: Boolean ->
            save.isEnabled = on
            save.alpha = if (on) 1f else DISABLED_ALPHA
        }
        arm(ready)
        val row =
            LinearLayout(ui.context).apply {
                addView(
                    ui.button(ui.string(R.string.shapes_redraw), Ui.Style.QUIET, onRedraw),
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
                )
                addView(
                    save,
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                        marginStart = ui.dp(Space.S)
                    },
                )
            }
        return row to arm
    }

    /**
     * The canvas one stroke is drawn on: the editors' dot grid, the stroke over it, and a line of small
     * capitals at the foot saying what to do — or, once a stroke has been refused, why.
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

        private val hint = context.getString(R.string.shapes_draw_hint)
        private val tooShort = context.getString(R.string.shapes_too_short)
        private val tooClose = context.getString(R.string.shapes_too_close)
        private val xs = FloatArray(TrackpadRecognizer.MAX_PATH)
        private val ys = FloatArray(TrackpadRecognizer.MAX_PATH)
        private val path = Path()
        private var count = 0
        private var message = hint

        init {
            contentDescription = context.getString(R.string.shapes_draw_title)
        }

        /** Wipes a stroke that came out wrong, from the options popup or from the next touch. */
        fun clear() {
            count = 0
            path.reset()
            message = hint
            invalidate()
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            stroke.pathEffect = null
            stroke.color = palette.ink
            stroke.strokeWidth = STROKE_DP * density
            stroke.strokeCap = Paint.Cap.ROUND
            stroke.strokeJoin = Paint.Join.ROUND
            canvas.drawPath(path, stroke)
            stroke.strokeWidth = Space.HAIR * density
            stroke.strokeCap = Paint.Cap.BUTT
            stroke.strokeJoin = Paint.Join.MITER
            text.color = palette.dim
            canvas.drawText(message, width / 2f, height - MESSAGE_BOTTOM_DP * density, text)
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
                message = tooShort
                return
            }
            if (Shapes.tooClose(points, existing)) {
                message = tooClose
                return
            }
            onStroke(points)
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
