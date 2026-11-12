package me.akshitbansal.edgepad.screens

import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.LayerDrawable
import android.text.InputFilter
import android.text.InputType
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.inputmethod.EditorInfo
import android.widget.EditText
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import java.util.Locale

/**
 * A colour picked from a strip of round swatches, under a row with the hex of what is chosen and a swatch of
 * it. The chosen swatch wears an accent ring a little way out from its edge. Each swatch is drawn small in the
 * middle of a full-size touch target, and is named for a screen reader by its colour rather than its hex.
 * With `editable` the hex is a field too, so a colour that is not on the strip can be typed in.
 */
object ColorPicker {
    private const val SWATCH_DP = 28f
    private const val CHOICE_DP = 30f
    private const val RING_DP = 2f

    /** The ring's gap plus its own width: how far it stands out from the swatch on every side. */
    private const val RING_OUT_DP = 4f
    private const val HEX_GAP_DP = 10f
    private const val HEX_FIELD_DP = 84f
    private const val HEX_LENGTH = 7
    private const val HEX_DIGITS = 6
    private const val HEX_BASE = 16
    private const val RGB = 0xFFFFFF
    private const val OPAQUE = 0xFF shl 24

    /** Greys first, then round the wheel; a colour not in the strip stays chosen until one is tapped. */
    private val choices =
        listOf(
            0xFFFFFFFF.toInt() to R.string.color_white,
            0xFFC8C8CC.toInt() to R.string.color_light_grey,
            0xFF84848C.toInt() to R.string.color_grey,
            0xFF3A3A40.toInt() to R.string.color_dark_grey,
            0xFF000000.toInt() to R.string.color_black,
            0xFFE53935.toInt() to R.string.color_red,
            0xFFFB8C00.toInt() to R.string.color_orange,
            0xFFFDD835.toInt() to R.string.color_yellow,
            0xFF43A047.toInt() to R.string.color_green,
            0xFF00ACC1.toInt() to R.string.color_cyan,
            0xFF1E88E5.toInt() to R.string.color_blue,
            0xFF3949AB.toInt() to R.string.color_indigo,
            0xFF8E24AA.toInt() to R.string.color_purple,
            0xFFD81B60.toInt() to R.string.color_pink,
        )

