package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import kotlin.math.roundToInt

/** How the corner dials respond and how big they are, with a live corner beside the sliders. */
object DialFeelScreen {
    private const val SENSITIVITY_STEP = 0.1f
    private const val LENGTH_STEP = 10f
    private const val HEIGHT_STEP = 0.1f
    private const val PREVIEW_DP = 220f

    private val sensitivitySteps =
        ((Settings.MAX_SENSITIVITY - Settings.MIN_SENSITIVITY) / SENSITIVITY_STEP)
            .roundToInt()
    private val lengthSteps = ((Settings.MAX_DIAL_LENGTH - Settings.MIN_DIAL_LENGTH) / LENGTH_STEP).roundToInt()
    private val heightSteps = ((Settings.MAX_DIAL_HEIGHT - Settings.MIN_DIAL_HEIGHT) / HEIGHT_STEP).roundToInt()

    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View {
        val preview = DialPreview(ui.context, settings)
        return ui.page(ui.bar(ui.string(R.string.dial_feel_title), onBack)) {
            columns(
                {
                    add(
                        ui.slider(
                            ui.string(R.string.slide_sensitivity),
                            sensitivitySteps,
                            ((settings.sensitivity - Settings.MIN_SENSITIVITY) / SENSITIVITY_STEP).roundToInt(),
                            { step -> ui.string(R.string.multiplier_value, sensitivity(step)) },
                        ) { step -> settings.sensitivity = sensitivity(step) },
                    )
                    hairline()
                    add(
                        ui.slider(
                            ui.string(R.string.dial_length),
                            lengthSteps,
                            ((settings.dialLength - Settings.MIN_DIAL_LENGTH) / LENGTH_STEP).roundToInt(),
                            { step -> ui.string(R.string.dial_length_value, length(step).roundToInt()) },
                        ) { step ->
                            settings.dialLength = length(step)
                            preview.show(settings.dialLength, settings.dialHeight)
                        },
                    )
                    hairline()
                    add(
                        ui.slider(
                            ui.string(R.string.dial_height),
                            heightSteps,
                            ((settings.dialHeight - Settings.MIN_DIAL_HEIGHT) / HEIGHT_STEP).roundToInt(),
                            { step -> ui.string(R.string.multiplier_value, height(step)) },
                        ) { step ->
                            settings.dialHeight = height(step)
                            preview.show(settings.dialLength, settings.dialHeight)
                        },
                    )
                    hairline()
                    add(ui.toggle(ui.string(R.string.haptic_ticks), settings.haptics) { settings.haptics = it })
                    hairline()
                    add(ui.toggle(ui.string(R.string.snap_round), settings.snap) { settings.snap = it })
                    hairline()
                },
                {
                    add(preview, Space.L, ui.dp(PREVIEW_DP))
                    mono(ui.string(R.string.dial_preview_caption), topDp = Space.S)
                },
            )
        }
    }

    private fun sensitivity(step: Int): Float = Settings.MIN_SENSITIVITY + step * SENSITIVITY_STEP

    private fun length(step: Int): Float = Settings.MIN_DIAL_LENGTH + step * LENGTH_STEP

    private fun height(step: Int): Float = Settings.MIN_DIAL_HEIGHT + step * HEIGHT_STEP
}
