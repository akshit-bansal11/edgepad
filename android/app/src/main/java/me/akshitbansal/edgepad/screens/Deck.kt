package me.akshitbansal.edgepad.screens

import android.content.res.ColorStateList
import android.graphics.drawable.BitmapDrawable
import android.graphics.drawable.Drawable
import android.os.Bundle
import android.text.TextPaint
import android.text.TextUtils
import android.util.DisplayMetrics
import android.util.TypedValue
import android.view.Gravity
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.accessibility.AccessibilityNodeInfo
import android.view.animation.DecelerateInterpolator
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/** The smallest side a picture is drawn at: under this a cell is too small to be showing a picture at all. */
private const val ICON_MIN_DP = 16f

/**
 * How far past its own pixels a laptop's picture is stretched on a big tile. The laptop sends 128 px, so
 * this keeps one at 160 px however large the tile; past about this much the filtering shows as softness.
 */
private const val MAX_UPSCALE = 1.25f

/**
 * The label's size as a fraction of the cell's shorter side. It is held between [Type.LABEL], the size every
 * macro button used to be drawn at, and [LABEL_MAX_SP]: a grid of three has room for text the size of a
 * headline, and a headline on a button reads as a mistake rather than as a size. The ceiling is a bound on a
 * computed size rather than a size of its own, so it sits between two steps of the type scale on purpose.
 */
private const val LABEL_SCALE = 0.08f
private const val LABEL_MAX_SP = 18f

/**
 * A held tile, the way a launcher lifts an icon: a little larger than its cell, on a card with a shadow
 * under it, so it reads as picked up off the page rather than as pressed into it.
 */
private const val LIFT_SCALE = 1.1f
private const val LIFT_MS = 150L

/** How long the other tiles take to slide out of the held one's way, and the held one to settle on a drop. */
private const val REFLOW_MS = 220L
private const val SETTLE_MS = 180L

/** The close cross as drawn, and the least that answers for it: the AA floor for a small target. */
private const val CROSS_DP = 22f
private const val CROSS_MIN_HIT_DP = 24f

/**
 * How long a tapped cross waits for the laptop to answer before it can be tapped again, so a second tap sends no
 * second close, and how dim it is meanwhile.
 */
private const val CLOSE_WAIT_MS = 3000L
private const val CLOSING_ALPHA = 0.4f

/**
 * One tile of a [Deck]. [key] names it across renames of its position, [tag] is what a tap hands back and
 * is the owner's to give meaning to, and [picture] is drawn over the label when there is one. [spoken] is the
 * string a screen reader says for a tap, given the label: "Run Chrome" for a macro, "Open Keyboard" for a control.
 * [closable] puts a close cross on the tile.
 */
