package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.PadMode
import me.akshitbansal.edgepad.surface.Shape
import me.akshitbansal.edgepad.surface.ShapeTarget
import me.akshitbansal.edgepad.surface.Shapes

/**
 * The shapes the user has drawn: one row each, with a small picture of the stroke, what it runs, and a way
 * to delete it. The picture is the point of the row — the name of a target says nothing about which scrawl
 * reaches it, and a list of six shapes described only in words is a list nobody can use.
 *
 * Deleting rewrites the whole set and asks to be shown again, the way the gamepad screen does after a
 * preset change: the empty state is a different page from the list, so re-filling a container in place
 * would have to know how to turn one into the other.
 */
object ShapesScreen {
    private const val PREVIEW_DP = 44f

    fun build(
        ui: Ui,
        settings: Settings,
        macros: List<String>,
        onDraw: () -> Unit,
        onChanged: () -> Unit,
        onBack: () -> Unit,
    ): View {
        val shapes = Shapes.decode(settings.shapes).orEmpty()
        val bar = ui.bar(ui.string(R.string.shapes_title), onBack)
        if (shapes.isEmpty()) {
            return ui.page(bar, centred = true) {
                grow()
                headline(ui.string(R.string.shapes_empty_title), Type.TITLE).gravity = Gravity.CENTER
                body(ui.string(R.string.shapes_empty_body), Space.L).gravity = Gravity.CENTER
                grow()
                add(ui.button(ui.string(R.string.shapes_add), Ui.Style.FILLED, onDraw), Space.XL)
            }
        }
        return ui.page(bar) {
            shapes.forEachIndexed { index, shape ->
                add(
                    row(ui, shape, macros) {
                        settings.shapes = Shapes.encode(shapes.filterIndexed { i, _ -> i != index })
                        onChanged()
                    },
                )
                hairline()
            }
            add(ui.button(ui.string(R.string.shapes_add), Ui.Style.FILLED, onDraw), Space.XL)
            mono(ui.string(R.string.shapes_footnote), topDp = Space.L)
        }
    }

    /**
     * What a target is called in a list. Shared with [ShapeDrawScreen], which offers exactly this
     * vocabulary, so a shape reads the same in the picker that made it and in the list that keeps it.
     */
    fun targetName(
        ui: Ui,
        target: ShapeTarget,
        macros: List<String>,
    ): String =
        when (target) {
            is ShapeTarget.Run -> {
                ui.string(target.action.nameRes)
            }

            is ShapeTarget.Macro -> {
                // The laptop's own name for the slot when it has sent one, and the slot's number when it
                // has not — which is every slot until a laptop is connected, and any slot it left empty.
                val stored = macros.getOrNull(target.slot).orEmpty()
                ui.string(R.string.shape_target_macro, stored.ifEmpty { (target.slot + 1).toString() })
            }

            is ShapeTarget.Pad -> {
                ui.string(
                    if (target.mode == PadMode.PAD_LOCKED) {
                        R.string.shape_target_lock_pad
                    } else {
                        R.string.shape_target_focus
                    },
                )
            }
        }

    private fun row(
        ui: Ui,
        shape: Shape,
        macros: List<String>,
        onDelete: () -> Unit,
    ): View {
        val preview =
            Preview(ui.context).apply {
                // Decoration: the sentence beside it already says what the shape runs, and a screen reader
                // reading a description of the drawing as well would say the same thing twice.
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                show(shape.points, ui.palette.ink)
            }
        val name = ui.string(R.string.shape_runs, targetName(ui, shape.target, macros))
        val start =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(
                    preview,
                    LinearLayout.LayoutParams(ui.dp(PREVIEW_DP), ui.dp(PREVIEW_DP)).apply {
                        marginEnd = ui.dp(Space.L)
                    },
                )
                addView(ui.text(name, Type.BODY, ui.palette.ink))
            }
        return ui.row(start, ui.chip(ui.string(R.string.shapes_delete), onDelete))
    }

    /**
     * One normalised stroke drawn small. The path is rebuilt when the points or the size change and never
     * while drawing, because this project's lint rejects an allocation inside onDraw outright.
     */
    private class Preview(
        context: Context,
    ) : View(context) {
        private val stroke =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeCap = Paint.Cap.ROUND
                strokeJoin = Paint.Join.ROUND
                strokeWidth = STROKE_DP * resources.displayMetrics.density
            }
        private val path = Path()
        private var points = FloatArray(0)

        fun show(
            points: FloatArray,
            color: Int,
        ) {
            this.points = points
            stroke.color = color
            rebuild()
            invalidate()
        }

        override fun onSizeChanged(
            w: Int,
            h: Int,
            oldw: Int,
            oldh: Int,
        ) {
            super.onSizeChanged(w, h, oldw, oldh)
            rebuild()
        }

        /** The stroke is normalised around the origin, so it is laid out from the view's own centre. */
        private fun rebuild() {
            path.reset()
            if (points.size < 4 || width == 0 || height == 0) return
            val side = minOf(width, height) * (1f - 2 * INSET)
            val cx = width / 2f
            val cy = height / 2f
            path.moveTo(cx + points[0] * side, cy + points[1] * side)
            for (i in 1 until points.size / 2) {
                path.lineTo(cx + points[i * 2] * side, cy + points[i * 2 + 1] * side)
            }
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            canvas.drawPath(path, stroke)
        }

        private companion object {
            const val STROKE_DP = 2f

            /** Room round the stroke, as a fraction of the shorter side, so a round shape is not clipped. */
            const val INSET = 0.12f
        }
    }
}
