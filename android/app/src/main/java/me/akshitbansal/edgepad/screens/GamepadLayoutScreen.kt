package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.content.Context
import android.graphics.Canvas
import android.graphics.RectF
import android.view.MotionEvent
import android.view.View
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.gamepad.Control
import me.akshitbansal.edgepad.gamepad.ControlGeometry
import me.akshitbansal.edgepad.gamepad.ControlKind
import me.akshitbansal.edgepad.gamepad.GamepadLayout
import me.akshitbansal.edgepad.gamepad.GamepadStore
import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * Drag controls on a full-size canvas with a snapping grid, exactly like [MediaLayoutScreen]'s editor:
 * a piece near the middle snaps to it, and the centre lines light up to say so. The options popup loads
 * a preset or resets the layout, and a long press opens a size picker for one control.
 */
object GamepadLayoutScreen {
    private val SIZES_DP = listOf(44, 56, 64, 80, 110, 150, 200)

    fun build(
        ui: Ui,
        store: GamepadStore,
        onBack: () -> Unit,
    ): View {
        val canvas = Editor(ui.context, store)
        return EditorFrame.build(ui, ui.string(R.string.gamepad_layout_title).uppercase(), canvas, onBack) { close ->
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(
                    presets(ui, store.current.name) { name ->
                        store.choosePreset(name)
                        canvas.reload()
                        close()
                    },
                )
                addView(
                    ui
                        .mono(
                            ui.string(R.string.gamepad_layout_hint),
                            Type.MICRO,
                            ui.palette.dim,
                            Type.TRACKING_ROW,
                        ).apply {
                            setPadding(0, ui.dp(Space.S), 0, ui.dp(Space.S))
                        },
                )
                addView(
                    EditorFrame.resetAndDone(ui, {
                        canvas.reset()
                        close()
                    }, close),
                )
            }
        }
    }

    /** The presets as a list of choices, the one named [current] marked. */
    fun presets(
        ui: Ui,
        current: String,
        onPick: (String) -> Unit,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            addView(ui.mono(ui.string(R.string.gamepad_preset), Type.MICRO, ui.palette.dim, Type.TRACKING_ROW))
            val names = GamepadLayout.presets.map { it.name }
            addView(ui.choices(names, names.indexOf(current)) { i -> onPick(names[i]) })
        }

    /** The surface's own proportions: a control is dragged by its centre and stored as fractions of the size. */
    private class Editor(
        context: Context,
        private val store: GamepadStore,
    ) : LayoutCanvas(context) {
        private var layout = store.current
        private var dragging: Control? = null
        private var downX = 0f
        private var downY = 0f

        init {
            contentDescription = context.getString(R.string.gamepad_layout_title)
        }

        fun reload() {
            layout = store.current
            invalidate()
        }

        fun reset() {
            store.reset()
            layout = store.current
            invalidate()
        }

        private fun bounds(
            control: Control,
            out: RectF,
        ) {
            val cx = control.x * width
            val cy = control.y * height
            val w = control.size * density
            val h = ControlGeometry.halfHeight(control.kind, w / 2) * 2
            out.set(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2)
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            for (control in layout.controls) {
                bounds(control, box)
                // The control being dragged lifts off the grid on a shadow.
                if (control == dragging) {
                    if (control.kind == ControlKind.BUTTON || control.kind == ControlKind.STICK) {
                        canvas.drawOval(box, lift)
                    } else {
                        canvas.drawRoundRect(box, CORNER_DP * density, CORNER_DP * density, lift)
                    }
                }
                stroke.color = if (control == dragging) palette.ink else palette.dim
                drawShape(canvas, control.kind, box)
                text.color = stroke.color
                canvas.drawText(
                    control.label,
                    box.centerX(),
                    box.centerY() + text.textSize * Type.CAP_CENTRE,
                    text,
                )
            }
        }

        private fun drawShape(
            canvas: Canvas,
            kind: ControlKind,
            box: RectF,
        ) {
            when (kind) {
                ControlKind.BUTTON, ControlKind.STICK -> {
                    canvas.drawOval(box, stroke)
                }

                ControlKind.SHOULDER -> {
                    val corner = ControlGeometry.CORNER_DP * density
                    canvas.drawRoundRect(box, corner, corner, stroke)
                }

                ControlKind.DPAD -> {
                    val third = box.width() / ControlGeometry.DPAD_CELLS
                    for (row in 0 until ControlGeometry.DPAD_CELLS.toInt()) {
                        for (col in 0 until ControlGeometry.DPAD_CELLS.toInt()) {
                            if (!ControlGeometry.isArmCell(row, col)) continue
                            canvas.drawRect(
                                box.left + col * third,
                                box.top + row * third,
                                box.left + (col + 1) * third,
                                box.top + (row + 1) * third,
                                stroke,
                            )
                        }
                    }
                }
            }
        }

        override fun onTouchEvent(event: MotionEvent): Boolean {
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    downX = event.x
                    downY = event.y
                    dragging =
                        layout.controls.lastOrNull {
                            bounds(it, box)
                            box.contains(event.x, event.y)
                        } ?: return false
                }

                MotionEvent.ACTION_MOVE -> {
                    val control = dragging ?: return false
                    val x = snap(event.x, width)
                    val y = snap(event.y, height)
                    replace(control, control.copy(x = x, y = y))
                    setOnCentre(x, y)
                }

                MotionEvent.ACTION_UP -> {
                    val control = dragging
                    dragging = null
                    clearOnCentre()
                    if (control != null) {
                        val moved =
                            abs(event.x - downX) > LONG_PRESS_SLOP_DP * density ||
                                abs(event.y - downY) > LONG_PRESS_SLOP_DP * density
                        if (!moved && event.eventTime - event.downTime >= LONG_PRESS_MS) {
                            showSizePicker(currentControl(control.id) ?: control)
                        } else {
                            store.save(layout)
                        }
                    }
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

        private fun currentControl(id: String): Control? = layout.controls.firstOrNull { it.id == id }

        private fun replace(
            old: Control,
            new: Control,
        ) {
            layout = layout.copy(controls = layout.controls.map { if (it.id == old.id) new else it })
            dragging = new
        }

        private fun showSizePicker(control: Control) {
            val current = SIZES_DP.indexOf(control.size.roundToInt()).coerceAtLeast(0)
            val labels = SIZES_DP.map { context.getString(R.string.dp_value, it) }.toTypedArray()
            AlertDialog
                .Builder(context)
                .setTitle(R.string.gamepad_size)
                .setSingleChoiceItems(labels, current) { dialog, which ->
                    val updated = control.copy(size = SIZES_DP[which].toFloat())
                    replace(control, updated)
                    store.save(layout)
                    invalidate()
                    dialog.dismiss()
                }.show()
        }

        private companion object {
            const val LONG_PRESS_MS = 500L
            const val LONG_PRESS_SLOP_DP = 12f
        }
    }
}
