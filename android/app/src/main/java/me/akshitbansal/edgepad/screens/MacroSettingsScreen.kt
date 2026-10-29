package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings

/**
 * The macro screen's own settings: how many macros one page shows and whether the labels are drawn. Both are
 * phone-side drawing choices. The grid of a page is worked out from the first, so every page has the same
 * tiles; the macros past it continue on further pages, which the macro screen pages through sideways.
 */
object MacroSettingsScreen {
    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View {
        val min = Settings.MIN_MACROS_PER_PAGE
        return ui.page(
            ui.bar(ui.string(R.string.macros_settings), onBack, backLabel = ui.string(R.string.macros_title)),
        ) {
            section(ui.string(R.string.macros_pages_title))
            card {
                add(
                    ui.slider(
                        ui.string(R.string.macros_per_page),
                        Settings.MAX_MACROS_PER_PAGE - min,
                        settings.macrosPerPage - min,
                        { step -> (step + min).toString() },
                    ) { step -> settings.macrosPerPage = step + min },
                )
            }
            footnote(ui.string(R.string.macros_per_page_caption))
            card {
                add(ui.toggle(ui.string(R.string.macros_labels), settings.macroLabels) { settings.macroLabels = it })
            }
            footnote(ui.string(R.string.macros_labels_caption))
        }
    }
}
