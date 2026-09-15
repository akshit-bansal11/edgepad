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

// Windows virtual-key codes, sent to the laptop over the existing key-frame protocol.
private const val CODE_ESC = 0x1B
private const val CODE_F1 = 0x70
private const val CODE_DEL = 0x2E
private const val CODE_BACKTICK = 0xC0
private const val CODE_DIGIT_0 = 0x30
private const val CODE_MINUS = 0xBD
private const val CODE_EQUALS = 0xBB
private const val CODE_BACKSPACE = 0x08
private const val CODE_TAB = 0x09
private const val CODE_A = 0x41
private const val CODE_OPEN_BRACKET = 0xDB
private const val CODE_CLOSE_BRACKET = 0xDD
private const val CODE_BACKSLASH = 0xDC
private const val CODE_CAPS = 0x14
private const val CODE_SEMICOLON = 0xBA
private const val CODE_QUOTE = 0xDE
private const val CODE_ENTER = 0x0D
private const val CODE_SHIFT = 0x10
private const val CODE_COMMA = 0xBC
private const val CODE_PERIOD = 0xBE
private const val CODE_SLASH = 0xBF
private const val CODE_CTRL = 0x11
private const val CODE_WIN = 0x5B
private const val CODE_ALT = 0x12
private const val CODE_SPACE = 0x20
private const val CODE_MENU = 0x5D
private const val CODE_LEFT = 0x25
private const val CODE_UP = 0x26
private const val CODE_RIGHT = 0x27
private const val CODE_DOWN = 0x28

private const val F_KEY_COUNT = 12
private const val DIGIT_COUNT = 10

// Key widths, in row units. A row's key fills (its share of units / the row's unit total) of the width.
private const val W1 = 1f
private const val W1_25 = 1.25f
private const val W1_5 = 1.5f
private const val W1_75 = 1.75f
private const val W2 = 2f
private const val W2_25 = 2.25f
private const val W2_75 = 2.75f
private const val W6_25 = 6.25f

private const val ROW_COUNT = 6
private const val PADDING_DP = 4f
private const val GAP_DP = 2f
private const val CORNER_DP = 4f
private const val STROKE_DP = 1f

/** A full on-screen keyboard: six rows, drawn and hit-tested on one canvas so several keys can be held at once. */
object KeyboardScreen {
    fun build(
        ui: Ui,
        onKey: (code: Int, down: Boolean) -> Unit,
        onBack: () -> Unit,
    ): View {
        val header = ui.bar(ui.string(R.string.keyboard_title), onBack)
        val keyboard = KeyboardView(ui.context, onKey)
        return LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(ui.palette.background)
            addView(header)
            addView(keyboard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
        }
    }

    // Reference identity, deliberately: two keys can share a label, code and width (the two Ctrl keys,
    // the two Shift keys), and each must be tracked as held independently of its twin.
    private class Key(
        val label: String,
        val code: Int,
        val units: Float,
        val sticky: Boolean = false,
    )

    private fun buildRows(): List<List<Key>> {
        val row0 =
            listOf(Key("Esc", CODE_ESC, W1)) +
                (0 until F_KEY_COUNT).map { Key("F${it + 1}", CODE_F1 + it, W1) } +
                listOf(Key("Del", CODE_DEL, W1))
        val row1 =
            listOf(Key("`", CODE_BACKTICK, W1)) +
                (0 until DIGIT_COUNT).map { Key(('0' + it).toString(), CODE_DIGIT_0 + it, W1) } +
                listOf(
                    Key("-", CODE_MINUS, W1),
                    Key("=", CODE_EQUALS, W1),
                    Key("Backspace", CODE_BACKSPACE, W2),
                )
        val row2 =
            listOf(Key("Tab", CODE_TAB, W1_5)) +
                "qwertyuiop".map { Key(it.uppercase(), CODE_A + (it - 'a'), W1) } +
                listOf(
                    Key("[", CODE_OPEN_BRACKET, W1),
                    Key("]", CODE_CLOSE_BRACKET, W1),
                    Key("\\", CODE_BACKSLASH, W1_5),
                )
        val row3 =
            listOf(Key("Caps", CODE_CAPS, W1_75)) +
                "asdfghjkl".map { Key(it.uppercase(), CODE_A + (it - 'a'), W1) } +
                listOf(
                    Key(";", CODE_SEMICOLON, W1),
                    Key("'", CODE_QUOTE, W1),
                    Key("Enter", CODE_ENTER, W2_25),
                )
        val row4 =
            listOf(Key("Shift", CODE_SHIFT, W2_25, sticky = true)) +
                "zxcvbnm".map { Key(it.uppercase(), CODE_A + (it - 'a'), W1) } +
                listOf(
                    Key(",", CODE_COMMA, W1),
                    Key(".", CODE_PERIOD, W1),
                    Key("/", CODE_SLASH, W1),
                    Key("Shift", CODE_SHIFT, W2_75, sticky = true),
                )
        val row5 =
            listOf(
                Key("Ctrl", CODE_CTRL, W1_25, sticky = true),
                Key("Win", CODE_WIN, W1_25),
                Key("Alt", CODE_ALT, W1_25, sticky = true),
                Key("Space", CODE_SPACE, W6_25),
                Key("Alt", CODE_ALT, W1_25, sticky = true),
                Key("Menu", CODE_MENU, W1_25, sticky = true),
                Key("Ctrl", CODE_CTRL, W1_25, sticky = true),
                Key("←", CODE_LEFT, W1),
                Key("↑", CODE_UP, W1),
                Key("↓", CODE_DOWN, W1),
                Key("→", CODE_RIGHT, W1),
            )
        return listOf(row0, row1, row2, row3, row4, row5)
    }

