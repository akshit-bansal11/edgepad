package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space

/** What is set for the keyboard and nowhere else: which board, what its top row does, how large its labels are. */
object KeyboardSettingsScreen {
    private val textRange = StepRange(Settings.MIN_KEY_TEXT_SCALE, Settings.MAX_KEY_TEXT_SCALE, 0.1f)

    fun build(
        ui: Ui,
        settings: Settings,
        onSystem: () -> Unit,
        onBack: () -> Unit,
    ): View =
        ui.page(ui.bar("", onBack, backLabel = ui.string(R.string.keyboard_title))) {
            val sizes = KeyboardSize.entries
            val modes = FnMode.entries
            largeTitle(ui.string(R.string.keyboard_settings_title))
            section(ui.string(R.string.keyboard_size))
            card {
                add(
                    ui.choices(
                        sizes.map { ui.string(R.string.percent_value, it.percent) },
                        sizes.indexOf(settings.keyboardSize),
                    ) { i -> settings.keyboardSize = sizes[i] },
                )
            }
            footnote(ui.string(R.string.keyboard_size_caption))
            card(Space.XL) {
                add(
                    ui.field(
                        ui.string(R.string.keyboard_function_keys),
                        ui.segmented(
                            listOf(ui.string(R.string.keyboard_with_fn), ui.string(R.string.keyboard_direct)),
                            modes.indexOf(settings.functionKeys),
                        ) { i -> settings.functionKeys = modes[i] },
                    ),
                )
            }
            footnote(ui.string(R.string.keyboard_function_keys_caption))
            card(Space.XL) {
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
