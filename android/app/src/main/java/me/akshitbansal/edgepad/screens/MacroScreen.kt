package me.akshitbansal.edgepad.screens

import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

private const val PORTRAIT_COLUMNS = 2
private const val LANDSCAPE_COLUMNS = 4

/**
 * The laptop's macro slots, as a grid of buttons. A tap hands back the slot's index and nothing else —
 * what it launches is the laptop's business, and MainActivity.runMacro says why that is worth keeping.
 *
 * The names arrive from the laptop, so the screen is built from whatever it last said; an empty list is
 * an empty state rather than an empty grid, because a grid of nothing looks broken and explains nothing.
 */
object MacroScreen {
    fun build(
        ui: Ui,
        names: List<String>,
        onRun: (index: Int) -> Unit,
        onBack: () -> Unit,
    ): View {
        val bar = ui.bar(ui.string(R.string.macros_title), onBack)
        // A slot with no name is a hole in the laptop's list, not the end of it: it keeps the index its
        // neighbours are counted from and simply is not drawn.
        val slots = names.withIndex().filter { it.value.isNotEmpty() }
        if (slots.isEmpty()) {
            return ui.page(bar, centred = true) {
                grow()
                headline(ui.string(R.string.macros_empty_title), Type.TITLE).gravity = Gravity.CENTER
                body(ui.string(R.string.macros_empty_body), Space.L).gravity = Gravity.CENTER
                grow()
            }
        }
        val columns = if (ui.landscape) LANDSCAPE_COLUMNS else PORTRAIT_COLUMNS
        return ui.page(bar) {
            section(ui.string(R.string.macros_count, slots.size))
            for (line in slots.chunked(columns)) add(row(ui, line, columns, onRun), Space.S)
        }
    }

    /** One line of the grid, padded to [columns] so a short last row keeps the others' width. */
    private fun row(
        ui: Ui,
        slots: List<IndexedValue<String>>,
        columns: Int,
        onRun: (index: Int) -> Unit,
    ): LinearLayout =
        LinearLayout(ui.context).apply {
            for (column in 0 until columns) {
                val slot = slots.getOrNull(column)
                val cell = if (slot == null) View(ui.context) else button(ui, slot, onRun)
                addView(
                    cell,
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                        if (column > 0) marginStart = ui.dp(Space.S)
                    },
                )
            }
        }

    private fun button(
        ui: Ui,
        slot: IndexedValue<String>,
        onRun: (index: Int) -> Unit,
    ): View =
        ui.button(slot.value.uppercase(), Ui.Style.OUTLINED) { onRun(slot.index) }.apply {
            // A long name is cut rather than wrapped, so every button in the grid stays one row tall.
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
            setPadding(ui.dp(Space.S), 0, ui.dp(Space.S), 0)
            // Spoken in the laptop's own spelling; the button's own label is shouted for the design.
            contentDescription = ui.string(R.string.macros_run, slot.value)
        }
}
