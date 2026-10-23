package me.akshitbansal.edgepad.screens

import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space

/** Which deck this is, to [Settings]: the order and the grid are kept per deck. */
private const val DECK = "controls"

/**
 * What the phone can drive besides the surface itself, as a [Deck] of tiles: the keyboard, the gamepad and,
 * when the laptop says it can take one, a capture. The surface keeps a single button for all of it, so
 * the row of buttons at the top stays the size it is however many of these there are.
 *
 * It is a deck for the same reason the macro grid is: the order and the grid are the owner's to arrange
 * with **Edit layout**, and the arithmetic that spreads three tiles over a page is already written. A tile
 * carries a [Target] in its tag and nothing about where it is drawn, so a reordered deck opens the same
 * screens.
 *
 * Capture is left out, rather than shown and disabled, when [capture] is false: a tile that opens a page of
 * dead buttons explains nothing that a missing tile does not.
 */
class ControlsScreen(
    private val ui: Ui,
    private val settings: Settings,
    capture: Boolean,
    private val onOpen: (Target) -> Unit,
    onBack: () -> Unit,
) {
    /** The screens a tile can open. The order here is the order of a deck nobody has arranged. */
    enum class Target(
        val label: Int,
        val icon: Int,
    ) {
        KEYBOARD(R.string.controls_keyboard, R.drawable.ic_keyboard),
        GAMEPAD(R.string.controls_gamepad, R.drawable.ic_gamepad_2),
        CAPTURE(R.string.controls_capture, R.drawable.ic_scan),
    }

    private val tiles = Target.entries.filter { it != Target.CAPTURE || capture }.map(::tile)
    private val keys = tiles.map { it.key }
    private var shown = DeckLayout.order(settings.deckOrder(DECK), keys)

    /** The room the grid has, across and down, once the window has said how much of it the bars take. */
    private var room: Pair<Int, Int>? = null

    // Not remembered across a rebuild on purpose, as on the macro deck: the order is saved by every move.
    private var editing = false
    private val edit = ui.chip(ui.string(R.string.macros_edit)) { toggleEditing() }
    private val bar =
        ui.bar(
            ui.string(R.string.controls_title),
            onBack,
            edit,
            backLabel = ui.string(R.string.back_to_surface),
        )
    private lateinit var holder: LinearLayout
    private var fits: List<Pair<Int, Int>> = emptyList()
    private var picker: View? = null

    val view: View =
        ui.page(bar) {
            holder =
                add(LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }, height = 0).apply {
                    (layoutParams as LinearLayout.LayoutParams).weight = 1f
                }
        }

    private val deck = Deck(ui, holder)

    init {
        view.addOnLayoutChangeListener { _, _, _, _, _, _, _, _, _ ->
            val measured = measureRoom()
            if (measured != null && measured != room) {
                room = measured
                // Not inside the layout pass that reported it: views added there would wait a frame anyway.
                view.post { render() }
            }
        }
    }

    private fun tile(target: Target) =
        DeckTile(
            key = target.name.lowercase(),
            label = ui.string(target.label),
            tag = target.ordinal,
            picture = ui.context.getDrawable(target.icon)?.mutate()?.apply { setTint(ui.palette.ink) },
            spoken = R.string.controls_open,
        )

    private fun toggleEditing() {
        editing = !editing
        edit.text = ui.string(if (editing) R.string.macros_edit_done else R.string.macros_edit)
        render()
    }

    private fun render() {
        deck.clear()
        picker = null
        val (across, down) = room ?: return
        // Offered against the height the deck is drawn at, so a grid the picker lists is one it can really draw.
        fits = DeckLayout.fitting(tiles.size, across, deckDown(down), ui.dp(Space.S), ui.dp(Space.TOUCH))
        if (editing) picker = gridPicker()
        draw()
    }

    /** Draws the deck from [shown], and under it the picker when editing. Both survive a move or a pick. */
    private fun draw() {
        val (across, down) = room ?: return
        // A stored grid that is no longer on offer (a tile was added, the screen turned) falls back to automatic.
        val columns = settings.deckColumns(DECK).takeIf { stored -> fits.any { it.second == stored } }
        deck.render(
            across,
            deckDown(down),
            shown.map { tiles[it] },
            labels = true,
            columns,
            onMove = if (editing) ::moved else null,
        ) { tile ->
            onOpen(Target.entries[tile.tag])
        }
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
        settings.setDeckOrder(DECK, shown.map { keys[it] })
        draw()
        // The grid was rebuilt, so the screen reader lost its place: put it back on the tile that moved.
        deck.focus(tiles[shown[to]].tag)
    }

    /** The height the deck is drawn at: half the room while editing, so the picker below is not a scroll away. */
    private fun deckDown(down: Int) = if (editing) down / 2 else down

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

    /** The page's room for the grid: the window less the bars, the nav bar and the page's own margins. */
    private fun measureRoom(): Pair<Int, Int>? {
        val insets = view.rootWindowInsets ?: return null
        val bars = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
        val across = view.width - bars.left - bars.right - 2 * ui.dp(Space.PAGE)
        val down = view.height - bars.top - bars.bottom - bar.height - ui.dp(Space.XL)
        return if (across > 0 && down > 0) across to down else null
    }
}
