package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.surface.Strokes

/**
 * Capture to the phone, and the pen the Annotate toolbar draws with: its colour, width and opacity, the same stored
 * values the Capture chip and the toolbar's own controls edit. Each is read when Annotate opens, so a change here shows
 * there. [onToPhone] is told when "To phone" changes: the capture inbox is set up and the laptop hears it.
 */
object CaptureSettingsScreen {
    fun build(
        ui: Ui,
        settings: Settings,
        onToPhone: (Boolean) -> Unit,
        onBack: () -> Unit,
    ): View =
        ui.page(ui.bar("", onBack, backLabel = ui.string(R.string.controls_capture))) {
            largeTitle(ui.string(R.string.settings_capture))
            card {
                add(
                    ui.toggle(ui.string(R.string.capture_to_phone), settings.captureToPhone) {
                        settings.captureToPhone = it
                        onToPhone(it)
                    },
                )
            }
            card(Space.XL) {
                add(
                    ColorPicker.build(
                        ui,
                        ui.string(R.string.settings_pen_colour),
                        ColorPicker.OPAQUE or settings.penRgb,
                        editable = true,
                    ) { colour -> settings.penRgb = colour and ColorPicker.RGB },
                )
                hairline()
                add(
                    ui.slider(
                        ui.string(R.string.annotate_width),
                        Strokes.WIDTH_STEPS,
                        Strokes.widthStep(settings.penWidth),
                        { step -> ui.string(R.string.annotate_width_value, step + 1) },
                    ) { step -> settings.penWidth = Strokes.widthAt(step) },
                )
                hairline()
                add(
                    ui.slider(
                        ui.string(R.string.annotate_opacity),
                        Strokes.OPACITY_STEPS,
                        Strokes.opacityStep(settings.penOpacity),
                        { step -> ui.string(R.string.percent_value, Strokes.opacityAt(step)) },
                    ) { step -> settings.penOpacity = Strokes.opacityAt(step) },
                )
            }
        }
}
