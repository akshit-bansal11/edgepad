package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.text.TextPaint
import android.util.TypedValue
import android.view.Gravity
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.gamepad.ControlKind
import me.akshitbansal.edgepad.gamepad.GamepadLayout
import kotlin.math.abs
import kotlin.math.atan2
import kotlin.math.hypot
import kotlin.math.roundToInt

/**
 * Plays a [GamepadLayout]: draws every control exactly as [GamepadLayoutScreen]'s editor does, but live
 * and multi-touch, so a thumb can hold the stick while other fingers tap buttons.
 */
object GamepadScreen {
    fun build(
        ui: Ui,
        layout: GamepadLayout,
        onKey: (code: Int, down: Boolean) -> Unit,
        onBack: () -> Unit,
        onEdit: () -> Unit,
    ): View {
        val surface = Surface(ui.context, layout, onKey)
        val header =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                setPadding(ui.dp(Space.L), ui.dp(Space.XL), ui.dp(Space.L), ui.dp(Space.S))
                val back =
                    Glyph(ui.context, Glyph.Shape.CHEVRON_LEFT, ui.palette.ink).apply {
                        contentDescription = ui.string(R.string.back)
                        ui.tappable(this, onBack)
                    }
                addView(back, LinearLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH)))
                addView(
                    ui.text(layout.name, Type.HEADING, ui.palette.ink, Type.sans, Type.TRACKING_TIGHT),
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
                )
                addView(ui.chip(ui.string(R.string.gamepad_edit), onEdit))
            }
        return LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(ui.palette.background)
            keepScreenOn = true
            contentDescription = ui.string(R.string.gamepad_description)
            addView(header)
            addView(surface, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
        }
    }

    /**
     * One direction bit per dpad/stick axis; up/down/left/right key indices in a control's keys line up
     * with these positions.
     */
    private object Dir {
        const val UP = 1
        const val DOWN = 2
        const val LEFT = 4
        const val RIGHT = 8
    }

    /**
     * Draws and drives every control at once. Geometry (px centres and sizes) is recomputed only in
     * [onSizeChanged] into pre-sized arrays; [onDraw] and touch handling allocate nothing. A pointer is
     * tracked by its id in [pointerToControl], never by its index, since indices shift when another
     * finger lifts.
     */
    private class Surface(
        context: Context,
        private val layout: GamepadLayout,
        private val onKey: (code: Int, down: Boolean) -> Unit,
    ) : View(context) {
        /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
        constructor(context: Context) : this(context, GamepadLayout("", emptyList()), { _, _ -> })

        private val density = resources.displayMetrics.density
        private val palette = Palette.of(context)
        private val controls = layout.controls
        private val slackPx = SLACK_DP * density

        // Geometry, one slot per control, recomputed in onSizeChanged.
        private val centerX = FloatArray(controls.size)
        private val centerY = FloatArray(controls.size)
        private val sizePx = FloatArray(controls.size)
        private val halfW = FloatArray(controls.size)
        private val halfH = FloatArray(controls.size)

        // Live state, one slot per control.
        private val pressed = BooleanArray(controls.size)
        private val dirBits = IntArray(controls.size)
        private val thumbOffX = FloatArray(controls.size)
        private val thumbOffY = FloatArray(controls.size)

        // Which control (index into [controls], or -1) each pointer id currently holds.
        private val pointerToControl = IntArray(MAX_POINTERS) { NONE }

        private val box = RectF()
        private val stroke =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeWidth = Space.HAIR * density
            }
        private val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
        private val text =
            TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
                typeface = Type.mono
                textAlign = Paint.Align.CENTER
                letterSpacing = Type.TRACKING_WIDE
                textSize = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, Type.MICRO, resources.displayMetrics)
            }

        init {
            contentDescription = context.getString(R.string.gamepad_description)
        }

        override fun onSizeChanged(
            w: Int,
            h: Int,
            oldw: Int,
            oldh: Int,
        ) {
            super.onSizeChanged(w, h, oldw, oldh)
            for (i in controls.indices) {
                val control = controls[i]
                centerX[i] = control.x * w
                centerY[i] = control.y * h
                sizePx[i] = control.size * density
                halfW[i] = sizePx[i] / 2f
                halfH[i] = if (control.kind == ControlKind.SHOULDER) halfW[i] * SHOULDER_ASPECT else halfW[i]
            }
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            for (i in controls.indices) {
                box.set(centerX[i] - halfW[i], centerY[i] - halfH[i], centerX[i] + halfW[i], centerY[i] + halfH[i])
                when (controls[i].kind) {
                    ControlKind.BUTTON -> drawRound(canvas, i)
                    ControlKind.SHOULDER -> drawShoulder(canvas, i)
                    ControlKind.DPAD -> drawDpad(canvas, i)
                    ControlKind.STICK -> drawStick(canvas, i)
                }
            }
        }

        private fun drawRound(
            canvas: Canvas,
            i: Int,
        ) {
            if (pressed[i]) {
                fill.color = palette.ink
                canvas.drawOval(box, fill)
                text.color = palette.background
            } else {
                stroke.color = palette.dim
                canvas.drawOval(box, stroke)
                text.color = palette.ink
            }
            drawLabel(canvas, i)
        }

        private fun drawShoulder(
            canvas: Canvas,
            i: Int,
        ) {
            val corner = CORNER_DP * density
            if (pressed[i]) {
                fill.color = palette.ink
                canvas.drawRoundRect(box, corner, corner, fill)
                text.color = palette.background
            } else {
                stroke.color = palette.dim
                canvas.drawRoundRect(box, corner, corner, stroke)
                text.color = palette.ink
            }
            drawLabel(canvas, i)
        }

        private fun drawDpad(
            canvas: Canvas,
            i: Int,
        ) {
            val third = box.width() / DPAD_CELLS
            for (row in 0 until DPAD_CELLS.toInt()) {
                for (col in 0 until DPAD_CELLS.toInt()) {
                    val bit = dpadCellBit(row, col)
                    if (row != 1 && col != 1) continue
                    val cell =
                        RectF(
                            box.left + col * third,
                            box.top + row * third,
                            box.left + (col + 1) * third,
                            box.top + (row + 1) * third,
                        )
                    if (bit != NO_BIT && dirBits[i] and bit != 0) {
                        fill.color = palette.ink
                        canvas.drawRect(cell, fill)
                    } else {
                        stroke.color = palette.dim
                        canvas.drawRect(cell, stroke)
                    }
                }
            }
            text.color = if (dirBits[i] != 0) palette.ink else palette.dim
            drawLabel(canvas, i)
        }

        private fun dpadCellBit(
            row: Int,
            col: Int,
        ): Int =
            when {
                row == 0 && col == 1 -> Dir.UP
                row == 2 && col == 1 -> Dir.DOWN
                row == 1 && col == 0 -> Dir.LEFT
                row == 1 && col == 2 -> Dir.RIGHT
                else -> NO_BIT
            }

        private fun drawStick(
            canvas: Canvas,
            i: Int,
        ) {
            stroke.color = palette.dim
            canvas.drawOval(box, stroke)
            val thumbRadius = sizePx[i] * STICK_THUMB_RATIO / 2f
            fill.color = palette.ink
            canvas.drawCircle(centerX[i] + thumbOffX[i], centerY[i] + thumbOffY[i], thumbRadius, fill)
            text.color = palette.dim
            drawLabel(canvas, i)
        }

        private fun drawLabel(
            canvas: Canvas,
            i: Int,
        ) {
            canvas.drawText(controls[i].label, centerX[i], centerY[i] + text.textSize * Type.CAP_CENTRE, text)
        }

        override fun performClick(): Boolean {
            super.performClick()
            return true
        }

        override fun onTouchEvent(event: MotionEvent): Boolean {
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                    val idx = event.actionIndex
                    val pointerId = event.getPointerId(idx)
                    if (pointerId < MAX_POINTERS) {
                        val controlIndex = hitTest(event.getX(idx), event.getY(idx))
                        if (controlIndex != NONE) {
                            pointerToControl[pointerId] = controlIndex
                            press(controlIndex, event.getX(idx), event.getY(idx))
                        }
                    }
                }

                MotionEvent.ACTION_MOVE -> {
                    for (pointerId in 0 until MAX_POINTERS) {
                        val controlIndex = pointerToControl[pointerId]
                        if (controlIndex == NONE) continue
                        val idx = event.findPointerIndex(pointerId)
                        if (idx == NONE) continue
                        move(controlIndex, event.getX(idx), event.getY(idx))
                    }
                }

                MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> {
                    val idx = event.actionIndex
                    val pointerId = event.getPointerId(idx)
                    if (pointerId < MAX_POINTERS) {
                        val controlIndex = pointerToControl[pointerId]
                        if (controlIndex != NONE) {
                            release(controlIndex)
                            pointerToControl[pointerId] = NONE
                        }
                    }
                    performClick()
                }

                MotionEvent.ACTION_CANCEL -> {
                    for (pointerId in 0 until MAX_POINTERS) {
                        val controlIndex = pointerToControl[pointerId]
                        if (controlIndex == NONE) continue
                        release(controlIndex)
                        pointerToControl[pointerId] = NONE
                    }
                }

                else -> {
                    return false
                }
            }
            invalidate()
            return true
        }

        /** The last (topmost-added) control whose hit area, plus a little slack, contains the point. */
        private fun hitTest(
            x: Float,
            y: Float,
        ): Int {
            for (i in controls.indices.reversed()) {
                val dx = x - centerX[i]
                val dy = y - centerY[i]
                val hit =
                    when (controls[i].kind) {
                        ControlKind.BUTTON, ControlKind.STICK -> {
                            hypot(dx, dy) <= halfW[i] + slackPx
                        }

                        ControlKind.SHOULDER -> {
                            abs(dx) <= halfW[i] + slackPx && abs(dy) <= halfH[i] + slackPx
                        }

                        ControlKind.DPAD -> {
                            val edge = halfW[i] + slackPx
                            val arm = halfW[i] / DPAD_ARM_DIVISOR + slackPx
                            abs(dx) <= edge && abs(dy) <= edge && (abs(dx) <= arm || abs(dy) <= arm)
                        }
                    }
                if (hit) return i
            }
            return NONE
        }

        private fun press(
            i: Int,
            x: Float,
            y: Float,
        ) {
            when (controls[i].kind) {
                ControlKind.BUTTON, ControlKind.SHOULDER -> {
                    pressed[i] = true
                    onKey(controls[i].keys[0], true)
                    performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
                }

                ControlKind.DPAD -> {
                    applyBits(i, direction(x - centerX[i], y - centerY[i], sizePx[i] * DPAD_DEAD_ZONE))
                }

                ControlKind.STICK -> {
                    updateThumb(i, x, y)
                    applyBits(i, direction(x - centerX[i], y - centerY[i], sizePx[i] * STICK_DEAD_ZONE))
                }
            }
        }

        private fun move(
            i: Int,
            x: Float,
            y: Float,
        ) {
            when (controls[i].kind) {
                ControlKind.DPAD -> {
                    applyBits(i, direction(x - centerX[i], y - centerY[i], sizePx[i] * DPAD_DEAD_ZONE))
                }

                ControlKind.STICK -> {
                    updateThumb(i, x, y)
                    applyBits(i, direction(x - centerX[i], y - centerY[i], sizePx[i] * STICK_DEAD_ZONE))
                }

                ControlKind.BUTTON, ControlKind.SHOULDER -> {
                    Unit
                }
            }
        }

        private fun release(i: Int) {
            when (controls[i].kind) {
                ControlKind.BUTTON, ControlKind.SHOULDER -> {
                    pressed[i] = false
                    onKey(controls[i].keys[0], false)
                }

                ControlKind.DPAD, ControlKind.STICK -> {
                    applyBits(i, 0)
                    thumbOffX[i] = 0f
                    thumbOffY[i] = 0f
                }
            }
        }

        private fun updateThumb(
            i: Int,
            x: Float,
            y: Float,
        ) {
            var dx = x - centerX[i]
            var dy = y - centerY[i]
            val thumbRadius = sizePx[i] * STICK_THUMB_RATIO / 2f
            val maxOffset = (halfW[i] - thumbRadius).coerceAtLeast(0f)
            val dist = hypot(dx, dy)
            if (dist > maxOffset && dist > 0f) {
                val scale = maxOffset / dist
                dx *= scale
                dy *= scale
            }
            thumbOffX[i] = dx
            thumbOffY[i] = dy
        }

        /** The 8-way direction at ([dx], [dy]) from a control's centre, beyond [deadZone] px; 0 inside it. */
        private fun direction(
            dx: Float,
            dy: Float,
            deadZone: Float,
        ): Int {
            if (hypot(dx, dy) < deadZone) return 0
            var degrees = Math.toDegrees(atan2(dy.toDouble(), dx.toDouble())).toFloat()
            if (degrees < 0) degrees += FULL_TURN
            val sector = ((degrees / SECTOR_DEGREES).roundToInt() % SECTOR_COUNT + SECTOR_COUNT) % SECTOR_COUNT
            return when (sector) {
                0 -> Dir.RIGHT
                1 -> Dir.DOWN or Dir.RIGHT
                2 -> Dir.DOWN
                3 -> Dir.DOWN or Dir.LEFT
                4 -> Dir.LEFT
                5 -> Dir.UP or Dir.LEFT
                6 -> Dir.UP
                7 -> Dir.UP or Dir.RIGHT
                else -> 0
            }
        }

        /** Sends onKey only for the bits that changed since the last call, and one haptic tick if any did. */
        private fun applyBits(
            i: Int,
            newBits: Int,
        ) {
            val old = dirBits[i]
            if (old == newBits) return
            val keys = controls[i].keys
            val bits = intArrayOf(Dir.UP, Dir.DOWN, Dir.LEFT, Dir.RIGHT)
            for (b in bits.indices) {
                val now = newBits and bits[b] != 0
                val was = old and bits[b] != 0
                if (now != was) onKey(keys[b], now)
            }
            dirBits[i] = newBits
            performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
        }

        private companion object {
            const val NONE = -1
            const val NO_BIT = 0
            const val MAX_POINTERS = 10
            const val SLACK_DP = 12f
            const val SHOULDER_ASPECT = 0.42f
            const val CORNER_DP = 6f
            const val DPAD_CELLS = 3f
            const val DPAD_ARM_DIVISOR = 3f
            const val DPAD_DEAD_ZONE = 0.15f
            const val STICK_DEAD_ZONE = 0.20f
            const val STICK_THUMB_RATIO = 0.35f
            const val FULL_TURN = 360f
            const val SECTOR_DEGREES = 45f
            const val SECTOR_COUNT = 8
        }
    }
}
