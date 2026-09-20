package me.akshitbansal.edgepad.screens

import android.graphics.BitmapFactory
import android.graphics.drawable.BitmapDrawable
import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

private const val PORTRAIT_COLUMNS = 3
private const val LANDSCAPE_COLUMNS = 5

/** The picture's side, and the room a row gives it above the label. */
private const val ICON_DP = 28f

/**
 * The laptop's macro slots, as a grid of buttons. A tap hands back the slot's index and nothing else —
 * what it launches is the laptop's business, and MainActivity.runMacro says why that is worth keeping.
 *
 * The names arrive from the laptop, so the screen is built from whatever it last said; an empty list is
 * an empty state rather than an empty grid, because a grid of nothing looks broken and explains nothing.
 * The icons arrive the same way and later, since they are asked for rather than pushed: a grid drawn
 * before they land is the same grid with labels on it, which is also what a laptop too old to send them
 * leaves behind for good.
 */
object MacroScreen {
    fun build(
        ui: Ui,
        names: List<String>,
        icon: (Int) -> ByteArray?,
        labels: Boolean,
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
        val pictures = slots.associate { it.index to decode(ui, icon(it.index)) }
        // One height for the whole grid, decided by whether anything in it has a picture. Sized per button
        // instead, a row holding one icon and one label would stand at two heights and read as a mistake.
        val tall = pictures.values.any { it != null }
        val columns = if (ui.landscape) LANDSCAPE_COLUMNS else PORTRAIT_COLUMNS
        return ui.page(bar) {
            section(ui.string(R.string.macros_count, slots.size))
            for (line in slots.chunked(columns)) {
                add(row(ui, line, columns, pictures, tall, labels, onRun), Space.S)
            }
        }
    }

    /** The bitmap for a slot, or null when there is none or the bytes are not a picture after all. */
    private fun decode(
        ui: Ui,
        png: ByteArray?,
    ): BitmapDrawable? {
        val bytes = png ?: return null
        val bitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.size) ?: return null
        return BitmapDrawable(ui.context.resources, bitmap)
    }

    /** One line of the grid, padded to [columns] so a short last row keeps the others' width. */
    private fun row(
        ui: Ui,
        slots: List<IndexedValue<String>>,
        columns: Int,
        pictures: Map<Int, BitmapDrawable?>,
        tall: Boolean,
        labels: Boolean,
        onRun: (index: Int) -> Unit,
    ): LinearLayout =
        LinearLayout(ui.context).apply {
            for (column in 0 until columns) {
                val slot = slots.getOrNull(column)
                val cell =
                    if (slot == null) {
                        View(ui.context)
                    } else {
                        button(ui, slot, pictures[slot.index], tall, labels, onRun)
                    }
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
        picture: BitmapDrawable?,
        tall: Boolean,
        labels: Boolean,
        onRun: (index: Int) -> Unit,
    ): View {
        // A button showing its picture only still answers to its name: the label is what is dropped, not
        // what the slot is called, so TalkBack reads the same thing either way.
        val shown = if (picture != null && !labels) "" else slot.value.uppercase()
        return ui.button(shown, Ui.Style.OUTLINED) { onRun(slot.index) }.apply {
            // A long name is cut rather than wrapped, so every button in the grid stays one row tall.
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
            setPadding(ui.dp(Space.S), ui.dp(Space.S), ui.dp(Space.S), ui.dp(Space.S))
            // Spoken in the laptop's own spelling; the button's own label is shouted for the design.
            contentDescription = ui.string(R.string.macros_run, slot.value)
            if (tall) minHeight = ui.dp(Space.BUTTON + ICON_DP)
            if (picture != null) draw(ui, this, picture, labels)
        }
    }

    /** Puts the picture above the label, at a fixed size rather than whatever the laptop happened to send. */
    private fun draw(
        ui: Ui,
        button: TextView,
        picture: BitmapDrawable,
        labels: Boolean,
    ) {
        val side = ui.dp(ICON_DP)
        picture.setBounds(0, 0, side, side)
        button.setCompoundDrawables(null, picture, null, null)
        button.compoundDrawablePadding = if (labels) ui.dp(Space.XS) else 0
    }
}
