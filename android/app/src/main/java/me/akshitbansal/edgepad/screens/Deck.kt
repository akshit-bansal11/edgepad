package me.akshitbansal.edgepad.screens

import android.content.ClipData
import android.graphics.Color
import android.graphics.drawable.Drawable
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.text.TextPaint
import android.text.TextUtils
import android.util.DisplayMetrics
import android.util.TypedValue
import android.view.DragEvent
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.accessibility.AccessibilityNodeInfo
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/** The smallest side a picture is drawn at: under this a cell is too small to be showing a picture at all. */
private const val ICON_MIN_DP = 16f

/**
 * The label's size as a fraction of the cell's shorter side. It is held between [Type.LABEL], the size every
 * macro button used to be drawn at, and [LABEL_MAX_SP]: a grid of three has room for text the size of a
 * headline, and a headline on a button reads as a mistake rather than as a size. The ceiling is a bound on a
 * computed size rather than a size of its own, so it sits between two steps of the type scale on purpose.
 */
private const val LABEL_SCALE = 0.08f
private const val LABEL_MAX_SP = 18f

// The edit-mode outline: dashes, so it reads as "can be moved" rather than as a button's border.
private const val DASH_DP = 4f
private const val DASH_GAP_DP = 3f

/** How faint the tile under a dragged one goes, to show that it is where the drop would land. */
private const val DROP_ALPHA = 0.5f

/**
 * One tile of a [Deck]. [key] names it across renames of its position, [tag] is what a tap hands back and
 * is the owner's to give meaning to, and [picture] is drawn over the label when there is one.
 */
data class DeckTile(
    val key: String,
    val label: String,
    val tag: Int,
    val picture: Drawable?,
)

/**
 * A grid of tiles spread over the whole room it is given: three tiles are drawn as large as the room
 * allows and fifteen as small as they have to be, because a grid of three huddled at the top of an empty
 * page wastes the one thing a wall-mounted phone has plenty of. The tiles sit on the page's ground with
 * no fill of their own; a tap, or the focus ring, is the only thing that draws a shape around one.
 *
 * It draws into [holder] and keeps what it was last asked to draw, so that one picture arriving later
 * ([setPicture]) dresses one tile instead of building the grid again.
 */
