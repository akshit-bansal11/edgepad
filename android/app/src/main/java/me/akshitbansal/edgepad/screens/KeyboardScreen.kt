package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.text.TextPaint
import android.util.TypedValue
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.DeviceOs

private const val CODE_SPACE = 0x20
private const val CODE_LEFT = 0x25
private const val CODE_DOWN = 0x28

private const val PADDING_DP = 6f

/** The gap between keys, shrunk on a crowded board to this share of a cell so the keys do not vanish. */
private const val GAP_DP = 6f
private const val GAP_SHARE = 0.15f

/** A key's corners, and never more than a third of its width, so a narrow key stays a key and not a pill. */
private const val CORNER_DP = 10f
private const val CORNER_SHARE = 3f

/** How far below a key its shadow line shows on the light board. */
private const val SHADOW_DP = 1f

/** The label size asked for, before the text-size setting scales it and [KeyboardView.fitLabels] fits it. */
private const val LABEL_SP = 14f

/** The small legend naming a key's other side, as a share of the label size. */
private const val SECONDARY_SHARE = 0.6f

/** A full on-screen keyboard, drawn and hit-tested on one canvas so several keys can be held at once. */
object KeyboardScreen {
    fun build(
        ui: Ui,
        textScale: Float,
        size: KeyboardSize,
        mode: FnMode,
        os: DeviceOs?,
        onKey: (code: Int, down: Boolean) -> Unit,
        onAction: (ActionId) -> Unit,
        onSettings: () -> Unit,
        onBack: () -> Unit,
    ): View {
        val header =
            ui.bar(
                ui.string(R.string.keyboard_title),
                onBack,
                ui.icon(Glyph.Shape.GEAR, ui.string(R.string.settings_title), onSettings),
                backLabel = ui.string(R.string.back_to_surface),
            )
        val keyboard = KeyboardView(ui.context, textScale, layoutFor(size), mode, os, onKey, onAction)
        return LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(ui.palette.background)
            ui.padToBars(this)
            addView(header)
            addView(keyboard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
        }
    }

    /**
     * Everything that is not a character: named keys (Esc, Tab, Shift…) and the arrows. They sit a step greyer
     * than the character keys, as on an iOS keyboard. Space is the one named key that types.
     */
    private fun isFunctionKey(cap: Cap): Boolean {
        val code = (cap.base as? KeyOut.Code)?.code ?: 0
        return (cap.label.length > 1 && code != CODE_SPACE) || code in CODE_LEFT..CODE_DOWN
    }

    /**
     * One finger's key, resolved once at touch-down: the release and every repeat use this [wire], so Fn
     * changing during the hold cannot release a different key from the one that went down.
     */
    private class Press(
        val index: Int,
        val wire: Int,
        val sticky: Boolean,
    )

    /**
     * An iOS-style board: rounded keys with no outline, character keys on the brighter fill and function
     * keys a step greyer, a soft shadow line under each key on the light board, and a held or armed key in
     * the accent. A key that would do nothing is drawn dimmed.
     *
     * Draws and hit-tests every key itself, rather than inflating one view per key, so several pointers
     * can each hold a different key at once: a modifier plus a letter, or four arrow/WASD keys in a game.
     * Keys are tracked by index, not by [Cap]: two caps can be equal (the two Fn keys), and each is its own key.
     */
    private class KeyboardView(
        context: Context,
        textScale: Float,
        private val board: Layout,
        private val mode: FnMode,
        os: DeviceOs?,
        onKey: (code: Int, down: Boolean) -> Unit,
        onAction: (ActionId) -> Unit,
    ) : View(context) {
        /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
        constructor(context: Context) : this(
            context,
            Settings.DEFAULT_KEY_TEXT_SCALE,
            layoutFor(Settings.DEFAULT_KEYBOARD_SIZE),
            Settings.DEFAULT_FUNCTION_KEYS,
            null,
            { _, _ -> },
            {},
        )

        private val density = resources.displayMetrics.density
        private val palette = Palette.of(context)
        private val caps = board.rows.flatten().filterIsInstance<Cap>()
        private val rects = List(caps.size) { RectF() }
        private val legends = caps.map { labelFor(it, os) }
        private val functionKeys = caps.map { isFunctionKey(it) }

        /** What each modifier and Fn key is called in [modifiers]; meaningless on any other key. */
        private val modifierCodes = IntArray(caps.size) { wireOf(caps[it], caps[it].base) ?: 0 }

        /** How many fingers are on each key. */
        private val pressCount = IntArray(caps.size)
        private val padding = PADDING_DP * density
        private val maxGap = GAP_DP * density
        private val corner = CORNER_DP * density
        private val shadow = SHADOW_DP * density
        private val labelInset = Space.XS * density

        // On the dark board the character keys take the lighter fill and the function keys the card, so both
        // still step up from the ground; on the light board it is white keys and grey function keys.
        private val keyFill = if (palette.dark) palette.faint else palette.card
        private val functionFill = if (palette.dark) palette.card else palette.off

        /** The size asked for; [fitLabels] may draw smaller, never larger. */
        private val wantedTextSize =
            TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, LABEL_SP, resources.displayMetrics) * textScale