data class DeckTile(
    val key: String,
    val label: String,
    val tag: Int,
    val picture: Drawable?,
    val spoken: Int = R.string.macros_run,
    val closable: Boolean = false,
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
        /** A tile's shorter side. */
        val side: Int,
    )

    private var tiles: List<DeckTile> = emptyList()
    private var labels = true
    private var planFor = 0
    private var wantedRows: Int? = null
    private var across = 0
    private var down = 0
    private var onMove: (from: Int, to: Int) -> Unit = { _, _ -> }
    private var onTap: (DeckTile) -> Unit = {}
    private var onClose: (DeckTile) -> Unit = {}
    private var grid: Grid? = null
    private val buttons = HashMap<Int, TextView>()

    /** Every close cross drawn so far, by tile tag. One stays in its cell, hidden, once its tile is not closable. */
    private val crosses = HashMap<Int, View>()

    /** The tags of the tiles whose cross was tapped and is waiting on the laptop. */
    private val closing = HashSet<Int>()

    /** Every tile's cell, by its display position: the views a drag slides about, cross and all. */
    private val cells = ArrayList<View>()

    /**
     * A tile pressed and held. [home] is the place it was drawn at, [lefts] and [tops] where every cell of
     * the grid stands in the holder, [startX] and [startY] where the finger was when it lifted, and
     * [target] the place the tile would land if it were let go now.
     */
    private class Lift(
        val view: View,
        val home: Int,
        val lefts: IntArray,
        val tops: IntArray,
        val startX: Float,
        val startY: Float,
    ) {
        var target = home
    }

    private var lift: Lift? = null

    /** The latest finger position over the holder, which is where a long press lifts its tile from. */
    private var lastX = 0f
    private var lastY = 0f

    private val easeOut = DecelerateInterpolator()

    init {
        // A held tile is drawn over its neighbours and its row's edges, and is scaled past its own cell.
        holder.clipChildren = false
    }

    /**
     * Draws [tiles] into the room, [across] by [down] pixels. [labels] off draws a tile that has a picture without its name.
     *
     * A tap runs the tile through [onTap], which is how the owner reaches its [DeckTile.tag]. Pressing and holding
     * one starts a drag (or a TalkBack action moves it) to another tile's place, reporting the two display
     * positions through [onMove]. [slots] is how many cells the grid is planned for when that is more than
     * the tiles it holds, so that a short last page of a pager keeps the tile size of the full ones. [rows] asks
     * for that many rows; null, a count the grid cannot be split into, or one that does not fit in [down], leaves
     * the choice to [plan]. A tap on a [DeckTile.closable] tile's cross goes to [onClose], never to [onTap].
     */
    fun render(
        across: Int,
        down: Int,
        tiles: List<DeckTile>,
        labels: Boolean,
        onMove: (from: Int, to: Int) -> Unit,
        onTap: (DeckTile) -> Unit,
        slots: Int = tiles.size,
        rows: Int? = null,
        onClose: (DeckTile) -> Unit = {},
    ) {
        this.planFor = slots
        this.wantedRows = rows
        this.across = across
        this.down = down
        this.tiles = tiles
        this.labels = labels
        this.onMove = onMove
        this.onTap = onTap
        this.onClose = onClose
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

    /** Takes every tile away, for an owner that has an empty state to put in the holder instead. */
    fun clear() {
        holder.removeAllViews()
        buttons.clear()
        crosses.clear()
        cells.clear()
        // A rebuild under a held tile drops the hold: the view it held is gone, and the finger's next move
        // goes to the new grid as an ordinary touch.
        lift = null
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
            render(across, down, tiles, labels, onMove, onTap, planFor, wantedRows, onClose)
            return
        }
        val button = buttons[tag] ?: return
        dress(button, tiles.first { it.tag == tag }, current)
    }

    /**
     * The tiles tagged in [open] have a cross and the rest do not. Only the crosses change, so the grid is not built
     * again, and what the laptop said ends the wait of any cross that was tapped.
     */
    fun setOpen(open: Set<Int>) {
        tiles = tiles.map { it.copy(closable = it.tag in open) }
        closing.clear()
        val current = grid ?: return
        tiles.forEach { tile ->
            val cross = crosses[tile.tag] ?: if (tile.closable) addCross(tile, current) else null
            cross?.let { dressCross(it, tile) }
        }
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
        val count = maxOf(planFor, tiles.size)
        val gap = ui.dp(Space.S)
        val pad = 2 * ui.dp(Space.S)
        // The column count that leaves the cell's shorter side longest, which is the largest a button can be
        // drawn without one of its sides being wasted on the other. Counted up to the number of tiles rather
        // than to what fits: three tiles are then three big ones, not three small ones in a wide row.
        var chosen = 1
        var width = 1
        var height = ui.dp(Space.TOUCH)
        // A row count the owner chose fixes the columns, as long as its rows fit: the touch floor below would push
        // the bottom ones off a pager page, where nothing scrolls to them.
        val forced =
            wantedRows
                ?.takeIf { DeckLayout.rowsFit(it, down, ui.dp(Space.TOUCH), gap) }
                ?.let { DeckLayout.columnsFor(count, it) }
        val candidates = if (forced != null) forced..forced else 1..count
        for (candidate in candidates) {
            val rows = (count + candidate - 1) / candidate
            val cellWidth = ((across - (candidate - 1) * gap) / candidate).coerceAtLeast(1)
            // Never under a touch target, however many tiles there are: below that the page scrolls instead.
            val cellHeight = ((down - (rows - 1) * gap) / rows).coerceAtLeast(ui.dp(Space.TOUCH))
            if (minOf(cellWidth, cellHeight) > minOf(width, height)) {
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
        return Grid(chosen, height, labelSp, line, icon, bare, minOf(width, height))
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
            // A tile on its way to another row, or held, is drawn outside this one.
            clipChildren = false
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
                    cell(tile, grid, first + column),
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f).apply {
                        marginStart = if (column == 0) edge else gap
                    },
                )
            }
            if (missing > 0) spacer(edge)
        }

    /**
     * What a drag moves: the tile's button, and over it, while the tile is closable, its cross. The cell rather than
     * the button is what is lifted and slid, so the cross stays on its tile. The cross is the upper child and takes
     * its own touches, which is why a press on it reaches neither the tap nor the long press under it.
     */
    private fun cell(
        tile: DeckTile,
        grid: Grid,
        position: Int,
    ): FrameLayout {
        val match = ViewGroup.LayoutParams.MATCH_PARENT
        val cell = FrameLayout(ui.context)
        cell.addView(button(tile, grid, position), FrameLayout.LayoutParams(match, match))
        cells.add(cell)
        if (tile.closable) addCross(tile, grid)?.let { dressCross(it, tile) }
        return cell
    }

    /**
     * Adds the close cross to [tile]'s cell, at its top-end corner: a card-coloured circle in the corner of a larger
     * area that answers for it, sized by [DeckLayout.crossHit]. Null when the tile is not drawn in this deck.
     */
    private fun addCross(
        tile: DeckTile,
        grid: Grid,
    ): View? {
        val cell = buttons[tile.tag]?.parent as? FrameLayout ?: return null
        val hit = DeckLayout.crossHit(grid.side, ui.dp(Space.TOUCH), ui.dp(CROSS_MIN_HIT_DP))
        val side = ui.dp(CROSS_DP)
        val inset = ui.dp(Space.XS)
        val cross =
            FrameLayout(ui.context).apply {
                contentDescription = ui.string(R.string.macros_close, tile.label)
                ui.tappable(this, Space.TILE_RADIUS) { close(tile) }
                addView(
                    ImageView(ui.context).apply {
                        setImageResource(R.drawable.ic_close)
                        imageTintList = ColorStateList.valueOf(ui.palette.ink)
                        background = ui.rounded(ui.palette.card, CROSS_DP / 2)
                        setPadding(inset, inset, inset, inset)
                    },
                    FrameLayout.LayoutParams(side, side, Gravity.TOP or Gravity.END),
                )
            }
        cell.addView(cross, FrameLayout.LayoutParams(hit, hit, Gravity.TOP or Gravity.END))
        crosses[tile.tag] = cross
        return cross
    }

    /** Shows [tile]'s cross only while the tile is closable, dimmed and deaf while it waits on the laptop. */
    private fun dressCross(
        cross: View,
        tile: DeckTile,
    ) {
        val waiting = tile.tag in closing
        cross.visibility = if (tile.closable) View.VISIBLE else View.GONE
        cross.isEnabled = !waiting
        cross.alpha = if (waiting) CLOSING_ALPHA else 1f
    }

    private fun isClosable(tag: Int): Boolean = tiles.any { it.tag == tag && it.closable }

    /** [tile]'s cross was tapped, or its TalkBack action run: once, until the laptop answers or the wait runs out. */
    private fun close(tile: DeckTile) {
        val tag = tile.tag
        if (tag in closing || !isClosable(tag)) return
        closing.add(tag)
        redressCross(tag)
        holder.postDelayed(
            {
                closing.remove(tag)
                redressCross(tag)
            },
            CLOSE_WAIT_MS,
        )
        onClose(tile)
    }

    private fun redressCross(tag: Int) {
        val cross = crosses[tag] ?: return
        dressCross(cross, tiles.firstOrNull { it.tag == tag } ?: return)
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
            ui.tappable(this, Space.PANEL_RADIUS) { onTap(tile) }
            reorderable(this, tile, position)
            // A long name is cut rather than wrapped, so every button in the grid stays one row tall.
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
            // Spoken as the laptop spells it, and named the same way when the label is not drawn at all.
            contentDescription = ui.string(tile.spoken, tile.label)
            buttons[tile.tag] = this
            dress(this, tile, grid)
        }

    /**
     * Moving one tile: pressed and held, it lifts and follows the finger while the others slide out of its
     * way, the way a phone's home screen reflows; and the same move as two TalkBack actions, since a drag
     * cannot be made without sight. The long press is the system's own, so it comes with the system's
     * haptic and with its timeout, and a press that lifts a tile never runs it. A closable tile also offers
     * its cross as a TalkBack action: the platform's dismiss, with the cross's own words on it.
     */
    private fun reorderable(
        button: TextView,
        tile: DeckTile,
        position: Int,
    ) {
        button.setOnLongClickListener { pickUp(it, position) }
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
        val dismiss =
            AccessibilityNodeInfo.AccessibilityAction(
                AccessibilityNodeInfo.ACTION_DISMISS,
                ui.string(R.string.macros_close, tile.label),
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
                    if (isClosable(tile.tag)) info.addAction(dismiss)
                }

                override fun performAccessibilityAction(
                    host: View,
                    action: Int,
                    args: Bundle?,
                ): Boolean {
                    if (action == AccessibilityNodeInfo.ACTION_DISMISS) {
                        close(tile)
                        return true
                    }
                    val to =
                        when (action) {
                            R.id.action_move_earlier -> position - 1
                            R.id.action_move_later -> position + 1
                            else -> return super.performAccessibilityAction(host, action, args)
                        }
                    if (to in tiles.indices) onMove(position, to)
                    return true
                }
            }
    }

    /**
     * Every touch over the holder, before its tiles see it, from the holder's own dispatch. While no tile is
     * held it only notes where the finger is and lets the touch through; once one is held it takes the rest
     * of the gesture, so the tile under the finger never sees its release and never runs. True when taken.
     */
    fun touch(event: MotionEvent): Boolean {
        lastX = event.x
        lastY = event.y
        val held = lift ?: return false
        // A cancel from above (a call coming in, the window losing focus) sends everything back home.
        when (event.actionMasked) {
            MotionEvent.ACTION_MOVE -> follow(held, event.x, event.y)
            MotionEvent.ACTION_UP -> drop(held, held.target)
            MotionEvent.ACTION_CANCEL -> drop(held, held.home)
        }
        return true
    }

    /**
     * Lifts the tile at [position], whose [button] was held: larger, on a card with a shadow, over the rows
     * around it. The cells' places are read once here, from the laid-out grid, so nothing is measured while
     * the finger moves.
     */
    private fun pickUp(
        button: View,
        position: Int,
    ): Boolean {
        if (lift != null || cells.size != tiles.size) return false
        val lefts = IntArray(cells.size) { (cells[it].parent as View).left + cells[it].left }
        val tops = IntArray(cells.size) { (cells[it].parent as View).top + cells[it].top }
        val view = cells[position]
        lift = Lift(view, position, lefts, tops, lastX, lastY)
        // The press shading goes: the tile is held now, not pressed, and it never sees the touch end.
        button.isPressed = false
        view.background = ui.rounded(ui.palette.card, Space.PANEL_RADIUS)
        val raised = ui.dp(Space.S).toFloat()
        // Rows are drawn in order, so the held tile's row is raised above the others or a later row hides it.
        (view.parent as View).translationZ = raised
        view
            .animate()
            .scaleX(LIFT_SCALE)
            .scaleY(LIFT_SCALE)
            .translationZ(raised)
            .setDuration(LIFT_MS)
            .setInterpolator(easeOut)
        // The page scrolls; a held tile dragged up or down must move the tile, not the page.
        holder.parent?.requestDisallowInterceptTouchEvent(true)
        return true
    }

    /**
     * The held tile under the finger, and the cell the finger is over as the place it would land. A gap
     * between cells keeps the last place, so a tile crossing one does not flicker; off the deck altogether
     * is no place, and everything slides back home until the finger returns.
     */
    private fun follow(
        held: Lift,
        x: Float,
        y: Float,
    ) {
        held.view.translationX = x - held.startX
        held.view.translationY = y - held.startY
        val inside = x >= 0 && y >= 0 && x < holder.width && y < holder.height
        val over = DeckLayout.slotAt(x, y, held.lefts, held.tops, held.view.width, held.view.height)
        val target =
            when {
                !inside -> held.home
                over >= 0 -> over
                else -> held.target
            }
        if (target == held.target) return
        held.target = target
        held.view.performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
        reflow(held)
    }

    /** Slides every tile but the held one to where it stands with the held one over [Lift.target]. */
    private fun reflow(held: Lift) {
        val places = DeckLayout.places(cells.size, held.home, held.target)
        cells.forEachIndexed { at, cell ->
            if (at == held.home) return@forEachIndexed
            cell
                .animate()
                .translationX((held.lefts[places[at]] - held.lefts[at]).toFloat())
                .translationY((held.tops[places[at]] - held.tops[at]).toFloat())
                .setDuration(REFLOW_MS)
                .setInterpolator(easeOut)
        }
    }

    /**
     * Lets the held tile go at place [to]: it settles into that cell, and only then is the order changed,
     * because that rebuilds the grid and the rebuilt grid draws every tile exactly where the slide left it.
     * [to] at home is a cancel, or a press that never moved, and moves nothing.
     */
    private fun drop(
        held: Lift,
        to: Int,
    ) {
        lift = null
        held.target = to
        reflow(held)
        val view = held.view
        view
            .animate()
            .translationX((held.lefts[to] - held.lefts[held.home]).toFloat())
            .translationY((held.tops[to] - held.tops[held.home]).toFloat())
            .scaleX(1f)
            .scaleY(1f)
            .translationZ(0f)
            .setDuration(SETTLE_MS)
            .setInterpolator(easeOut)
            .withEndAction {
                view.background = null
                (view.parent as? View)?.translationZ = 0f
                if (to != held.home) onMove(held.home, to)
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
        // A vector is drawn at the cell's full size and stays sharp at any of them. A laptop's picture is a
        // bitmap of a fixed size, and stretched past a little over that it reads as a blur rather than as
        // an icon, so on a big tile it is drawn at most [MAX_UPSCALE] times its own pixels, centred.
        val icon =
            if (picture is BitmapDrawable) {
                minOf(grid.icon, (picture.bitmap.width * MAX_UPSCALE).toInt())
            } else {
                grid.icon
            }
        // TextView draws a top compound drawable at its own top padding and centres the text in what is
        // left under it, so the two are only centred together when the padding above and below is the
        // room left over. In a cell this tall, leaving it even would pin the picture to the ceiling.
        val slack = if (picture == null) side else ((grid.height - icon - gap - reserved) / 2).coerceAtLeast(0)
        button.setPadding(side, slack, side, slack)
        picture?.setBounds(0, 0, icon, icon)
        button.setCompoundDrawables(null, picture, null, null)
        button.compoundDrawablePadding = gap
    }
}
