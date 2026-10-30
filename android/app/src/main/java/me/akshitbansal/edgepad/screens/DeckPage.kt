package me.akshitbansal.edgepad.screens

import android.graphics.drawable.Drawable
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
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
 *
 * With [perPage] the tiles are split into pages of that many, which a finger slides between; every page is a
 * grid planned for [perPage] cells, so a tile keeps its size from page to page. Without it there is one page.
 * [rows] asks for that many rows in every grid; null lets each grid pick the columns that make the biggest tiles.
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
    private val perPage: Int? = null,
    private val rows: Int? = null,
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

    /** The page showing, and the deck of each page once there are two or more. */
    private var page = 0
    private val pageDecks = ArrayList<Deck>()

    /** One page of the pager: a deck of its own, which sees every touch before its tiles do, like the grid below. */
    private inner class PageHolder : LinearLayout(ui.context) {
        val deck = Deck(ui, this)

        init {
            orientation = LinearLayout.VERTICAL
        }

        override fun dispatchTouchEvent(event: MotionEvent): Boolean =
            deck.touch(event) || super.dispatchTouchEvent(event)
    }

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
        pageDecks.forEach { it.setPicture(tag, picture) }
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
        draw()
    }

    /** Draws the deck from [shown]. It survives a move. */
    private fun draw() {
        val (across, down) = room ?: return
        pageDecks.clear()
        val ordered = shown.map { tiles[it] }
        val pages = perPage?.let { DeckLayout.pages(ordered.size, it) }.orEmpty()
        if (perPage != null && pages.size > 1) {
            drawPages(across, down, ordered, pages, perPage)
            return
        }
        val slots = perPage ?: ordered.size
        deck.render(across, down, ordered, labels, onMove = ::moved, onTap = onTap, slots = slots, rows = rows)
    }

    /**
     * Two or more pages: a [Pager] of decks over a row of dots. Every deck is planned for [per] cells in the
     * room less the dots, and a tile moves within its own page only. A page's positions are offset by where
     * the page starts, so the stored order stays one list.
     */
    private fun drawPages(
        across: Int,
        down: Int,
        ordered: List<DeckTile>,
        pages: List<IntRange>,
        per: Int,
    ) {
        // ponytail: a tile cannot be dragged onto another page; edge-hover paging would need the pager to scroll
        // under a held tile. Within a page it moves as on a single one, and the pager is held still meanwhile.
        deck.clear()
        val dots = PageDots(ui, pages.size)
        val pager =
            Pager(ui.context) { at ->
                page = at
                dots.select(at)
            }
        pages.forEach { range ->
            val pageHolder = PageHolder()
            pageDecks.add(pageHolder.deck)
            pager.row.addView(pageHolder, LinearLayout.LayoutParams(across, ViewGroup.LayoutParams.MATCH_PARENT))
            pageHolder.deck.render(
                across,
                down - ui.dp(Space.XL),
                ordered.slice(range),
                labels,
                onMove = { from, to -> moved(range.first + from, range.first + to) },
                onTap = onTap,
                slots = per,
                rows = rows,
            )
        }
        holder.addView(pager, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
        holder.addView(dots, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(Space.XL)))
        page = page.coerceAtMost(pages.lastIndex)
        dots.select(page)
        pager.jumpTo(page)
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
