package me.akshitbansal.edgepad.screens

import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.LayerDrawable
import android.view.Gravity
import android.view.View
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import java.util.Locale

/**
 * A colour picked from a strip of round swatches, under a row with the hex of what is chosen and a swatch of
 * it. The chosen swatch wears an accent ring a little way out from its edge. Each swatch is drawn small in the
 * middle of a full-size touch target, and is named for a screen reader by its colour rather than its hex.
 */
object ColorPicker {
    private const val SWATCH_DP = 28f
    private const val CHOICE_DP = 30f
    private const val RING_DP = 2f

    /** The ring's gap plus its own width: how far it stands out from the swatch on every side. */
    private const val RING_OUT_DP = 4f
    private const val HEX_GAP_DP = 10f
    private const val RGB = 0xFFFFFF

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
        onChange: (Int) -> Unit,
    ): View {
        val swatch =
            View(ui.context).apply {
                background = circle(ui, initial)
                contentDescription = label
            }
        val hex = ui.mono(hexOf(initial), Type.VALUE, ui.palette.dim)
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
            (swatch.background as GradientDrawable).setColor(color)
            hex.text = hexOf(color)
            mark(color)
            onChange(color)
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
}