    fun build(
        ui: Ui,
        label: CharSequence,
        initial: Int,
        editable: Boolean = false,
        onChange: (Int) -> Unit,
    ): View {
        val swatch =
            View(ui.context).apply {
                background = circle(ui, initial)
                contentDescription = label
            }
        var current = initial
        val hex: TextView =
            if (editable) hexField(ui, initial) else ui.secondary(hexOf(initial), Type.VALUE, ui.palette.dim)
        val rings = ArrayList<Triple<Int, GradientDrawable, View>>(choices.size)

        fun mark(chosen: Int) {
            for ((c, ring, choice) in rings) {
                val on = c == chosen
                outline(ui, ring, on)
                choice.isSelected = on
                choice.stateDescription = if (on) ui.string(R.string.chosen) else null
            }
        }

        fun choose(color: Int) {
            current = color
            (swatch.background as GradientDrawable).setColor(color)
            hex.text = hexOf(color)
            mark(color)
            onChange(color)
        }

        // What was typed is taken when the keyboard is done with it or the field is left; anything that is not a
        // colour puts back the one that is chosen, rather than leaving the field saying something else.
        fun commit() {
            val typed = colourOf(hex.text.toString())
            if (typed == null) {
                hex.text = hexOf(current)
            } else if (typed != current) {
                choose(typed)
            }
        }
        if (editable) {
            hex.setOnEditorActionListener { _, _, _ ->
                commit()
                true
            }
            hex.setOnFocusChangeListener { _, focused -> if (!focused) commit() }
        }
        val strip =
            LinearLayout(ui.context).apply {
                // The ring and the swatch are centred in the touch target, with this much clear room round them.
                val room = (ui.dp(Space.TOUCH) - ui.dp(CHOICE_DP + 2 * RING_OUT_DP)) / 2
                val out = room + ui.dp(RING_OUT_DP)
                for ((color, name) in choices) {
                    val ring = GradientDrawable().apply { shape = GradientDrawable.OVAL }
                    val choice =
                        View(ui.context).apply {
                            // The ring behind, the swatch inset from it by the ring and its gap. The gap is left
                            // clear, so it shows the card the picker sits on, as the design's does.
                            background =
                                LayerDrawable(arrayOf(ring, circle(ui, color))).apply {
                                    setLayerInset(0, room, room, room, room)
                                    setLayerInset(1, out, out, out, out)
                                }
                            contentDescription = ui.string(name)
                            ui.tappable(this, Space.TOUCH / 2) { choose(color) }
                        }
                    rings.add(Triple(color, ring, choice))
                    addView(choice, LinearLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH)))
                }
                mark(initial)
            }
        return LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            val head =
                LinearLayout(ui.context).apply {
                    gravity = Gravity.CENTER_VERTICAL
                    minimumHeight = ui.dp(Space.ROW)
                    addView(
                        ui.text(label, Type.BODY, ui.palette.ink),
                        LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f),
                    )
                    addView(
                        hex,
                        LinearLayout
                            .LayoutParams(
                                LinearLayout.LayoutParams.WRAP_CONTENT,
                                LinearLayout.LayoutParams.WRAP_CONTENT,
                            ).apply { marginEnd = ui.dp(HEX_GAP_DP) },
                    )
                    addView(swatch, LinearLayout.LayoutParams(ui.dp(SWATCH_DP), ui.dp(SWATCH_DP)))
                }
            addView(head)
            addView(
                HorizontalScrollView(ui.context).apply {
                    isHorizontalScrollBarEnabled = false
                    setPadding(0, 0, 0, ui.dp(Space.M))
                    addView(strip)
                },
            )
        }
    }

    /** A round swatch of [color], with a hairline round it so white on a white card still shows its edge. */
    private fun circle(
        ui: Ui,
        color: Int,
    ): GradientDrawable =
        GradientDrawable().apply {
            shape = GradientDrawable.OVAL
            setColor(color)
            setStroke(ui.dp(Space.HAIR), ui.palette.line)
        }

    /** The chosen swatch's ring is drawn in the accent; the others' is there but clear, so nothing shifts. */
    private fun outline(
        ui: Ui,
        ring: GradientDrawable,
        chosen: Boolean,
    ) {
        ring.setStroke(ui.dp(RING_DP), if (chosen) ui.palette.accent else Color.TRANSPARENT)
    }

    private fun hexOf(color: Int): String = "#%06X".format(Locale.ROOT, color and RGB)

    /** The opaque colour [text] spells, as `#RRGGBB` or `RRGGBB`, or null when it spells none. */
    internal fun colourOf(text: String): Int? {
        val digits = text.trim().removePrefix("#")
        if (digits.length != HEX_DIGITS || digits.any { Character.digit(it, HEX_BASE) < 0 }) return null
        return OPAQUE or digits.toInt(HEX_BASE)
    }

    /** The hex as a field: capitals, no suggestions, and a touch target as tall as a row. */
    private fun hexField(
        ui: Ui,
        initial: Int,
    ): EditText =
        EditText(ui.context).apply {
            setText(hexOf(initial))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, Type.VALUE)
            setTextColor(ui.palette.dim)
            typeface = Type.face
            background = null
            setSingleLine()
            gravity = Gravity.CENTER_VERTICAL or Gravity.END
            inputType =
                InputType.TYPE_CLASS_TEXT or
                InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS or
                InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS
            imeOptions = EditorInfo.IME_ACTION_DONE
            importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
            filters = arrayOf(InputFilter.LengthFilter(HEX_LENGTH))
            minWidth = ui.dp(HEX_FIELD_DP)
            minHeight = ui.dp(Space.TOUCH)
            contentDescription = ui.string(R.string.color_hex)
        }
}
