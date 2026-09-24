package me.akshitbansal.edgepad.screens

import android.graphics.BitmapFactory
import android.graphics.drawable.BitmapDrawable
import android.text.TextPaint
import android.text.TextUtils
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/** The picture's side, and the room a row gives it above the label. */
private const val ICON_DP = 28f

/** The largest picture accepted from the laptop, which sends 48px; the headroom is for an older or newer laptop. */
private const val MAX_ICON_PX = 256

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
        // A bar of nothing but the way out. A "Macros" heading over a grid of macros repeats what the grid
        // already says and costs it a row; an empty title draws nothing, and a TextView with neither text
        // nor description is skipped by TalkBack, so the chevron is all that is there to read or to tap.
        val bar = ui.bar("", onBack)
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
        val columns = columnCount(ui, slots, pictures, labels)
        return ui.page(bar) {
            for (line in slots.chunked(columns)) {
                add(row(ui, line, columns, pictures, tall, labels, onRun), Space.S)
            }
        }
    }

    /**
     * How many buttons fit across the page. Measured rather than fixed at three or five: every cell is as
     * wide as the grid's widest label, so no button is a different size from its neighbour, and a row takes
     * as many of them as the screen actually has room for.
     *
     * The measuring happens here, while the screen is being built, rather than in a layout pass. It can:
     * MainActivity rebuilds this screen from scratch whenever the laptop's list changes, so the labels are
     * all known before a single view exists. The alternative, a flow layout that measures its own children,
     * would have to allocate inside onLayout, which this project's lint rejects outright.
     */
    private fun columnCount(
        ui: Ui,
        slots: List<IndexedValue<String>>,
        pictures: Map<Int, BitmapDrawable?>,
        labels: Boolean,
    ): Int {
        // The same face, size and tracking Ui.button draws its label with, so the width is the real one.
        val paint =
            TextPaint().apply {
                typeface = Type.face
                letterSpacing = Type.TRACKING_BUTTON
                textSize =
                    TypedValue.applyDimension(
                        TypedValue.COMPLEX_UNIT_SP,
                        Type.LABEL,
                        ui.context.resources.displayMetrics,
                    )
            }
        val widest = slots.maxOf { paint.measureText(label(it, pictures[it.index], labels)) }
        // A cell is the widest label plus the button's own padding, and never narrower than a picture:
        // with the labels turned off there is no text to measure and the icon is the whole of the width.
        val cell = maxOf(widest.toInt(), ui.dp(ICON_DP)) + 2 * ui.dp(Space.S)
        val gap = ui.dp(Space.S)
        // Only the page's side margins come off the screen's width. A display cutout takes a few pixels
        // more in landscape, which at worst clips a character off the widest label rather than the row.
        val room = ui.context.resources.displayMetrics.widthPixels - 2 * ui.dp(Space.PAGE)
        // Capped by what fits and not by how many slots there are: a grid holding two macros keeps a row of
        // the full count and pads the rest, so those two are ordinary buttons sitting at the left. Capped at
        // the number of slots instead, two macros would each be drawn half a screen wide.
        return ((room + gap) / (cell + gap)).coerceAtLeast(1)
    }

    /**
     * What a button draws: its name, or nothing at all when it has a picture and the labels are turned off.
     * A button showing its picture only still answers to its name — the label is what is dropped, not what
     * the slot is called, so TalkBack reads the same thing either way.
     */
    private fun label(
        slot: IndexedValue<String>,
        picture: BitmapDrawable?,
        labels: Boolean,
    ): String = if (picture != null && !labels) "" else slot.value.uppercase()

    /** The bitmap for a slot, or null when there is none or the bytes are not a picture after all. */
    private fun decode(
        ui: Ui,
        png: ByteArray?,
    ): BitmapDrawable? {
        val bytes = png ?: return null
        // The header first: a few kilobytes of PNG can claim any size, and decoding a claimed 30000px square
        // would take gigabytes. The laptop never sends more than 48px, so anything past the cap is not an icon.
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
        if (bounds.outWidth !in 1..MAX_ICON_PX || bounds.outHeight !in 1..MAX_ICON_PX) return null
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
        // The same label columnCount measured, so the cell it sized is the cell this fills.
        val shown = label(slot, picture, labels)
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
