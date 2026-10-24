package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings

/**
 * The macro screen's own settings: how many columns the grid has and whether the labels are drawn. Both are
 * phone-side drawing choices. The grid is offered as columns, not as rows × columns, because this page does
 * not know how many macros the laptop has; the rows follow from the count, and the macro screen falls back to
 * Automatic for a column count its room cannot draw.
 */
object MacroSettingsScreen {
    /** The grids offered: the macro screen holds at most fifteen macros, which is five across and three down. */
    private const val MAX_COLUMNS = 5

    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View {
        val names =
            listOf(ui.string(R.string.macros_grid_auto)) +
                (1..MAX_COLUMNS).map { ui.string(R.string.macros_grid_choice, it) }
        // A stored count past the choices reads as Automatic, which is what the macro screen draws for it too.
        val chosen = settings.deckColumns(MACRO_DECK).takeIf { it in 1..MAX_COLUMNS } ?: 0
        return ui.page(
            ui.bar(ui.string(R.string.macros_settings), onBack, backLabel = ui.string(R.string.macros_title)),
        ) {
            section(ui.string(R.string.macros_grid_title))
            card {
                add(ui.choices(names, chosen) { pick -> settings.setDeckColumns(MACRO_DECK, pick) })
            }
            footnote(ui.string(R.string.macros_grid_caption))
            card {
                add(ui.toggle(ui.string(R.string.macros_labels), settings.macroLabels) { settings.macroLabels = it })
            }
            footnote(ui.string(R.string.macros_labels_caption))
        }
    }
}
