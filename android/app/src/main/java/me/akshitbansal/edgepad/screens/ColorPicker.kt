package me.akshitbansal.edgepad.screens

import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import java.util.Locale

/** A colour as three rulers, hue, saturation and value, beside a swatch that shows the result. */
object ColorPicker {
    private const val HUE_STEPS = 36
    private const val HUE_STEP = 10f
    private const val PERCENT = 100
    private const val PERCENT_STEPS = 20
    private const val PERCENT_STEP = 5f
    private const val SWATCH_DP = 40f

    fun build(
        ui: Ui,
        label: CharSequence,
        initial: Int,
        onChange: (Int) -> Unit,
    ): View {
        val hsv = FloatArray(3)
        Color.colorToHSV(initial, hsv)
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

        fun changed() {
            val color = Color.HSVToColor(hsv)
            (swatch.background as GradientDrawable).setColor(color)
            hex.text = hexOf(color)
            onChange(color)
        }
        return LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            val head =
                LinearLayout(ui.context).apply {
                    gravity = Gravity.CENTER_VERTICAL
                    minimumHeight = ui.dp(Space.ROW)
                    addView(
                        ui.text(label, Type.BODY, ui.palette.ink, Type.plain),
                        LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f),
                    )
                    addView(
                        hex,
                        LinearLayout
                            .LayoutParams(
                                LinearLayout.LayoutParams.WRAP_CONTENT,
                                LinearLayout.LayoutParams.WRAP_CONTENT,
                            ).apply {
                                marginEnd =
                                    ui.dp(Space.M)
                            },
                    )
                    addView(swatch, LinearLayout.LayoutParams(ui.dp(SWATCH_DP), ui.dp(SWATCH_DP)))
                }
            addView(head)
            addView(
                slider(ui, R.string.color_hue, HUE_STEPS, (hsv[0] / HUE_STEP).toInt()) {
                    hsv[0] = it * HUE_STEP
                    changed()
                },
            )
            addView(
                slider(ui, R.string.color_saturation, PERCENT_STEPS, (hsv[1] * PERCENT_STEPS).toInt()) {
                    hsv[1] = it * PERCENT_STEP / PERCENT
                    changed()
                },
            )
            addView(
                slider(ui, R.string.color_value, PERCENT_STEPS, (hsv[2] * PERCENT_STEPS).toInt()) {
                    hsv[2] = it * PERCENT_STEP / PERCENT
                    changed()
                },
            )
        }
    }

    private fun slider(
        ui: Ui,
        labelRes: Int,
        max: Int,
        value: Int,
        onChange: (Int) -> Unit,
    ): View =
        LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            addView(
                ui.mono(ui.string(labelRes), Type.MICRO, ui.palette.dim),
                LinearLayout.LayoutParams(ui.dp(LABEL_DP), LinearLayout.LayoutParams.WRAP_CONTENT),
            )
            addView(
                ui.ruler(max, value, onChange).apply {
                    contentDescription = ui.string(labelRes)
                },
                LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f),
            )
        }

    private const val LABEL_DP = 40f
    private const val RGB = 0xFFFFFF

    private fun hexOf(color: Int): String = "#%06X".format(Locale.ROOT, color and RGB)
}
