package me.akshitbansal.edgepad.screens

import android.graphics.drawable.Drawable
import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings

/** Which deck this is, to [Settings]: the order and the grid are kept per deck. */
private const val DECK = "controls"

/**
 * What the phone can drive besides the surface itself, as a [DeckPage] of tiles: the keyboard, the gamepad
 * and, when the laptop says it can, a capture and an annotation. The surface keeps a single button for all of it,
 * so the row of buttons at the top stays the size it is however many of these there are.
 *
 * A tile carries a [Target] in its tag and nothing about where it is drawn, so a reordered deck opens the
 * same screens.
 *
 * Capture is left out, rather than shown and disabled, when [capture] is false: a tile that opens a page of
 * dead buttons explains nothing that a missing tile does not. Annotate is left out the same way until the
 * laptop has named the feature and its screen size, since the pen pad is shaped to that screen.
 */
class ControlsScreen(
    private val ui: Ui,
    settings: Settings,
    capture: Boolean,
    annotate: Boolean,
    onOpen: (Target) -> Unit,
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
        ANNOTATE(R.string.controls_annotate, R.drawable.ic_pen),
    }

    private val page =
        DeckPage(
            ui,
            settings,
            DECK,
            title = ui.string(R.string.controls_title),
            backLabel = ui.string(R.string.back_to_surface),
            onBack = onBack,
            labels = true,
        ) { tile -> onOpen(Target.entries[tile.tag]) }

    val view: View = page.view

    init {
        val shown =
            Target.entries.filter {
                when (it) {
                    Target.CAPTURE -> capture
                    Target.ANNOTATE -> annotate
                    else -> true
                }
            }
        page.show(shown.map(::tile))
    }

    private fun tile(target: Target) =
        DeckTile(
            key = target.name.lowercase(),
            label = ui.string(target.label),
            tag = target.ordinal,
            picture = picture(target),
            spoken = R.string.controls_open,
        )

    /** The target's Lucide icon in the ink colour, its own copy so tinting it leaves the shared drawable alone. */
    private fun picture(target: Target): Drawable? {
        val icon = ui.context.getDrawable(target.icon)?.mutate()
        icon?.setTint(ui.palette.ink)
        return icon
    }
}
