package me.akshitbansal.edgepad.screens

import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.LayerDrawable
import android.view.Gravity
import android.view.View
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import java.util.Locale

/**
 * A colour picked from a strip of round swatches, under a row with the hex of what is chosen and a swatch of
 * it. The chosen swatch wears an accent ring a little way out from its edge.
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
        intArrayOf(
            0xFFFFFFFF.toInt(),
            0xFFC8C8CC.toInt(),
            0xFF84848C.toInt(),
            0xFF3A3A40.toInt(),
            0xFF000000.toInt(),
            0xFFE53935.toInt(),
            0xFFFB8C00.toInt(),
            0xFFFDD835.toInt(),
            0xFF43A047.toInt(),
            0xFF00ACC1.toInt(),
            0xFF1E88E5.toInt(),
            0xFF3949AB.toInt(),
            0xFF8E24AA.toInt(),
            0xFFD81B60.toInt(),
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
        val rings = ArrayList<Pair<Int, GradientDrawable>>(choices.size)

        fun choose(color: Int) {
            (swatch.background as GradientDrawable).setColor(color)
            hex.text = hexOf(color)
            for ((c, ring) in rings) outline(ui, ring, chosen = c == color)
            onChange(color)
        }
        val strip =
            LinearLayout(ui.context).apply {
                for (color in choices) {
                    val ring = GradientDrawable().apply { shape = GradientDrawable.OVAL }
                    outline(ui, ring, chosen = color == initial)
                    rings.add(color to ring)
                    val choice =
                        View(ui.context).apply {
                            // The ring behind, the swatch inset from it by the ring and its gap. The gap is left
                            // clear, so it shows the card the picker sits on, as the design's does.
                            val out = ui.dp(RING_OUT_DP)
                            background =
                                LayerDrawable(arrayOf(ring, circle(ui, color))).apply {
                                    setLayerInset(1, out, out, out, out)
                                }
                            contentDescription = hexOf(color)
                            ui.tappable(this, CHOICE_DP / 2 + RING_OUT_DP) { choose(color) }
                        }
                    // Each swatch's box includes its ring's room, so two boxes side by side leave the design's
                    // 8 dp between the swatches themselves.
                    val size = ui.dp(CHOICE_DP + 2 * RING_OUT_DP)
                    addView(choice, LinearLayout.LayoutParams(size, size))
                }
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
