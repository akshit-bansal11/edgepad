package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings

/**
 * Everything about the on-screen keyboard, which until 3.0 was one slider stranded on a page titled
 * "Background & pattern" — a title that described neither it nor the macro row beside it. A keyboard the
 * user can resize is a keyboard setting, so it lives under the keyboard's own name and nowhere else.
 */
object KeyboardSettingsScreen {
    private val textRange = StepRange(Settings.MIN_KEY_TEXT_SCALE, Settings.MAX_KEY_TEXT_SCALE, 0.1f)

    /** The Settings hub's one-line summary: the text size, in the same ×n the slider reads out. */
    fun summary(
        ui: Ui,
        settings: Settings,
    ): String = ui.string(R.string.multiplier_value, settings.keyTextScale)

    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View =
        ui.page(ui.bar(ui.string(R.string.keyboard_settings_title), onBack)) {
            add(textSize(ui, settings))
            hairline()
        }

    /** How large the keyboard's key labels are drawn; the keyboard shrinks the lot if any would leave its key. */
    private fun textSize(
        ui: Ui,
        settings: Settings,
    ): View =
        ui.slider(
            ui.string(R.string.keyboard_text_size),
            textRange.steps,
            textRange.stepOf(settings.keyTextScale),
            { step -> ui.string(R.string.multiplier_value, textRange.valueAt(step)) },
        ) { step ->
            settings.keyTextScale = textRange.valueAt(step)
        }
}
