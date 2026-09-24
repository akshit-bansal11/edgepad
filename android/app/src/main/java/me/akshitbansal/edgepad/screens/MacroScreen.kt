package me.akshitbansal.edgepad.screens

import android.graphics.BitmapFactory
import android.graphics.drawable.BitmapDrawable
import android.text.TextPaint
import android.text.TextUtils
import android.util.DisplayMetrics
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/** The smallest side a picture is drawn at: under this a cell is too small to be showing a picture at all. */
private const val ICON_MIN_DP = 16f

/** The largest picture accepted from the laptop, which sends 128px; the headroom is for an older or newer laptop. */
private const val MAX_ICON_PX = 256

/**
 * The label's size as a fraction of the cell's shorter side. It is held between [Type.LABEL], the size every
 * macro button used to be drawn at, and [LABEL_MAX_SP]: a grid of three has room for text the size of a
 * headline, and a headline on a button reads as a mistake rather than as a size.
 */
private const val LABEL_SCALE = 0.08f
private const val LABEL_MAX_SP = 18f

/**
 * What the system bars take off the screen's height. The real figure arrives with the window insets, long
 * after this screen is built, so this stands in for it: a status bar plus a gesture bar on a typical phone.
 */
private const val SYSTEM_BARS_DP = 56f

/**
 * The laptop's macro slots, as a grid of buttons. A tap hands back the slot's index and nothing else —
 * what it launches is the laptop's business, and MainActivity.runMacro says why that is worth keeping.
 *
 * The names arrive from the laptop, so the screen is built from whatever it last said; an empty list is
 * an empty state rather than an empty grid, because a grid of nothing looks broken and explains nothing.
 * The icons arrive the same way and later, since they are asked for rather than pushed: a grid drawn
 * before they land is the same grid with labels on it, which is also what a laptop too old to send them
 * leaves behind for good.
 *
 * However many slots there are, they are spread over the whole page: three macros are drawn as large as the
 * screen allows and fifteen as small as they have to be, because a grid of three buttons huddled at the top
 * of an empty page wastes the one thing a wall-mounted phone has plenty of.
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
        // Pictures and nothing else: the labels are turned off and every slot answered with one. That is the
        // one case where the outline goes and the cell belongs entirely to the picture. A slot whose picture
        // never arrived still has to fall back to its name, and a bare word with no border is not a button,
        // so one missing picture keeps the outlines — and the label row that goes with them — for the lot.
        val bare = !labels && pictures.values.all { it != null }
        val grid = plan(ui, slots.size, labels, bare)
        return ui.page(bar) {
            slots.chunked(grid.columns).forEachIndexed { at, line ->
                // The first row sits straight under the bar. A gap above it as well would be one gap more
                // than the height was worked out from, and the last row would fall off the bottom.
                add(row(ui, line, grid, pictures, onRun), if (at == 0) 0f else Space.S, grid.height)
            }
        }
    }

    /**
     * Everything the grid decided before it drew anything: how many buttons stand across a row, how tall
     * each one is, and how the label and the picture inside it are sized to match.
     */
    private class Grid(
        val columns: Int,
        val height: Int,
        val labelSp: Float,
        val line: Int,
        val icon: Int,
        val labels: Boolean,
        val bare: Boolean,
    )

    /**
     * Works the grid out from the room the page has and the number of slots in it.
     *
     * The arithmetic happens here, while the screen is being built, rather than in a layout pass. It can:
     * MainActivity rebuilds this screen from scratch whenever the laptop's list or its icons change, so the
     * count is known before a single view exists. The alternative, a layout that measures its own children,
     * would have to allocate inside onLayout, which this project's lint rejects outright.
     */
    private fun plan(
        ui: Ui,
        count: Int,
        labels: Boolean,
        bare: Boolean,
    ): Grid {
        val metrics = ui.context.resources.displayMetrics
        val gap = ui.dp(Space.S)
        val pad = 2 * ui.dp(Space.S)
        // Only the page's side margins come off the screen's width; the bar, the page's own bottom margin
        // and [SYSTEM_BARS_DP] come off its height. A display cutout in landscape, or a three-button
        // navigation bar, takes a little more than either allows for. Being wrong this way leaves a strip of
        // the page unused, which is the failure worth having: wrong the other way, the last row sits under
        // the edge of the screen and the page scrolls to reach a grid that was meant to fit.
        val across = metrics.widthPixels - 2 * ui.dp(Space.PAGE)
        val down = metrics.heightPixels - ui.dp(Space.BAR) - ui.dp(Space.XL) - ui.dp(SYSTEM_BARS_DP)
        // The column count that leaves the cell's shorter side longest, which is the largest a button can be
        // drawn without one of its sides being wasted on the other. Counted up to the number of slots rather
        // than to what fits: three macros are then three big buttons, not three small ones in a wide row.
        var columns = 1
        var width = 1
        var height = ui.dp(Space.TOUCH)
        for (candidate in 1..count) {
            val rows = (count + candidate - 1) / candidate
            val cellWidth = ((across - (candidate - 1) * gap) / candidate).coerceAtLeast(1)
            // Never under a touch target, however many slots there are: below that the page scrolls instead.
            val cellHeight = ((down - (rows - 1) * gap) / rows).coerceAtLeast(ui.dp(Space.TOUCH))
            if (minOf(cellWidth, cellHeight) > minOf(width, height)) {
                columns = candidate
                width = cellWidth
                height = cellHeight
            }
        }
        val labelSp = (minOf(width, height) / metrics.density * LABEL_SCALE).coerceIn(Type.LABEL, LABEL_MAX_SP)
        // One line of label, reserved for every button in the grid or for none of them, so that a row
        // holding one picture and one name does not stand at two heights.
        val line = if (bare) 0 else lineHeight(metrics, labelSp)
        // No ceiling: the picture takes whatever the cell has left, so a short list draws large pictures and
        // a full one draws small. There was a 64 dp cap while the laptop sent 48px squares, because past
        // that the blur was what the eye read rather than the icon. The laptop sends 128 from 3.0.1, which
        // is sharp at the sizes this grid produces, so the cap became the thing making a big button look
        // empty rather than the thing keeping it honest.
        val icon = minOf(width - pad, height - pad - line - ui.dp(Space.XS)).coerceAtLeast(ui.dp(ICON_MIN_DP))
        return Grid(columns, height, labelSp, line, icon, labels, bare)
    }

    /** How tall one line of label stands at [sp], measured in the face and size the button draws it in. */
    private fun lineHeight(
        metrics: DisplayMetrics,
        sp: Float,
    ): Int {
        val paint =
            TextPaint().apply {
                typeface = Type.face
                textSize = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, sp, metrics)
            }
        return paint.fontMetricsInt.run { descent - ascent }
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

    /**
     * One line of the grid, padded out to the full column count so a short last row keeps the others' width.
     * The padding is split between the two ends rather than heaped on the far one, so a last row of three
     * under rows of four sits under the middle of them instead of hanging off one side.
     */
    private fun row(
        ui: Ui,
        slots: List<IndexedValue<String>>,
        grid: Grid,
        pictures: Map<Int, BitmapDrawable?>,
        onRun: (index: Int) -> Unit,
    ): LinearLayout =
        LinearLayout(ui.context).apply {
            val lead = (grid.columns - slots.size) / 2
            for (column in 0 until grid.columns) {
                // getOrNull answers null to the negative index the leading empty cells ask it for.
                val slot = slots.getOrNull(column - lead)
                val cell =
                    if (slot == null) View(ui.context) else button(ui, slot, pictures[slot.index], grid, onRun)
                addView(
                    cell,
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f).apply {
                        if (column > 0) marginStart = ui.dp(Space.S)
                    },
                )
            }
        }

    private fun button(
        ui: Ui,
        slot: IndexedValue<String>,
        picture: BitmapDrawable?,
        grid: Grid,
        onRun: (index: Int) -> Unit,
    ): View {
        val shown = label(slot, picture, grid.labels)
        val side = ui.dp(Space.S)
        // A button with nothing to read reserves neither the gap under the picture nor the line under that,
        // whether it is alone in a grid of pictures or the one slot in a named grid whose picture arrived.
        val gap = if (shown.isEmpty()) 0 else ui.dp(Space.XS)
        val reserved = if (shown.isEmpty()) 0 else grid.line
        return ui.button(shown, Ui.Style.OUTLINED) { onRun(slot.index) }.apply {
            // Nothing but pictures on the page, so nothing needs a border to say where one button ends and
            // the next begins. The pressed and focused states are drawn in the foreground and so survive it.
            if (grid.bare) background = null
            setTextSize(TypedValue.COMPLEX_UNIT_SP, grid.labelSp)
            // A long name is cut rather than wrapped, so every button in the grid stays one row tall.
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
            // Spoken in the laptop's own spelling; the button's own label is shouted for the design.
            contentDescription = ui.string(R.string.macros_run, slot.value)
            // TextView draws a top compound drawable at its own top padding and centres the text in what is
            // left under it, so the two are only centred together when the padding above and below is the
            // room left over. In a cell this tall, leaving it even would pin the picture to the ceiling.
            val slack =
                if (picture == null) side else ((grid.height - grid.icon - gap - reserved) / 2).coerceAtLeast(0)
            setPadding(side, slack, side, slack)
            if (picture != null) {
                picture.setBounds(0, 0, grid.icon, grid.icon)
                setCompoundDrawables(null, picture, null, null)
                compoundDrawablePadding = gap
            }
        }
    }
}
