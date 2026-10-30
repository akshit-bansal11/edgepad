package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space

/** What is set for the gamepad and nowhere else: which layout it wears, and the way into the layout editor. */
object GamepadSettingsScreen {
    fun build(
        ui: Ui,
        names: List<String>,
        current: String,
        onPick: (String) -> Unit,
        onEdit: () -> Unit,
        onSystem: () -> Unit,
        onBack: () -> Unit,
    ): View =
        ui.page(ui.bar("", onBack, backLabel = ui.string(R.string.controls_gamepad))) {
            largeTitle(ui.string(R.string.gamepad_settings_title))
            section(ui.string(R.string.gamepad_layout_section))
            card {
                add(ui.choices(names, names.indexOf(current)) { i -> onPick(names[i]) })
            }
            card(Space.M) {
                add(ui.link(ui.string(R.string.gamepad_edit), null, onEdit))
            }
            systemSettings(onSystem)
        }
}
