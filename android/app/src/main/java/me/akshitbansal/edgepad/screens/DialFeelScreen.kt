package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import kotlin.math.roundToInt

/** How the corner dials respond and how big they are, with a live corner beside the sliders. */
object DialFeelScreen {
    private const val PREVIEW_DP = 220f

    private val sensitivityRange = StepRange(Settings.MIN_SENSITIVITY, Settings.MAX_SENSITIVITY, 0.1f)
    private val lengthRange = StepRange(Settings.MIN_DIAL_LENGTH, Settings.MAX_DIAL_LENGTH, 10f)
    private val heightRange = StepRange(Settings.MIN_DIAL_HEIGHT, Settings.MAX_DIAL_HEIGHT, 0.1f)

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
                            sensitivityRange.steps,
                            sensitivityRange.stepOf(settings.sensitivity),
                            { step -> ui.string(R.string.multiplier_value, sensitivityRange.valueAt(step)) },
                        ) { step -> settings.sensitivity = sensitivityRange.valueAt(step) },
                    )
                    hairline()
                    add(
                        ui.slider(
                            ui.string(R.string.dial_length),
                            lengthRange.steps,
                            lengthRange.stepOf(settings.dialLength),
                            { step -> ui.string(R.string.dial_length_value, lengthRange.valueAt(step).roundToInt()) },
                        ) { step ->
                            settings.dialLength = lengthRange.valueAt(step)
                            preview.show(settings.dialLength, settings.dialHeight)
                        },
                    )
                    hairline()
                    add(
                        ui.slider(
                            ui.string(R.string.dial_height),
                            heightRange.steps,
                            heightRange.stepOf(settings.dialHeight),
                            { step -> ui.string(R.string.multiplier_value, heightRange.valueAt(step)) },
                        ) { step ->
                            settings.dialHeight = heightRange.valueAt(step)
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
}
