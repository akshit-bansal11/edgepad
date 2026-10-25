package me.akshitbansal.edgepad.screens

import android.graphics.drawable.Drawable
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.WindowInsets
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space

/**
 * A page that is a [Deck] under a nav bar, with everything the macro grid and the Controls grid share:
 * tiles pressed and held, then dragged (or moved by a screen reader's actions) into the owner's order, an
 * optional settings button in the bar, and the measuring of the room the grid is drawn in. The order and
 * the grid ([MacroSettingsScreen]) are phone-side drawing choices kept per [deckId]. A tap hands back the
 * [DeckTile] it was given, never its place on screen, so a screen whose tiles stand for something on the
 * laptop (a macro slot) puts that in [DeckTile.tag] and can be arranged freely without running the wrong thing.
 *
 * An owner supplies tiles with [show], and only what differs between owners stays with them: what the tiles
 * are, what a tap does, and [empty], the state drawn when there are no tiles.
 */
class DeckPage(
    private val ui: Ui,
    private val settings: Settings,
    private val deckId: String,
    title: String,
    backLabel: String,
    onBack: () -> Unit,
    private val labels: Boolean,
    private val empty: ((LinearLayout) -> Unit)? = null,
    onSettings: (() -> Unit)? = null,
    private val onTap: (DeckTile) -> Unit,
) {
    private var tiles: List<DeckTile> = emptyList()

    /** The room the grid has, across and down, once the window has said how much of it the bars take. */
    private var room: Pair<Int, Int>? = null

    // The nav bar every sub-screen has, back with the name centred. It costs the grid no row: the title sits
    // in the bar's own height, which [measureRoom] takes off the page.
    private val gear = onSettings?.let { ui.icon(Glyph.Shape.GEAR, ui.string(R.string.macros_settings), it) }
    private val bar = ui.bar(title, onBack, *listOfNotNull(gear).toTypedArray(), backLabel = backLabel)
    private lateinit var holder: LinearLayout

    /** The tiles in the order they are drawn, as indexes into [tiles], and the stored-order key of each tile. */
    private var shown: List<Int> = emptyList()
    private var keys: List<String> = emptyList()

    /** The (rows, columns) grids the room can draw, for telling a stored column count that no longer fits. */
    private var fits: List<Pair<Int, Int>> = emptyList()

    val view: View =
        ui.page(bar) {
            // Takes the page's whole height, so an empty state can centre itself in it. The deck sees every
            // touch before its tiles do, which is how a held tile keeps the finger once it has left its cell.
            val grid =
                object : LinearLayout(ui.context) {
                    override fun dispatchTouchEvent(event: MotionEvent): Boolean =
                        deck.touch(event) || super.dispatchTouchEvent(event)
                }
            holder =
                add(grid.apply { orientation = LinearLayout.VERTICAL }, height = 0).apply {
                    (layoutParams as LinearLayout.LayoutParams).weight = 1f
                }
        }

    private val deck = Deck(ui, holder)

    init {
        // Where a moved tile landed is spoken from the bar, which outlives every rebuild of the grid; see [moved].
        bar.accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
        // The grid is sized from the room the page really has, which is known only once the window's insets
        // have reached it, rather than from the display's size less a guess at the system bars.
        view.addOnLayoutChangeListener { _, _, _, _, _, _, _, _, _ ->
            val measured = measureRoom()
            if (measured != null && measured != room) {
                room = measured
                // Not inside the layout pass that reported it: views added there would wait a frame anyway.
                view.post { render() }
            }
        }
    }

    /** A new set of tiles, in the owner's natural order; the stored order is applied here. Keys must be unique. */
    fun show(tiles: List<DeckTile>) {
        this.tiles = tiles
        render()
    }

    /** One tile's picture arrived: that tile is dressed in it, without building the grid again. */
    fun setPicture(
        tag: Int,
        picture: Drawable?,
    ) {
        if (tiles.none { it.tag == tag }) return
        tiles = tiles.map { if (it.tag == tag) it.copy(picture = picture) else it }
        deck.setPicture(tag, picture)
    }

    private fun render() {
        deck.clear()
        if (tiles.isEmpty()) {
            holder.gravity = Gravity.CENTER
            empty?.invoke(holder)
            return
        }
        holder.gravity = Gravity.NO_GRAVITY
        val (across, down) = room ?: return
        keys = tiles.map { it.key }
        shown = DeckLayout.order(settings.deckOrder(deckId), keys)
        fits = DeckLayout.fitting(tiles.size, across, down, ui.dp(Space.S), ui.dp(Space.TOUCH))
        draw()
    }

    /** Draws the deck from [shown]. It survives a move. */
    private fun draw() {
        val (across, down) = room ?: return
        // A stored grid that is no longer on offer (a tile was added, the screen turned) falls back to automatic.
        val columns = settings.deckColumns(deckId).takeIf { stored -> fits.any { it.second == stored } }
        deck.render(across, down, shown.map { tiles[it] }, labels, columns, onMove = ::moved, onTap = onTap)
    }

    /** The tile at display place [from] was dropped on [to]. Saved on every move, so leaving loses nothing. */
    private fun moved(
        from: Int,
        to: Int,
    ) {
        shown = DeckLayout.move(shown, from, to)
        settings.setDeckOrder(deckId, shown.map { keys[it] })
        draw()
        // The rebuilt grid took the screen reader's place with it, and taking its focus back is not the app's
        // to do. It is told where the tile went instead, through the bar that outlives every rebuild. Cleared
        // first, so the same place twice running is still a change, and is read out again.
        bar.stateDescription = null
        bar.stateDescription = ui.string(R.string.macros_moved, to + 1, shown.size)
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
}
