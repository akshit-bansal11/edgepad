package me.akshitbansal.edgepad.screens

import android.graphics.drawable.GradientDrawable
import android.view.Gravity
import android.view.View
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import java.util.Locale

/** A colour picked from a strip of swatches, beside a larger swatch and the hex of what is chosen. */
object ColorPicker {
    private const val SWATCH_DP = 40f
    private const val CHOICE_DP = 32f
    private const val RING_DP = 2f
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
                background =
                    GradientDrawable().apply {
                        setColor(initial)
                        setStroke(ui.dp(Space.HAIR), ui.palette.dim)
                    }
                contentDescription = label
            }
        val hex = ui.mono(hexOf(initial), Type.CAPTION, ui.palette.ink, 0f)
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
                    val ring = GradientDrawable().apply { setColor(color) }
                    outline(ui, ring, chosen = color == initial)
                    rings.add(color to ring)
                    val choice =
                        View(ui.context).apply {
                            background = ring
                            contentDescription = hexOf(color)
                            ui.tappable(this) { choose(color) }
                        }
                    val size = ui.dp(CHOICE_DP)
                    addView(choice, LinearLayout.LayoutParams(size, size).apply { marginEnd = ui.dp(Space.S) })
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
                            ).apply { marginEnd = ui.dp(Space.M) },
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

    /** The chosen swatch wears an ink ring; the rest a hairline. */
    private fun outline(
        ui: Ui,
        ring: GradientDrawable,
        chosen: Boolean,
    ) {
        val width = if (chosen) RING_DP else Space.HAIR
        val color = if (chosen) ui.palette.ink else ui.palette.dim
        ring.setStroke(ui.dp(width), color)
    }

    private fun hexOf(color: Int): String = "#%06X".format(Locale.ROOT, color and RGB)
}
