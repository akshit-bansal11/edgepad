package me.akshitbansal.edgepad.screens

import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings

/**
 * The macro screen's own settings: how many macros one page shows, how that many are laid out and whether the
 * labels are drawn. All are phone-side drawing choices. The grid of a page is worked out from the first, so
 * every page has the same tiles; the macros past it continue on further pages, which the macro screen pages
 * through sideways.
 */
object MacroSettingsScreen {
    /** Chips per line: seven at most (Auto and six grids), which do not fit one line at a readable size. */
    private const val CHIPS_PER_LINE = 4

    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View {
        val min = Settings.MIN_MACROS_PER_PAGE
        val layout = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        fillLayout(ui, settings, layout)
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
                    ) { step ->
                        settings.macrosPerPage = step + min
                        fillLayout(ui, settings, layout)
                    },
                )
            }
            footnote(ui.string(R.string.macros_per_page_caption))
            section(ui.string(R.string.macros_layout))
            card { add(layout) }
            footnote(ui.string(R.string.macros_layout_caption))
            card {
                add(ui.toggle(ui.string(R.string.macros_labels), settings.macroLabels) { settings.macroLabels = it })
            }
            footnote(ui.string(R.string.macros_labels_caption))
        }
    }

    /**
     * Fills [layout] with Auto and a chip for each grid the current page size offers, wrapped over lines. The
     * chosen row count is kept only while it is still offered, else Auto shows as chosen; the stored value is
     * left alone, so sliding back to a size that offers it again brings the grid back.
     */
    private fun fillLayout(
        ui: Ui,
        settings: Settings,
        layout: LinearLayout,
    ) {
        layout.removeAllViews()
        val choices = DeckLayout.choices(settings.macrosPerPage)
        val chosen = settings.macroRows.takeIf { rows -> choices.any { it.first == rows } } ?: Settings.AUTO_MACRO_ROWS
        val auto = Triple<Int, String, String?>(Settings.AUTO_MACRO_ROWS, ui.string(R.string.macros_layout_auto), null)
        val options =
            listOf(auto) +
                choices.map { (rows, columns) ->
                    Triple(
                        rows,
                        ui.string(R.string.macros_layout_grid, rows, columns),
                        ui.string(R.string.macros_layout_grid_description, rows, columns),
                    )
                }
        options.chunked(CHIPS_PER_LINE).forEach { line ->
            val row = LinearLayout(ui.context).apply { orientation = LinearLayout.HORIZONTAL }
            for ((rows, label, description) in line) {
                val chip =
                    ui.chip(label) {
                        settings.macroRows = rows
                        fillLayout(ui, settings, layout)
                    }
                chip.contentDescription = description ?: label
                chip.isSelected = rows == chosen
                if (rows == chosen) chip.setTextColor(ui.palette.accent)
                row.addView(chip, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            }
            // Short last line: spacers keep its chips the width of the others instead of stretching them.
            repeat(CHIPS_PER_LINE - line.size) {
                row.addView(View(ui.context), LinearLayout.LayoutParams(0, 0, 1f))
            }
            layout.addView(row)
        }
    }
}
