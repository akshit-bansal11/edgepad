package me.akshitbansal.edgepad.screens

import android.content.res.ColorStateList
import android.graphics.BitmapFactory
import android.graphics.drawable.BitmapDrawable
import android.view.Gravity
import android.view.View
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
internal const val MACRO_DECK = "macros"

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
 * A page shows [Settings.macrosPerPage] macros, spread over the whole page so a wall-mounted phone has no
 * empty room, and the laptop's rest continue on further pages that a finger slides to. Every page has the same
 * grid, so a tile keeps its size from page to page and a short last page leaves cells empty at the end.
 *
 * What the laptop sends later is handed in rather than rebuilding the screen: [setMacros] redraws the grid
 * inside the page, and [iconArrived] decodes one picture and dresses one button. Until 3.2 each of them
 * built the whole screen again, which threw away where TalkBack was and decoded every picture in the grid
 * once for each picture that arrived.
 *
 * Arranging the grid (pressing and holding a tile to drag it, the gear for [MacroSettingsScreen]) is
 * [DeckPage]'s. It is all phone-side drawing: a tile's [DeckTile.tag] is the laptop's slot index, so a tap
 * hands back the slot and never the tile's place on screen, and the order can be anything without a macro
 * running the wrong thing.
 */
class MacroScreen(
    private val ui: Ui,
    settings: Settings,
    names: List<String>,
    private val icon: (Int) -> ByteArray?,
    labels: Boolean,
    onRun: (index: Int) -> Unit,
    onSettings: () -> Unit,
    onBack: () -> Unit,
) {
    private var slots: List<IndexedValue<String>> = emptyList()

    private val page =
        DeckPage(
            ui,
            settings,
            MACRO_DECK,
            title = ui.string(R.string.macros_title),
            backLabel = ui.string(R.string.back_to_controls),
            onBack = onBack,
            labels = labels,
            empty = ::emptyState,
            onSettings = onSettings,
            perPage = settings.macrosPerPage,
            rows = settings.macroRows.takeIf { it != Settings.AUTO_MACRO_ROWS },
            // The laptop's slot, never the tile's place in the deck: the order on screen is only a drawing.
            onTap = { tile -> onRun(tile.tag) },
        )

    val view: View = page.view

    init {
        setMacros(names)
    }

    /** A new list from the laptop. Its icons are gone with it, since a slot number now means another macro. */
    fun setMacros(names: List<String>) {
        // A slot with no name is a hole in the laptop's list, not the end of it: it keeps the index its
        // neighbours are counted from and simply is not drawn.
        slots = names.withIndex().filter { it.value.isNotEmpty() }
        val keys = DeckLayout.keys(slots.map { it.value })
        page.show(
            slots.mapIndexed { at, slot ->
                DeckTile(key = keys[at], label = slot.value, tag = slot.index, picture = decode(icon(slot.index)))
            },
        )
    }

    /** The laptop finished sending [slot]'s picture: that one is decoded and that one button dressed in it. */
    fun iconArrived(slot: Int) {
        if (slots.none { it.index == slot }) return
        page.setPicture(slot, decode(icon(slot)))
    }

    /** The empty state, drawn into the page's holder. */
    private fun emptyState(holder: LinearLayout) {
        Column(ui, holder).apply {
            add(emptyTile(), width = ui.dp(EMPTY_TILE_DP), height = ui.dp(EMPTY_TILE_DP))
            headline(ui.string(R.string.macros_empty_title), Type.TITLE, Space.L).gravity = Gravity.CENTER
            body(ui.string(R.string.macros_empty_body), Space.S).gravity = Gravity.CENTER
        }
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
        // Decoded at full size and scaled once, by the tile, to its final bounds with filtering on. A full
        // grid draws the picture smaller than it was sent, and a mip chain keeps that from aliasing.
        bitmap.setHasMipMap(true)
        return BitmapDrawable(ui.context.resources, bitmap).apply { isFilterBitmap = true }
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
