package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings

/**
 * How the macro grid draws its buttons. One row today, and it earns a page of its own for the same reason
 * the keyboard's text size does: it belongs to the macros, not to the surface's background.
 */
object MacroSettingsScreen {
    /** The Settings hub's one-line summary: which of the two appearances is set. */
    fun summary(
        ui: Ui,
        settings: Settings,
    ): String =
        ui.string(
            if (settings.macroLabels) R.string.macro_buttons_label else R.string.macro_buttons_icon,
        )

    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View =
        ui.page(ui.bar(ui.string(R.string.macro_buttons), onBack)) {
            add(buttons(ui, settings))
            hairline()
        }

    /**
     * Whether a macro button carries its name under its picture. Only ever half a choice: a slot the laptop
     * sent no picture for shows its name whichever way this is set, because the alternative is a button with
     * nothing on it at all.
     */
    private fun buttons(
        ui: Ui,
        settings: Settings,
    ): View {
        val styles = listOf(ui.string(R.string.macro_buttons_label), ui.string(R.string.macro_buttons_icon))
        val pick =
            ui.segmented(styles, if (settings.macroLabels) 0 else 1) { i ->
                settings.macroLabels = i == 0
            }
        return ui.field(ui.string(R.string.macro_buttons), pick)
    }
}
