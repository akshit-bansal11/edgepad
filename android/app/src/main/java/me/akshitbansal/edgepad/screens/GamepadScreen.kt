package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.text.TextPaint
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.gamepad.Binding
import me.akshitbansal.edgepad.gamepad.ControlGeometry
import me.akshitbansal.edgepad.gamepad.ControlKind
import me.akshitbansal.edgepad.gamepad.GamepadLayout
import me.akshitbansal.edgepad.gamepad.PadAxis
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.PadButton
import me.akshitbansal.edgepad.protocol.PadStatus
import kotlin.math.abs
import kotlin.math.atan2
import kotlin.math.hypot
import kotlin.math.roundToInt

/**
 * Plays a [GamepadLayout]: draws every control exactly as [GamepadLayoutScreen]'s editor does, but live
 * and multi-touch, so a thumb can hold the stick while other fingers tap buttons.
 *
 * A class rather than a builder, like [ReconnectingScreen], because the laptop's answer to PAD_ATTACH
 * arrives after the screen is already up: [setStatus] is how the activity hands it over without rebuilding
 * the screen under the player's fingers.
 */
class GamepadScreen(
    ui: Ui,
    layout: GamepadLayout,
    status: PadStatus?,
    onKey: (code: Int, down: Boolean) -> Unit,
    onPad: (state: Frame.PadState) -> Unit,
    onBack: () -> Unit,
    onPreset: (String) -> Unit,
    onEdit: () -> Unit,
) {
    private val pad = Surface(ui.context, layout, onKey, onPad)

    val view: View =
        EditorFrame
            .build(ui, layout.name.uppercase(), pad, onBack) { close ->
                LinearLayout(ui.context).apply {
                    orientation = LinearLayout.VERTICAL
                    addView(
                        GamepadLayoutScreen.presets(ui, layout.name) { name ->
                            close()
                            onPreset(name)
                        },
                    )
                    addView(
                        ui.button(ui.string(R.string.gamepad_edit), Ui.Style.QUIET) {
                            close()
                            onEdit()
                        },
                        LinearLayout
                            .LayoutParams(
                                ViewGroup.LayoutParams.MATCH_PARENT,
                                ViewGroup.LayoutParams.WRAP_CONTENT,
                            ).apply {
                                topMargin = ui.dp(Space.S)
                            },
                    )
                }
            }.apply {
                keepScreenOn = true
                contentDescription = ui.string(R.string.gamepad_description)
            }

    init {
        setStatus(status)
    }

    /**
     * What the laptop last said about its virtual controller, or null while it has said nothing at all —
     * which is also what a laptop too old to know PAD_ATTACH leaves behind, since it drops the action and
     * never answers. Only READY gives the player a controller; everything else is the keyboard and a line
     * on screen saying which of the two reasons it is.
     */
    fun setStatus(status: PadStatus?) {
        pad.showMode(status)
    }

    /**
     * One direction bit per dpad/stick axis; up/down/left/right key indices in a control's keys line up
     * with these positions, as do [Surface.PAD_DIRECTIONS]'s four d-pad masks.
     */
    private object Dir {
        const val UP = 1
        const val DOWN = 2
        const val LEFT = 4
        const val RIGHT = 8
    }

    /**
     * Draws and drives every control at once. Geometry (px centres and sizes) is recomputed only in
     * [onSizeChanged] into pre-sized arrays; [onDraw] and touch handling allocate nothing beyond the one
     * [Frame.PadState] a change actually sends, which is the message itself. A pointer is tracked by its id
     * in [pointerToControl], never by its index, since indices shift when another finger lifts.
     */
    private class Surface(
        context: Context,
        private val layout: GamepadLayout,
        private val onKey: (code: Int, down: Boolean) -> Unit,
        private val onPad: (state: Frame.PadState) -> Unit,
    ) : View(context) {
        /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
        constructor(context: Context) : this(context, GamepadLayout("", emptyList()), { _, _ -> }, { })

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
        private val pull = IntArray(controls.size)

        // The whole pad as the laptop will read it, and the last one actually sent. Two fixed arrays rather
        // than two frames, so an event that changes nothing — a finger sliding inside a dead zone — costs
        // a comparison and no allocation at all.
        private val padState = IntArray(PAD_FIELDS)
        private val sentState = IntArray(PAD_FIELDS)

        /** True only while the laptop has a controller plugged in for us; otherwise this is a keyboard. */
        private var padMode = false

        /** Why this is a keyboard, drawn in the middle of the screen, or null when it is not one. */
        private var mode: String? = null

        // Which control (index into [controls], or -1) each pointer id currently holds.
        private val pointerToControl = IntArray(MAX_POINTERS) { NONE }

        private val box = RectF()
        private val stroke =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeWidth = Space.HAIR * density
            }
        private val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
        private val text: TextPaint = Type.pieceLabel(resources.displayMetrics)
        private val dpadCell = RectF()

        init {
            contentDescription = context.getString(R.string.gamepad_description)
        }

        /** Takes the laptop's last word on its controller and settles which of the two modes this pad is in. */
        fun showMode(status: PadStatus?) {
            padMode = status == PadStatus.READY
            mode =
                when (status) {
                    PadStatus.READY -> null
                    PadStatus.NO_DRIVER -> context.getString(R.string.gamepad_mode_no_driver)
                    PadStatus.ATTACH_FAILED -> context.getString(R.string.gamepad_mode_attach_failed)
                    null -> context.getString(R.string.gamepad_mode_waiting)
                }
            // The state of the view, not its name: a screen reader announces it with the description
            // already set, which is how the player who cannot see the line still hears it.
            stateDescription = mode
            invalidate()
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
                halfH[i] = ControlGeometry.halfHeight(control.kind, halfW[i])
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
            // The middle of the screen is the one place every preset leaves empty, and a pad that has
            // fallen back to the keyboard has to say so somewhere the player is already looking.
            val line = mode
            if (line != null) {
                text.color = palette.dim
                canvas.drawText(line, width / 2f, height / 2f, text)
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
            val corner = ControlGeometry.CORNER_DP * density
            if (pressed[i] && controls[i].binding !is Binding.Trigger) {
                fill.color = palette.ink
                canvas.drawRoundRect(box, corner, corner, fill)
                text.color = palette.background
                drawLabel(canvas, i)
                return
            }
            // A trigger fills from the bottom by how far it is pulled, in the faint tone rather than in ink,
            // because a trigger that looks identical at a tenth and at full is an analog control the player
            // has no way to aim. Filled behind the outline, so the label stays readable over it.
            if (controls[i].binding is Binding.Trigger && pull[i] > 0) {
                fill.color = palette.faint
                canvas.drawRect(
                    box.left,
                    box.bottom - box.height() * pull[i] / PadAxis.TRIGGER_MAX,
                    box.right,
                    box.bottom,
                    fill,
                )
            }
            stroke.color = palette.dim
            canvas.drawRoundRect(box, corner, corner, stroke)
            text.color = palette.ink
            drawLabel(canvas, i)
        }

        private fun drawDpad(
            canvas: Canvas,
            i: Int,
        ) {
            val third = box.width() / ControlGeometry.DPAD_CELLS
            for (row in 0 until ControlGeometry.DPAD_CELLS.toInt()) {
                for (col in 0 until ControlGeometry.DPAD_CELLS.toInt()) {
                    if (!ControlGeometry.isArmCell(row, col)) continue
                    val bit = dpadCellBit(row, col)
                    dpadCell.set(
                        box.left + col * third,
                        box.top + row * third,
                        box.left + (col + 1) * third,
                        box.top + (row + 1) * third,
                    )
                    if (bit != NO_BIT && dirBits[i] and bit != 0) {
                        fill.color = palette.ink
                        canvas.drawRect(dpadCell, fill)
                    } else {
                        stroke.color = palette.dim
                        canvas.drawRect(dpadCell, stroke)
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
            fill.color = palette.ink
            canvas.drawCircle(centerX[i] + thumbOffX[i], centerY[i] + thumbOffY[i], thumbRadius(i), fill)
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
            // Once per event, after every control the event touched has been taken in: one frame carries the
            // whole pad, so two fingers landing together are one frame and not two.
            if (padMode) sendPad()
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
                    pull[i] = PadAxis.trigger(y - centerY[i], halfH[i])
                    keys(i)?.let { onKey(it[0], true) }
                    performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
                }

                ControlKind.DPAD -> {
                    applyBits(i, direction(x - centerX[i], y - centerY[i], sizePx[i] * DPAD_DEAD_ZONE))
                }

                ControlKind.STICK -> {
                    updateThumb(i, x, y)
                    stickKeys(i, x, y)
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
                    stickKeys(i, x, y)
                }

                ControlKind.SHOULDER -> {
                    // A trigger is squeezed further by sliding down it, so a held finger keeps reporting.
                    pull[i] = PadAxis.trigger(y - centerY[i], halfH[i])
                }

                ControlKind.BUTTON -> {
                    Unit
                }
            }
        }

        private fun release(i: Int) {
            when (controls[i].kind) {
                ControlKind.BUTTON, ControlKind.SHOULDER -> {
                    pressed[i] = false
                    pull[i] = 0
                    keys(i)?.let { onKey(it[0], false) }
                }

                ControlKind.DPAD, ControlKind.STICK -> {
                    applyBits(i, 0)
                    thumbOffX[i] = 0f
                    thumbOffY[i] = 0f
                }
            }
        }

        /** The keyboard codes this control presses, or null when it drives the controller instead. */
        private fun keys(i: Int): List<Int>? = (controls[i].binding as? Binding.Keys)?.codes

        /** Half the thumb a stick draws, which is also what the thumb's own travel is short of the rim. */
        private fun thumbRadius(i: Int): Float = sizePx[i] * STICK_THUMB_RATIO / 2f

        private fun travel(i: Int): Float = (halfW[i] - thumbRadius(i)).coerceAtLeast(0f)

        /**
         * A stick bound to keys still answers in eight sectors, because four keys cannot say anything else.
         * A stick bound to the controller skips this entirely: it has a magnitude to send, and a haptic tick
         * at every 45 degrees would fire all the way round a slow circle.
         */
        private fun stickKeys(
            i: Int,
            x: Float,
            y: Float,
        ) {
            if (keys(i) == null) return
            applyBits(i, direction(x - centerX[i], y - centerY[i], sizePx[i] * STICK_DEAD_ZONE))
        }

        private fun updateThumb(
            i: Int,
            x: Float,
            y: Float,
        ) {
            var dx = x - centerX[i]
            var dy = y - centerY[i]
            val maxOffset = travel(i)
            val dist = hypot(dx, dy)
            if (dist > maxOffset && dist > 0f) {
                val scale = maxOffset / dist
                dx *= scale
                dy *= scale
            }
            thumbOffX[i] = dx
            thumbOffY[i] = dy
        }

        /**
         * The whole pad from the current state of every control, sent only when it differs from the last
         * one sent. A PAD_STATE frame is a snapshot and not an edge, so an event that moved nothing is an
         * event with nothing to say.
         */
        private fun sendPad() {
            padState.fill(0)
            for (i in controls.indices) gather(i)
            if (padState.contentEquals(sentState)) return
            padState.copyInto(sentState)
            onPad(
                Frame.PadState(
                    padState[BUTTONS],
                    padState[LT],
                    padState[RT],
                    padState[LX],
                    padState[LY],
                    padState[RX],
                    padState[RY],
                ),
            )
        }

        /** What one control contributes to [padState]. A keyboard binding contributes nothing; it sends keys. */
        private fun gather(i: Int) {
            when (val binding = controls[i].binding) {
                is Binding.Keys -> Unit

                is Binding.Button -> {
                    if (pressed[i]) padState[BUTTONS] = padState[BUTTONS] or binding.button.mask
                }

                Binding.Dpad -> {
                    padState[BUTTONS] = padState[BUTTONS] or dpadMask(dirBits[i])
                }

                is Binding.Trigger -> {
                    // Two controls on one trigger is a layout the editor allows, so the harder squeeze wins
                    // rather than whichever control happens to be later in the list.
                    val slot = if (binding.side == Binding.Side.RIGHT) RT else LT
                    padState[slot] = maxOf(padState[slot], pull[i])
                }

                is Binding.Stick -> {
                    // The axes come from the same clamped offset the thumb is drawn at, so what the player
                    // sees and what the laptop is told cannot drift apart. Y is negated because the screen
                    // grows downwards and XInput's axis grows up.
                    val slot = if (binding.side == Binding.Side.RIGHT) RX else LX
                    val reach = travel(i)
                    val distance = hypot(thumbOffX[i], thumbOffY[i])
                    val dead = reach * STICK_AXIS_DEAD_ZONE
                    padState[slot] = PadAxis.stick(thumbOffX[i], distance, reach, dead)
                    padState[slot + 1] = PadAxis.stick(-thumbOffY[i], distance, reach, dead)
                }
            }
        }

        /** The d-pad's four XInput bits for the four directions [dirBits] holds. */
        private fun dpadMask(bits: Int): Int {
            var mask = 0
            for (b in DIR_BITS.indices) {
                if (bits and DIR_BITS[b] != 0) mask = mask or PAD_DIRECTIONS[b]
            }
            return mask
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

        /**
         * Records the new directions, sends onKey only for the bits that changed since the last call when
         * this control presses keys at all, and ticks once if any did. A d-pad on the controller keeps the
         * bits for the drawing and for [gather]; it has no keys to send.
         */
        private fun applyBits(
            i: Int,
            newBits: Int,
        ) {
            val old = dirBits[i]
            if (old == newBits) return
            val codes = keys(i)
            if (codes != null) {
                for (b in DIR_BITS.indices) {
                    val now = newBits and DIR_BITS[b] != 0
                    val was = old and DIR_BITS[b] != 0
                    if (now != was) onKey(codes[b], now)
                }
            }
            dirBits[i] = newBits
            performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
        }

        private companion object {
            val DIR_BITS = intArrayOf(Dir.UP, Dir.DOWN, Dir.LEFT, Dir.RIGHT)

            /** The d-pad's XInput masks, in [DIR_BITS]' order. */
            val PAD_DIRECTIONS =
                intArrayOf(
                    PadButton.DPAD_UP.mask,
                    PadButton.DPAD_DOWN.mask,
                    PadButton.DPAD_LEFT.mask,
                    PadButton.DPAD_RIGHT.mask,
                )

            // Where each field of a PadState sits while it is being assembled. LY and RY follow their own X
            // on purpose: a stick writes both of its axes from one slot number.
            const val BUTTONS = 0
            const val LT = 1
            const val RT = 2
            const val LX = 3
            const val LY = 4
            const val RX = 5
            const val RY = 6
            const val PAD_FIELDS = 7

            const val NONE = -1
            const val NO_BIT = 0
            const val MAX_POINTERS = 10
            const val SLACK_DP = 12f
            const val DPAD_ARM_DIVISOR = 3f
            const val DPAD_DEAD_ZONE = 0.15f
            const val STICK_DEAD_ZONE = 0.20f

            /**
             * The analog stick's dead zone, as a fraction of the thumb's travel rather than of the control's
             * size. [STICK_DEAD_ZONE] is measured against a finger that may be anywhere on the screen, while
             * the thumb is clamped to the rim: the same 0.20 of the diameter would be well over half of the
             * travel and the stick would feel like it barely moved.
             */
            const val STICK_AXIS_DEAD_ZONE = 0.15f
            const val STICK_THUMB_RATIO = 0.35f
            const val FULL_TURN = 360f
            const val SECTOR_DEGREES = 45f
            const val SECTOR_COUNT = 8
        }
    }
}
