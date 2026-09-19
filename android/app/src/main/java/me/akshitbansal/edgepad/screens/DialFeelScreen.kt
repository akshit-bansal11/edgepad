package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.surface.DialKind
import kotlin.math.roundToInt

/**
 * How the corner dials respond and how big they are, with a live corner beside the sliders.
 *
 * Sensitivity comes in two layers. The shared slider is the one most people ever touch; under the preview,
 * each dial kind may take a speed of its own, because they do not want the same one — volume runs 0-100
 * over a thumb's width and wants a slow ruler, while the app switcher is a stepper that wants a fast one.
 * A per-dial slider at its lowest step reads SHARED and removes the override rather than storing a second
 * default, so changing the shared slider later still moves every dial that never asked to differ.
 */
object DialFeelScreen {
    private const val PREVIEW_DP = 220f

    /** Step 0 of a per-dial slider: follow the shared value instead of holding one. */
    private const val SHARED_STEP = 0

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
                    section(ui.string(R.string.dial_feel_each))
                    for (kind in DialKind.entries) {
                        add(perDial(ui, settings, kind))
                        hairline()
                    }
                    mono(ui.string(R.string.dial_feel_each_caption), topDp = Space.S)
                },
            )
        }
    }

    /** One dial kind's own sensitivity, with SHARED as the step below the range's bottom. */
    private fun perDial(
        ui: Ui,
        settings: Settings,
        kind: DialKind,
    ): View {
        val start =
            if (settings.hasOwnSensitivity(kind)) {
                sensitivityRange.stepOf(settings.sensitivityOf(kind)) + 1
            } else {
                SHARED_STEP
            }
        return ui.slider(
            ui.string(kind.nameRes),
            sensitivityRange.steps + 1,
            start,
            { step ->
                if (step == SHARED_STEP) {
                    ui.string(R.string.dial_feel_shared)
                } else {
                    ui.string(R.string.multiplier_value, sensitivityRange.valueAt(step - 1))
                }
            },
        ) { step ->
            settings.setSensitivityOf(kind, if (step == SHARED_STEP) null else sensitivityRange.valueAt(step - 1))
        }
    }
}