class Deck(
    private val ui: Ui,
    private val holder: LinearLayout,
) {
    /**
     * Everything the grid decided before it drew anything: how many tiles stand across a row, how tall
     * each one is, and how the label and the picture inside it are sized to match.
     */
    private class Grid(
        val columns: Int,
        val height: Int,
        val labelSp: Float,
        val line: Int,
        val icon: Int,
        /** No label row anywhere: the labels are off and every tile has its picture. */
        val bare: Boolean,
    )

    private var tiles: List<DeckTile> = emptyList()
    private var labels = true
    private var columns: Int? = null
    private var across = 0
    private var down = 0
    private var onMove: ((from: Int, to: Int) -> Unit)? = null
    private var onTap: (DeckTile) -> Unit = {}
    private var grid: Grid? = null
    private val buttons = HashMap<Int, TextView>()

    /**
     * Draws [tiles] into the room, [across] by [down] pixels. [columns] fixes how many stand across a row;
     * null lets the room decide. [labels] off draws a tile that has a picture without its name.
     *
     * [onMove] non-null is edit mode: a tap runs nothing, and a tile is dragged (or moved by a TalkBack
     * action) to another tile's place, reporting the two display positions. It never says what a tile
     * does; [onTap] is how live mode reaches the tile's [DeckTile.tag].
     */
    fun render(
        across: Int,
        down: Int,
        tiles: List<DeckTile>,
        labels: Boolean,
        columns: Int?,
        onMove: ((from: Int, to: Int) -> Unit)?,
        onTap: (DeckTile) -> Unit,
    ) {
        this.across = across
        this.down = down
        this.tiles = tiles
        this.labels = labels
        this.columns = columns
        this.onMove = onMove
        this.onTap = onTap
        clear()
        if (tiles.isEmpty()) return
        val planned = plan(bare())
        grid = planned
        tiles.chunked(planned.columns).forEachIndexed { at, line ->
            holder.addView(
                row(line, planned, first = at * planned.columns),
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, planned.height).apply {
                    // The first row sits straight under the bar. A gap above it as well would be one gap more
                    // than the height was worked out from, and the last row would fall off the bottom.
                    if (at > 0) topMargin = ui.dp(Space.S)
                },
            )
        }
    }

    /**
     * Puts accessibility focus on the tile for [tag], once it is attached to the rebuilt grid. Through the
     * public action rather than `requestAccessibilityFocus()`, which the SDK hides from apps.
     */
    fun focus(tag: Int) {
        val tile = buttons[tag] ?: return
        tile.post { tile.performAccessibilityAction(AccessibilityNodeInfo.ACTION_ACCESSIBILITY_FOCUS, null) }
    }

    /** Takes every tile away, for an owner that has an empty state to put in the holder instead. */
    fun clear() {
        holder.removeAllViews()
        buttons.clear()
        grid = null
    }

    /** The picture for [tag]'s tile arrived: that one tile is dressed in it, unless that changes the whole grid. */
    fun setPicture(
        tag: Int,
        picture: Drawable?,
    ) {
        val current = grid ?: return
        tiles = tiles.map { if (it.tag == tag) it.copy(picture = picture) else it }
        // The last picture arriving with the labels off takes the label row away from every tile, and
        // that is a new grid rather than one new picture.
        if (bare() != current.bare) {
            render(across, down, tiles, labels, columns, onMove, onTap)
            return
        }
        val button = buttons[tag] ?: return
        dress(button, tiles.first { it.tag == tag }, current)
    }

    /**
     * Pictures and nothing else: the labels are turned off and every tile answered with one. That is the
     * one case where no row is kept for a label and the card belongs entirely to the picture. A tile whose
     * picture never arrived still has to fall back to its name, so one missing picture keeps the label row
     * for the lot.
     */
    private fun bare(): Boolean = !labels && tiles.all { it.picture != null }

    /**
     * Works the grid out from the room it has and the number of tiles in it.
     *
     * The arithmetic happens here, once the room is known, rather than in a layout that measures its own
     * children, which would have to allocate inside onLayout, and this project's lint rejects that outright.
     */
    private fun plan(bare: Boolean): Grid {
        val metrics = ui.context.resources.displayMetrics
        val count = tiles.size
        val gap = ui.dp(Space.S)
        val pad = 2 * ui.dp(Space.S)
        // The column count that leaves the cell's shorter side longest, which is the largest a button can be
        // drawn without one of its sides being wasted on the other. Counted up to the number of tiles rather
        // than to what fits: three tiles are then three big ones, not three small ones in a wide row. A
        // fixed count is the only candidate there is, and is taken as it stands.
        val fixed = columns?.coerceIn(1, count)
        var chosen = 1
        var width = 1
        var height = ui.dp(Space.TOUCH)
        for (candidate in if (fixed != null) fixed..fixed else 1..count) {
            val rows = (count + candidate - 1) / candidate
            val cellWidth = ((across - (candidate - 1) * gap) / candidate).coerceAtLeast(1)
            // Never under a touch target, however many tiles there are: below that the page scrolls instead.
            val cellHeight = ((down - (rows - 1) * gap) / rows).coerceAtLeast(ui.dp(Space.TOUCH))
            if (fixed != null || minOf(cellWidth, cellHeight) > minOf(width, height)) {
                chosen = candidate
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
        return Grid(chosen, height, labelSp, line, icon, bare)
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

    /**
     * One line of the grid. A short last row keeps the others' tile width and is centred under them: the
     * missing columns are two spacers of half their width each, rather than whole empty cells heaped on
     * one side, so a last row of two under rows of three sits exactly under the middle of them. The gaps a
     * full row would have between its tiles that this row lacks are shared between the two ends for the
     * same reason, which keeps every tile in the grid the same width. [first] is the display position of
     * the row's first tile.
     */
    private fun row(
        line: List<DeckTile>,
        grid: Grid,
        first: Int,
    ): LinearLayout =
        LinearLayout(ui.context).apply {
            val missing = grid.columns - line.size
            val half = missing / 2f
            val gap = ui.dp(Space.S)
            val edge = (missing * gap / 2f).toInt()

            fun spacer(start: Int) =
                addView(
                    View(ui.context),
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, half).apply {
                        marginStart = start
                    },
                )
            if (missing > 0) spacer(0)
            line.forEachIndexed { column, tile ->
                addView(
                    button(tile, grid, first + column),
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f).apply {
                        marginStart = if (column == 0) edge else gap
                    },
                )
            }
            if (missing > 0) spacer(edge)
        }

    /**
     * A tile on the ground, the picture over a bold ink label. It has no fill: the picture and the name
     * are the tile, and a card behind each of them was a second shape around a shape. Built from plain
     * text, not from [Ui.button]: that set up a press and a click of its own, which this then replaced with
     * a second one rounded to the tile's corners.
     */
    private fun button(
        tile: DeckTile,
        grid: Grid,
        position: Int,
    ): TextView =
        ui.text("", grid.labelSp, ui.palette.ink, face = Type.bold).apply {
            gravity = Gravity.CENTER
            minHeight = ui.dp(Space.BUTTON)
            val move = onMove
            if (move == null) {
                ui.tappable(this, Space.PANEL_RADIUS) { onTap(tile) }
            } else {
                reorderable(this, tile, position, move)
            }
            // A long name is cut rather than wrapped, so every button in the grid stays one row tall.
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
            // Spoken as the laptop spells it, and named the same way when the label is not drawn at all. In edit
            // mode a tap runs nothing, so the tile must not be announced as something that runs.
            val spoken = if (move == null) R.string.macros_run else R.string.macros_reorder
            contentDescription = ui.string(spoken, tile.label)
            buttons[tile.tag] = this
            dress(this, tile, grid)
        }

    /**
     * Edit mode for one tile: a dashed outline, a long press that starts a drag, and the same move as two
     * TalkBack actions, since a drag cannot be made without sight. A drop onto a tile puts the dragged one
     * in that tile's place. The move is posted rather than made inside the drop, because it rebuilds the
     * grid and with it the very views the drag is still being dispatched to.
     */
    private fun reorderable(
        button: TextView,
        tile: DeckTile,
        position: Int,
        move: (from: Int, to: Int) -> Unit,
    ) {
        button.isFocusable = true
        button.background =
            GradientDrawable().apply {
                setColor(Color.TRANSPARENT)
                setStroke(ui.dp(Space.HAIR), ui.palette.dim, ui.dp(DASH_DP).toFloat(), ui.dp(DASH_GAP_DP).toFloat())
                cornerRadius = ui.dp(Space.PANEL_RADIUS).toFloat()
            }
        button.setOnLongClickListener {
            it.startDragAndDrop(ClipData.newPlainText(tile.key, tile.key), View.DragShadowBuilder(it), position, 0)
        }
        button.setOnDragListener { target, event ->
            when (event.action) {
                DragEvent.ACTION_DRAG_ENTERED -> target.alpha = DROP_ALPHA
                DragEvent.ACTION_DRAG_EXITED, DragEvent.ACTION_DRAG_ENDED -> target.alpha = 1f
                DragEvent.ACTION_DROP -> {
                    target.alpha = 1f
                    val from = event.localState as? Int
                    if (from != null && from != position) target.post { move(from, position) }
                }
            }
            true
        }
        val earlier =
            AccessibilityNodeInfo.AccessibilityAction(
                R.id.action_move_earlier,
                ui.string(R.string.macros_move_earlier),
            )
        val later =
            AccessibilityNodeInfo.AccessibilityAction(
                R.id.action_move_later,
                ui.string(R.string.macros_move_later),
            )
        button.accessibilityDelegate =
            object : View.AccessibilityDelegate() {
                override fun onInitializeAccessibilityNodeInfo(
                    host: View,
                    info: AccessibilityNodeInfo,
                ) {
                    super.onInitializeAccessibilityNodeInfo(host, info)
                    if (position > 0) info.addAction(earlier)
                    if (position < tiles.size - 1) info.addAction(later)
                }

                override fun performAccessibilityAction(
                    host: View,
                    action: Int,
                    args: Bundle?,
                ): Boolean {
                    val to =
                        when (action) {
                            R.id.action_move_earlier -> position - 1
                            R.id.action_move_later -> position + 1
                            else -> return super.performAccessibilityAction(host, action, args)
                        }
                    if (to in tiles.indices) move(position, to)
                    return true
                }
            }
    }

    /**
     * What a button shows: its name, or nothing at all when it has a picture and the labels are turned off,
     * and its picture when there is one. A button showing its picture only still answers to its name — the
     * label is what is dropped, not what the tile is called, so TalkBack reads the same thing either way.
     */
    private fun dress(
        button: TextView,
        tile: DeckTile,
        grid: Grid,
    ) {
        val picture = tile.picture
        val shown = if (picture != null && !labels) "" else tile.label
        val side = ui.dp(Space.S)
        // A button with nothing to read reserves neither the gap under the picture nor the line under that,
        // whether it is alone in a grid of pictures or the one tile in a named grid whose picture arrived.
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
