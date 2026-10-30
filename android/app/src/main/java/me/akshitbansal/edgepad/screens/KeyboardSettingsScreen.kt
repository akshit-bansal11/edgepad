package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings

/** What is set for the keyboard and nowhere else: how large its key labels are drawn. */
object KeyboardSettingsScreen {
    private val textRange = StepRange(Settings.MIN_KEY_TEXT_SCALE, Settings.MAX_KEY_TEXT_SCALE, 0.1f)

    fun build(
        ui: Ui,
        settings: Settings,
        onSystem: () -> Unit,
        onBack: () -> Unit,
    ): View =
        ui.page(ui.bar("", onBack, backLabel = ui.string(R.string.keyboard_title))) {
            largeTitle(ui.string(R.string.keyboard_settings_title))
            card {
                // The keyboard shrinks the lot if any label would leave its key.
                add(
                    ui.slider(
                        ui.string(R.string.keyboard_text_size),
                        textRange.steps,
                        textRange.stepOf(settings.keyTextScale),
                        { step -> ui.string(R.string.multiplier_value, textRange.valueAt(step)) },
                    ) { step -> settings.keyTextScale = textRange.valueAt(step) },
                )
            }
            systemSettings(onSystem)
        }
}