        private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
        private val shadowPaint =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.FILL
                color = palette.line
            }
        private val labelOn =
            TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
                typeface = Type.bold
                textAlign = Paint.Align.CENTER
                color = palette.onAccent
                textSize = wantedTextSize
            }
        private val labelOff = paintLike(labelOn, palette.ink)
        private val labelDim = paintLike(labelOn, palette.dim)
        private val secondaryOn = paintLike(labelOn, palette.onAccent)
        private val secondaryOff = paintLike(labelOn, palette.dim)

        private val pointerKeys = mutableMapOf<Int, Press>()

        // Fn goes through the latch like Shift, so it is sticky the same way; it stops here and never reaches
        // the laptop. The latch also carries the action codes, which go out as actions on down only.
        private val send: (Int, Boolean) -> Unit = { code, down -> sendWire(code, down, onKey, onAction) }
        private val modifiers = ModifierLatch(send)

        // Repeats go through the latch like the first press, so an armed modifier wraps them too; no haptic.
        private val keyRepeat =
            KeyRepeat(
                post = { task, delay -> postDelayed(task, delay) },
                cancel = { task -> removeCallbacks(task) },
                fire = { code -> modifiers.keyDown(code) },
            )

        init {
            contentDescription = context.getString(R.string.keyboard_description)
            isClickable = true
            isFocusable = true
        }

        private fun paintLike(
            paint: TextPaint,
            tint: Int,
        ): TextPaint =
            TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
                set(paint)
                color = tint
            }

        override fun onSizeChanged(
            w: Int,
            h: Int,
            oldw: Int,
            oldh: Int,
        ) {
            super.onSizeChanged(w, h, oldw, oldh)
            val unit = (w - 2 * padding) / board.width
            val rowHeight = (h - 2 * padding) / board.rows.size
            val gap = minOf(maxGap, GAP_SHARE * minOf(unit, rowHeight))
            var keyIndex = 0
            board.rows.forEachIndexed { rowIndex, row ->
                val top = padding + rowIndex * rowHeight + gap / 2
                var column = 0f
                for (item in row) {
                    when (item) {
                        is Gap -> {
                            column += item.units
                        }

                        is Cap -> {
                            val left = padding + column * unit + gap / 2
                            val right = left + item.units * unit - gap
                            rects[keyIndex].set(left, top, right, top + item.rows * rowHeight - gap)
                            keyIndex++
                            column += item.units
                        }
                    }
                }
            }
            fitLabels()
        }

        /**
         * One text size for every key, so labels stay consistent: the wanted size, shrunk until each label's
         * width and the line's full height fit inside its own key with [labelInset] to spare on every side.
         * A key can show either of its two labels large, so both are measured.
         * Width and height both scale linearly with the size, so one measurement at the wanted size is enough.
         * Runs here rather than in onDraw, where lint rejects the allocation getFontMetrics makes.
         */
        private fun fitLabels() {
            labelOn.textSize = wantedTextSize
            val metrics = labelOn.fontMetrics
            val lineHeight = metrics.descent - metrics.ascent
            var fit = 1f
            for (i in caps.indices) {
                // A key with no room inside its inset (a zero-sized first layout) fits a size of zero: no text at all.
                val availableWidth = maxOf(0f, rects[i].width() - 2 * labelInset)
                val availableHeight = maxOf(0f, rects[i].height() - 2 * labelInset)
                val fnWidth = caps[i].fnLabel?.let { labelOn.measureText(it) } ?: 0f
                val width = maxOf(labelOn.measureText(legends[i]), fnWidth)
                if (width > 0f) fit = minOf(fit, availableWidth / width)
                if (lineHeight > 0f) fit = minOf(fit, availableHeight / lineHeight)
            }
            labelOn.textSize = wantedTextSize * fit
            labelOff.textSize = labelOn.textSize
            labelDim.textSize = labelOn.textSize
            secondaryOn.textSize = labelOn.textSize * SECONDARY_SHARE
            secondaryOff.textSize = secondaryOn.textSize
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            val fnActive = modifiers.isActive(FN_CODE)
            for (i in caps.indices) {
                val cap = caps[i]
                val rect = rects[i]
                val out = resolve(cap, fnActive, mode)
                val modifier = cap.role != KeyRole.NORMAL
                val held = pressCount[i] > 0 || (modifier && modifiers.isActive(modifierCodes[i]))
                val radius = minOf(corner, rect.width() / CORNER_SHARE)
                if (!palette.dark) {
                    // The shadow is the same key shape one step lower; the key drawn over it leaves only its
                    // bottom edge showing, the soft line under an iOS key.
                    rect.offset(0f, shadow)
                    canvas.drawRoundRect(rect, radius, radius, shadowPaint)
                    rect.offset(0f, -shadow)
                }
                fillPaint.color =
                    when {
                        held -> palette.accent
                        functionKeys[i] -> functionFill
                        else -> keyFill
                    }
                canvas.drawRoundRect(rect, radius, radius, fillPaint)
                // resolve() hands back this very object exactly when the key's Fn side is the active one.
                val fnSide = out === cap.fn
                val primary = if (fnSide) cap.fnLabel ?: legends[i] else legends[i]
                val other = if (fnSide) legends[i] else cap.fnLabel
                val label =
                    when {
                        held -> labelOn
                        !modifier && out === KeyOut.None -> labelDim
                        else -> labelOff
                    }
                canvas.drawText(primary, rect.centerX(), rect.centerY() + label.textSize * Type.CAP_CENTRE, label)
                if (other != null) {
                    val small = if (held) secondaryOn else secondaryOff
                    canvas.drawText(other, rect.centerX(), rect.top + labelInset + small.textSize, small)
                }
            }
        }

        private fun keyAt(
            x: Float,
            y: Float,
        ): Int? {
            for (i in caps.indices) {
                if (rects[i].contains(x, y)) return i
            }
            return null
        }

        override fun onTouchEvent(event: MotionEvent): Boolean {
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                    val index = event.actionIndex
                    // True, not false, on a hole or a dead key: false would drop the gesture and every finger in it.
                    val key = keyAt(event.getX(index), event.getY(index)) ?: return true
                    val cap = caps[key]
                    val out = resolve(cap, modifiers.isActive(FN_CODE), mode)
                    val wire = wireOf(cap, out) ?: return true
                    val sticky = cap.role != KeyRole.NORMAL
                    performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
                    pointerKeys[event.getPointerId(index)] = Press(key, wire, sticky)
                    pressCount[key]++
                    if (sticky) {
                        modifiers.modifierDown(wire)
                    } else {
                        modifiers.keyDown(wire)
                        if (repeats(cap, out)) keyRepeat.press(wire)
                    }
                }

                MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> {
                    val index = event.actionIndex
                    val press = pointerKeys.remove(event.getPointerId(index)) ?: return true
                    pressCount[press.index]--
                    if (press.sticky) {
                        modifiers.modifierUp(press.wire)
                    } else {
                        keyRepeat.release(press.wire)
                        modifiers.keyUp(press.wire)
                    }
                    performClick()
                }

                MotionEvent.ACTION_CANCEL -> {
                    releaseAll()
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

        // Lets go of every key still down; also run on detach, when the up events will never arrive.
        private fun releaseAll() {
            keyRepeat.stop()
            for (press in pointerKeys.values) if (!press.sticky) send(press.wire, false)
            pointerKeys.clear()
            pressCount.fill(0)
            modifiers.cancel()
        }

        override fun onDetachedFromWindow() {
            releaseAll()
            super.onDetachedFromWindow()
        }
    }
}