    /**
     * Draws and hit-tests every key itself, rather than inflating one view per key, so several pointers
     * can each hold a different key at once: a modifier plus a letter, or four arrow/WASD keys in a game.
     */
    private class KeyboardView(
        context: Context,
        private val onKey: (code: Int, down: Boolean) -> Unit,
    ) : View(context) {
        /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
        constructor(context: Context) : this(context, { _, _ -> })

        private val density = resources.displayMetrics.density
        private val palette = Palette.of(context)
        private val rows = buildRows()
        private val keys = rows.flatten()
        private val rects = List(keys.size) { RectF() }
        private val padding = PADDING_DP * density
        private val gap = GAP_DP * density
        private val corner = CORNER_DP * density

        private val fillPaint =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.FILL
                color = palette.ink
            }
        private val strokePaint =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeWidth = STROKE_DP * density
                color = palette.line
            }
        private val labelOn =
            TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
                typeface = Type.face
                textAlign = Paint.Align.CENTER
                letterSpacing = Type.TRACKING_WIDE
                color = palette.background
                textSize = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, Type.MICRO, resources.displayMetrics)
            }
        private val labelOff =
            TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
                set(labelOn)
                color = palette.ink
            }

        private val pointerKeys = mutableMapOf<Int, Key>()
        private val modifiers = ModifierLatch(onKey)

        init {
            contentDescription = context.getString(R.string.keyboard_description)
            isClickable = true
            isFocusable = true
        }

        override fun onSizeChanged(
            w: Int,
            h: Int,
            oldw: Int,
            oldh: Int,
        ) {
            super.onSizeChanged(w, h, oldw, oldh)
            val rowHeight = (h - 2 * padding - (ROW_COUNT - 1) * gap) / ROW_COUNT
            var keyIndex = 0
            rows.forEachIndexed { rowIndex, row ->
                val top = padding + rowIndex * (rowHeight + gap)
                val totalUnits = row.sumOf { it.units.toDouble() }.toFloat()
                val unitWidth = (w - 2 * padding - (row.size - 1) * gap) / totalUnits
                var x = padding
                for (key in row) {
                    val keyWidth = unitWidth * key.units
                    rects[keyIndex].set(x, top, x + keyWidth, top + rowHeight)
                    x += keyWidth + gap
                    keyIndex++
                }
            }
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            for (i in keys.indices) {
                val key = keys[i]
                val rect = rects[i]
                val held = pointerKeys.containsValue(key) || (key.sticky && modifiers.isArmed(key.code))
                if (held) {
                    canvas.drawRoundRect(rect, corner, corner, fillPaint)
                } else {
                    canvas.drawRoundRect(rect, corner, corner, strokePaint)
                }
                val label = if (held) labelOn else labelOff
                canvas.drawText(key.label, rect.centerX(), rect.centerY() + label.textSize * Type.CAP_CENTRE, label)
            }
        }

        private fun keyAt(
            x: Float,
            y: Float,
        ): Key? {
            for (i in keys.indices) {
                if (rects[i].contains(x, y)) return keys[i]
            }
            return null
        }

        override fun onTouchEvent(event: MotionEvent): Boolean {
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                    val index = event.actionIndex
                    val key = keyAt(event.getX(index), event.getY(index)) ?: return false
                    performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
                    pointerKeys[event.getPointerId(index)] = key
                    if (key.sticky) modifiers.modifierDown(key.code) else modifiers.keyDown(key.code)
                }

                MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> {
                    val index = event.actionIndex
                    val key = pointerKeys.remove(event.getPointerId(index)) ?: return false
                    if (key.sticky) modifiers.modifierUp(key.code) else modifiers.keyUp(key.code)
                    performClick()
                }

                MotionEvent.ACTION_CANCEL -> {
                    for (key in pointerKeys.values) if (!key.sticky) onKey(key.code, false)
                    pointerKeys.clear()
                    modifiers.cancel()
                }

                else -> {
                    return false
                }
            }
            invalidate()
            return true
        }

        override fun performClick(): Boolean {
            super.performClick()
            return true
        }
    }
}
