package me.akshitbansal.edgepad.screens

import android.content.res.ColorStateList
import android.graphics.BitmapFactory
import android.graphics.drawable.BitmapDrawable
import android.text.TextPaint
import android.text.TextUtils
import android.util.DisplayMetrics
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
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
 * headline, and a headline on a button reads as a mistake rather than as a size. The ceiling is a bound on a
 * computed size rather than a size of its own, so it sits between two steps of the type scale on purpose.
 */
private const val LABEL_SCALE = 0.08f
private const val LABEL_MAX_SP = 18f

// The empty state: a tile with the macro glyph in it, over a heavy title.
private const val EMPTY_TILE_DP = 64f
private const val EMPTY_ICON_DP = 30f

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
 *
 * What the laptop sends later is handed in rather than rebuilding the screen: [setMacros] redraws the grid
 * inside the page, and [iconArrived] decodes one picture and dresses one button. Until 3.2 each of them
 * built the whole screen again, which threw away where TalkBack was and decoded every picture in the grid
 * once for each picture that arrived.
 */
class MacroScreen(
    private val ui: Ui,
    names: List<String>,
    private val icon: (Int) -> ByteArray?,
    private val labels: Boolean,
    private val onRun: (index: Int) -> Unit,
    onBack: () -> Unit,
) {
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
        /** No label row anywhere: the labels are off and every slot has its picture. */
        val bare: Boolean,
    )

    private var slots: List<IndexedValue<String>> = emptyList()
    private val pictures = HashMap<Int, BitmapDrawable?>()
    private val buttons = HashMap<Int, TextView>()
    private var grid: Grid? = null

    /** The room the grid has, across and down, once the window has said how much of it the bars take. */
    private var room: Pair<Int, Int>? = null

    // The nav bar every sub-screen has, back to the surface with the name centred. It costs the grid no
    // row: the title sits in the bar's own height, which [measureRoom] takes off the page.
    private val bar = ui.bar(ui.string(R.string.macros_title), onBack, backLabel = ui.string(R.string.back_to_surface))
    private lateinit var holder: LinearLayout

    val view: View =
        ui.page(bar) {
            // Takes the page's whole height, so the empty state can centre itself in it.
            holder =
                add(LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }, height = 0).apply {
                    (layoutParams as LinearLayout.LayoutParams).weight = 1f
                }
        }

    init {
        // The grid is sized from the room the page really has, which is known only once the window's insets
        // have reached it. It was worked out from the display's size less a guess at the system bars until
        // 3.2, which was wrong wherever the guess was: a three-button bar, a cutout, a split screen.
        view.addOnLayoutChangeListener { _, _, _, _, _, _, _, _, _ ->
            val measured = measureRoom()
            if (measured != null && measured != room) {
                room = measured
                // Not inside the layout pass that reported it: views added there would wait a frame anyway.
                view.post { render() }
            }
        }
        setMacros(names)
    }

    /** A new list from the laptop. Its icons are gone with it, since a slot number now means another macro. */
    fun setMacros(names: List<String>) {
        // A slot with no name is a hole in the laptop's list, not the end of it: it keeps the index its
        // neighbours are counted from and simply is not drawn.
        slots = names.withIndex().filter { it.value.isNotEmpty() }
        pictures.clear()
        for (slot in slots) pictures[slot.index] = decode(icon(slot.index))
        render()
    }

    /** The laptop finished sending [slot]'s picture: that one is decoded and that one button dressed in it. */
    fun iconArrived(slot: Int) {
        if (slots.none { it.index == slot }) return
        pictures[slot] = decode(icon(slot))
        val current = grid ?: return
        // The last picture arriving with the labels off takes the label row away from every button, and
        // that is a new grid rather than one new picture.
        if (bare() != current.bare) {
            render()
            return
        }
        val button = buttons[slot] ?: return
        dress(button, slots.first { it.index == slot }, current)
    }

    /**
     * Pictures and nothing else: the labels are turned off and every slot answered with one. That is the
     * one case where no row is kept for a label and the card belongs entirely to the picture. A slot whose
     * picture never arrived still has to fall back to its name, so one missing picture keeps the label row
     * for the lot.
     */
    private fun bare(): Boolean = !labels && slots.all { pictures[it.index] != null }

    private fun render() {
        holder.removeAllViews()
        buttons.clear()
        grid = null
        if (slots.isEmpty()) {
            holder.gravity = Gravity.CENTER
            Column(ui, holder).apply {
                add(emptyTile(), width = ui.dp(EMPTY_TILE_DP), height = ui.dp(EMPTY_TILE_DP))
                headline(ui.string(R.string.macros_empty_title), Type.TITLE, Space.L).gravity = Gravity.CENTER
                body(ui.string(R.string.macros_empty_body), Space.S).gravity = Gravity.CENTER
            }
            return
        }
        holder.gravity = Gravity.NO_GRAVITY
        val (across, down) = room ?: return
        val planned = plan(across, down, bare())
        grid = planned
        slots.chunked(planned.columns).forEachIndexed { at, line ->
            holder.addView(
                row(line, planned),
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, planned.height).apply {
                    // The first row sits straight under the bar. A gap above it as well would be one gap more
                    // than the height was worked out from, and the last row would fall off the bottom.
                    if (at > 0) topMargin = ui.dp(Space.S)
                },
            )
        }
    }

    /**
     * The page's room for the grid: the window less the system bars and cutout the page pads itself clear
     * of, less the nav bar and the page's side and bottom margins. Null until the window has said.
     */
    private fun measureRoom(): Pair<Int, Int>? {
        val insets = view.rootWindowInsets ?: return null
        val bars = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
        val across = view.width - bars.left - bars.right - 2 * ui.dp(Space.PAGE)
        val down = view.height - bars.top - bars.bottom - bar.height - ui.dp(Space.XL)
        return if (across > 0 && down > 0) across to down else null
    }

    /**
     * Works the grid out from the room the page has and the number of slots in it.
     *
     * The arithmetic happens here, once the room is known, rather than in a layout that measures its own
     * children, which would have to allocate inside onLayout, and this project's lint rejects that outright.
     */
    private fun plan(
        across: Int,
        down: Int,
        bare: Boolean,
    ): Grid {
        val metrics = ui.context.resources.displayMetrics
        val count = slots.size
        val gap = ui.dp(Space.S)
        val pad = 2 * ui.dp(Space.S)
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
        return Grid(columns, height, labelSp, line, icon, bare)
    }

    /** How tall one line of label stands at [sp], measured in the face and size the button draws it in. */
    private fun lineHeight(
        metrics: DisplayMetrics,
        sp: Float,
    ): Int {
        val paint =
            TextPaint().apply {
                typeface = Type.bold
                textSize = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, sp, metrics)
            }
        return paint.fontMetricsInt.run { descent - ascent }
    }

    /** The bitmap for a slot, or null when there is none or the bytes are not a picture after all. */
    private fun decode(png: ByteArray?): BitmapDrawable? {
        val bytes = png ?: return null
        // The header first: a few kilobytes of PNG can claim any size, and decoding a claimed 30000px square
        // would take gigabytes. The laptop sends 128px squares, so anything past the cap is not an icon.
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
        line: List<IndexedValue<String>>,
        grid: Grid,
    ): LinearLayout =
        LinearLayout(ui.context).apply {
            val lead = (grid.columns - line.size) / 2
            for (column in 0 until grid.columns) {
                // getOrNull answers null to the negative index the leading empty cells ask it for.
                val slot = line.getOrNull(column - lead)
                val cell = if (slot == null) View(ui.context) else button(slot, grid)
                addView(
                    cell,
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f).apply {
                        if (column > 0) marginStart = ui.dp(Space.S)
                    },
                )
            }
        }

    /** The empty state's picture: the macro glyph, dim, on a rounded card tile. */
    private fun emptyTile(): View =
        FrameLayout(ui.context).apply {
            background = ui.rounded(ui.palette.card, Space.PANEL_RADIUS)
            importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            addView(
                ImageView(ui.context).apply {
                    setImageResource(R.drawable.ic_macro)
                    imageTintList = ColorStateList.valueOf(ui.palette.dim)
                },
                FrameLayout.LayoutParams(ui.dp(EMPTY_ICON_DP), ui.dp(EMPTY_ICON_DP), Gravity.CENTER),
            )
        }

    /**
     * A card on the ground, the picture over a bold ink label, rather than a tinted accent button: a wall of
     * accent would paint the whole screen blue, and the accent is kept for what is live. Built from plain
     * text, not from [Ui.button]: that set up a press and a click of its own, which this then replaced with
     * a second one rounded to the card's corners.
     */
    private fun button(
        slot: IndexedValue<String>,
        grid: Grid,
    ): TextView =
        ui.text("", grid.labelSp, ui.palette.ink, face = Type.bold).apply {
            gravity = Gravity.CENTER
            minHeight = ui.dp(Space.BUTTON)
            background = ui.rounded(ui.palette.card, Space.PANEL_RADIUS)
            ui.tappable(this, Space.PANEL_RADIUS) { onRun(slot.index) }
            // A long name is cut rather than wrapped, so every button in the grid stays one row tall.
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
            // Spoken as the laptop spells it, and named the same way when the label is not drawn at all.
            contentDescription = ui.string(R.string.macros_run, slot.value)
            buttons[slot.index] = this
            dress(this, slot, grid)
        }

    /**
     * What a button shows: its name, or nothing at all when it has a picture and the labels are turned off,
     * and its picture when there is one. A button showing its picture only still answers to its name — the
     * label is what is dropped, not what the slot is called, so TalkBack reads the same thing either way.
     */
    private fun dress(
        button: TextView,
        slot: IndexedValue<String>,
        grid: Grid,
    ) {
        val picture = pictures[slot.index]
        val shown = if (picture != null && !labels) "" else slot.value
        val side = ui.dp(Space.S)
        // A button with nothing to read reserves neither the gap under the picture nor the line under that,
        // whether it is alone in a grid of pictures or the one slot in a named grid whose picture arrived.
        val gap = if (shown.isEmpty()) 0 else ui.dp(Space.XS)
        val reserved = if (shown.isEmpty()) 0 else grid.line
        button.text = shown
        // TextView draws a top compound drawable at its own top padding and centres the text in what is
        // left under it, so the two are only centred together when the padding above and below is the
        // room left over. In a cell this tall, leaving it even would pin the picture to the ceiling.
        val slack = if (picture == null) side else ((grid.height - grid.icon - gap - reserved) / 2).coerceAtLeast(0)
        button.setPadding(side, slack, side, slack)
        picture?.setBounds(0, 0, grid.icon, grid.icon)
        button.setCompoundDrawables(null, picture, null, null)
        button.compoundDrawablePadding = gap
    }
}
