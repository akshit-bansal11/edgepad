package me.akshitbansal.edgepad.screens

import android.content.res.ColorStateList
import android.graphics.BitmapFactory
import android.graphics.drawable.BitmapDrawable
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/** The largest picture accepted from the laptop, which sends 128px; the headroom is for an older or newer laptop. */
private const val MAX_ICON_PX = 256

/** Which deck this is, to [Settings]: the order and the grid are kept per deck. */
private const val DECK = "macros"

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
 *
 * **Edit layout** in the bar turns the grid into something to arrange: tiles are dragged (or moved by a
 * screen reader's actions) into the order the owner wants, and a list under them picks how many rows and
 * columns the grid has. Both are phone-side drawing choices. A tap still hands back the laptop's slot index,
 * never the tile's place on screen, so the order can be anything without a macro running the wrong thing.
 */
class MacroScreen(
    private val ui: Ui,
    private val settings: Settings,
    names: List<String>,
    private val icon: (Int) -> ByteArray?,
    private val labels: Boolean,
    private val onRun: (index: Int) -> Unit,
    onBack: () -> Unit,
) {
    private var slots: List<IndexedValue<String>> = emptyList()
    private val pictures = HashMap<Int, BitmapDrawable?>()

    /** The room the grid has, across and down, once the window has said how much of it the bars take. */
    private var room: Pair<Int, Int>? = null

    // The nav bar every sub-screen has, back to the surface with the name centred. It costs the grid no
    // row: the title sits in the bar's own height, which [measureRoom] takes off the page.
    // Not remembered across a rebuild on purpose: turning the phone mid-edit puts the deck back in live mode,
    // with the order already saved by every move.
    private var editing = false
    private val edit = ui.chip(ui.string(R.string.macros_edit)) { toggleEditing() }
    private val bar =
        ui.bar(
            ui.string(R.string.macros_title),
            onBack,
            edit,
            backLabel = ui.string(R.string.back_to_surface),
        )
    private lateinit var holder: LinearLayout

    /** The slots in the order they are drawn, as indexes into [slots], and the stored-order key of each slot. */
    private var shown: List<Int> = emptyList()
    private var keys: List<String> = emptyList()

    /** The (rows, columns) grids the picker offers, and the picker itself while in edit mode. */
    private var fits: List<Pair<Int, Int>> = emptyList()
    private var picker: View? = null

    val view: View =
        ui.page(bar) {
            // Takes the page's whole height, so the empty state can centre itself in it.
            holder =
                add(LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }, height = 0).apply {
                    (layoutParams as LinearLayout.LayoutParams).weight = 1f
                }
        }

    private val deck = Deck(ui, holder)

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
        // Editing redraws whole: the deck's own rebuild (when the last picture lets the labels go) would take
        // the picker out of the holder with it.
        if (editing) draw() else deck.setPicture(slot, pictures[slot])
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
        edit.visibility = if (slots.isEmpty()) View.GONE else View.VISIBLE
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
        val names = slots.map { it.value }
        keys = DeckLayout.keys(names)
        shown = DeckLayout.order(settings.deckOrder(DECK), names)
        // Offered against the room live mode has, so a grid the picker lists is one the deck can really draw.
        fits = DeckLayout.fitting(slots.size, across, down, ui.dp(Space.S), ui.dp(Space.TOUCH))
        if (editing) picker = gridPicker()
        draw()
    }

    /** Draws the deck from [shown], and under it the picker when editing. Both survive a move or a pick. */
    private fun draw() {
        val (across, down) = room ?: return
        // A stored grid that is no longer on offer (a macro was added, the screen turned) falls back to automatic.
        val columns = settings.deckColumns(DECK).takeIf { stored -> fits.any { it.second == stored } }
        deck.render(
            across,
            // Half the room while editing, so the picker below is on the screen and not a scroll away.
            if (editing) down / 2 else down,
            shown.map { tile(it) },
            labels,
            columns,
            onMove = if (editing) ::moved else null,
        ) { tile ->
            // The laptop's slot, never the tile's place in the deck: the order on screen is only a drawing.
            onRun(tile.tag)
        }
        picker?.let {
            holder.addView(
                it,
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT),
            )
        }
    }

    /** The tile for [at], an index into [slots]. [DeckTile.tag] is that slot's laptop index. */
    private fun tile(at: Int) =
        DeckTile(key = keys[at], label = slots[at].value, tag = slots[at].index, picture = pictures[slots[at].index])

    /** The tile at display place [from] was dropped on [to]. Saved on every move, so leaving loses nothing. */
    private fun moved(
        from: Int,
        to: Int,
    ) {
        shown = DeckLayout.move(shown, from, to)
        settings.setDeckOrder(DECK, shown.map { keys[it] })
        draw()
    }

    /** "Automatic" and each grid [fits], the chosen one checked. A pick redraws the deck and keeps the list. */
    private fun gridPicker(): View {
        val stored = settings.deckColumns(DECK)
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
                            settings.setDeckColumns(DECK, if (pick == 0) 0 else fits[pick - 1].second)
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
}
