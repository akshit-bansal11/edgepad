package me.akshitbansal.edgepad.screens

import android.graphics.drawable.Drawable
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space

/**
 * A page that is a [Deck] under a nav bar, with everything the macro grid and the Controls grid share:
 * the **Edit layout** chip, tiles dragged (or moved by a screen reader's actions) into the owner's order,
 * the picker for how many rows and columns the grid has, and the measuring of the room the grid is drawn in.
 * Both are phone-side drawing choices kept per [deckId]. A tap hands back the [DeckTile] it was given,
 * never its place on screen, so a screen whose tiles stand for something on the laptop (a macro slot) puts
 * that in [DeckTile.tag] and can be arranged freely without running the wrong thing.
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
    private val onTap: (DeckTile) -> Unit,
) {
    private var tiles: List<DeckTile> = emptyList()

    /** The room the grid has, across and down, once the window has said how much of it the bars take. */
    private var room: Pair<Int, Int>? = null

    // The nav bar every sub-screen has, back with the name centred. It costs the grid no row: the title sits
    // in the bar's own height, which [measureRoom] takes off the page.
    // Editing is not remembered across a rebuild on purpose: turning the phone mid-edit puts the deck back in
    // live mode, with the order already saved by every move.
    private var editing = false
    private val edit = ui.chip(ui.string(R.string.macros_edit)) { toggleEditing() }
    private val bar = ui.bar(title, onBack, edit, backLabel = backLabel)
    private lateinit var holder: LinearLayout

    /** The tiles in the order they are drawn, as indexes into [tiles], and the stored-order key of each tile. */
    private var shown: List<Int> = emptyList()
    private var keys: List<String> = emptyList()

    /** The (rows, columns) grids the picker offers, and the picker itself while in edit mode. */
    private var fits: List<Pair<Int, Int>> = emptyList()
    private var picker: View? = null

    val view: View =
        ui.page(bar) {
            // Takes the page's whole height, so an empty state can centre itself in it.
            holder =
                add(LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }, height = 0).apply {
                    (layoutParams as LinearLayout.LayoutParams).weight = 1f
                }
        }

    private val deck = Deck(ui, holder)

    init {
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
        // Editing redraws whole: the deck's own rebuild (when the last picture lets the labels go) would take
        // the picker out of the holder with it.
        if (editing) draw() else deck.setPicture(tag, picture)
    }

    private fun toggleEditing() {
        editing = !editing
        edit.text = ui.string(if (editing) R.string.macros_edit_done else R.string.macros_edit)
        render()
    }

    private fun render() {
        deck.clear()
        picker = null
        // Nothing to arrange in an empty state.
        edit.visibility = if (tiles.isEmpty()) View.GONE else View.VISIBLE
        if (tiles.isEmpty()) {
            // Nothing left to arrange, and the chip is hidden: leave edit mode so it is not stuck on Done.
            editing = false
            edit.text = ui.string(R.string.macros_edit)
            holder.gravity = Gravity.CENTER
            empty?.invoke(holder)
            return
        }
        holder.gravity = Gravity.NO_GRAVITY
        val (across, down) = room ?: return
        keys = tiles.map { it.key }
        shown = DeckLayout.order(settings.deckOrder(deckId), keys)
        // Offered against the height the deck is drawn at, so a grid the picker lists is one it can really draw.
        fits = DeckLayout.fitting(tiles.size, across, deckDown(down), ui.dp(Space.S), ui.dp(Space.TOUCH))
        if (editing) picker = gridPicker()
        draw()
    }

    /** Draws the deck from [shown], and under it the picker when editing. Both survive a move or a pick. */
    private fun draw() {
        val (across, down) = room ?: return
        // A stored grid that is no longer on offer (a tile was added, the screen turned) falls back to automatic.
        val columns = settings.deckColumns(deckId).takeIf { stored -> fits.any { it.second == stored } }
        deck.render(
            across,
            deckDown(down),
            shown.map { tiles[it] },
            labels,
            columns,
            onMove = if (editing) ::moved else null,
            onTap = onTap,
        )
        picker?.let {
            holder.addView(
                it,
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT),
            )
        }
    }

    /** The tile at display place [from] was dropped on [to]. Saved on every move, so leaving loses nothing. */
    private fun moved(
        from: Int,
        to: Int,
    ) {
        shown = DeckLayout.move(shown, from, to)
        settings.setDeckOrder(deckId, shown.map { keys[it] })
        draw()
        // The grid was rebuilt, so the screen reader lost its place: put it back on the tile that moved.
        deck.focus(tiles[shown[to]].tag)
    }

    /** The height the deck is drawn at: half the room while editing, so the picker below is not a scroll away. */
    private fun deckDown(down: Int) = if (editing) down / 2 else down

    /** "Automatic" and each grid [fits], the chosen one checked. A pick redraws the deck and keeps the list. */
    private fun gridPicker(): View {
        val stored = settings.deckColumns(deckId)
        val chosen = fits.indexOfFirst { it.second == stored } + 1
        val names =
            listOf(ui.string(R.string.macros_grid_auto)) +
                fits.map { (rows, columns) -> ui.string(R.string.macros_grid_choice, rows, columns) }
        return LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            Column(ui, this).apply {
                section(ui.string(R.string.macros_grid_title))
                card {
                    add(
                        ui.choices(names, chosen) { pick ->
                            settings.setDeckColumns(deckId, if (pick == 0) 0 else fits[pick - 1].second)
                            draw()
                        },
                    )
                }
            }
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
}
