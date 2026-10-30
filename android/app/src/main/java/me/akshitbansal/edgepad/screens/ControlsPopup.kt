package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space

/** Which deck this is, to [Settings]: the order is kept per deck. It is the id the Controls screen used. */
private const val DECK = "controls"

/** Tiles per row, and the height of a row in dp. The popup is meant to stay small. */
private const val PER_ROW = 3
private const val ROW_DP = 96f

/** The widest the popup grows, in dp: about a phone's portrait width, so a landscape one is not stretched. */
private const val MAX_WIDTH_DP = 360f

/**
 * What the phone can drive besides the surface itself, as a small modal under the surface's top buttons: the
 * keyboard, the gamepad, the macros and, when the laptop says it can, a capture and an annotation. A tile
 * closes the popup and opens that control; a tap outside or Back closes it.
 *
 * The tiles are a [Deck], so they keep drag-to-reorder and the order stored under [DECK]. A tile carries a
 * [Target] in its tag and nothing about where it is drawn, so a reordered deck opens the same screens.
 *
 * Capture is left out, rather than shown and disabled, when it is false: a tile that opens a page of dead
 * buttons explains nothing that a missing tile does not. Annotate is left out the same way until the laptop
 * has named the feature and its screen size, since the pen pad is shaped to that screen.
 */
class ControlsPopup(
    private val ui: Ui,
    private val settings: Settings,
    private val onOpen: (Target) -> Unit,
) {
    /** The controls a tile can open. The order here is the order of a deck nobody has arranged. */
    enum class Target(
        val label: Int,
        val icon: StencilIcon.Kind,
    ) {
        KEYBOARD(R.string.controls_keyboard, StencilIcon.Kind.KEYBOARD),
        GAMEPAD(R.string.controls_gamepad, StencilIcon.Kind.GAMEPAD),
        MACROS(R.string.controls_macros, StencilIcon.Kind.MACROS),
        CAPTURE(R.string.controls_capture, StencilIcon.Kind.CAPTURE),
        ANNOTATE(R.string.controls_annotate, StencilIcon.Kind.ANNOTATE),
    }

    private var dialog: AlertDialog? = null
    private var across = 0
    private lateinit var deck: Deck

    /** The tiles in the order they are drawn, as indexes into the tiles shown, and the stored-order key of each. */
    private var shown: List<Int> = emptyList()
    private var keys: List<String> = emptyList()
    private var tiles: List<DeckTile> = emptyList()

    /** The deck sees every touch before its tiles do, so a held tile keeps the finger once it leaves its cell. */
    private val holder =
        object : LinearLayout(ui.context) {
            override fun dispatchTouchEvent(event: MotionEvent): Boolean =
                deck.touch(event) || super.dispatchTouchEvent(event)
        }

    val isOpen: Boolean get() = dialog?.isShowing == true

    /** Opens the popup over [owner], which closes it if it leaves the window. */
    fun show(
        owner: View,
        capture: Boolean,
        annotate: Boolean,
    ) {
        dismiss()
        val insets = owner.rootWindowInsets
        val bars = insets?.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
        val left = bars?.left ?: 0
        val right = bars?.right ?: 0
        val top = bars?.top ?: 0
        val margin = ui.dp(Space.PAGE)
        val width = minOf(owner.width - left - right - 2 * margin, ui.dp(MAX_WIDTH_DP))
        val pad = ui.dp(Space.L)
        across = width - 2 * pad
        holder.orientation = LinearLayout.VERTICAL
        holder.setPadding(pad, pad, pad, pad)
        deck = Deck(ui, holder)
        draw(capture, annotate)
        val shownDialog =
            AlertDialog
                .Builder(ui.context)
                .setView(holder)
                .openOver(owner)
        dialog = shownDialog
        shownDialog.window?.apply {
            // The window's own title is what a screen reader announces when it opens.
            setTitle(ui.string(R.string.controls_title))
            setBackgroundDrawable(ui.rounded(ui.palette.card, Space.CARD_RADIUS))
            setGravity(Gravity.TOP or Gravity.CENTER_HORIZONTAL)
            setLayout(width, ViewGroup.LayoutParams.WRAP_CONTENT)
            // Just under the top buttons, which sit a touch target below the system bar.
            attributes = attributes.apply { y = top + ui.dp(Space.TOUCH) + ui.dp(Space.L) }
        }
        holder.requestFocus()
    }

    /** The laptop named its features after the popup opened: the tiles are drawn again from them. */
    fun refresh(
        capture: Boolean,
        annotate: Boolean,
    ) {
        if (isOpen) draw(capture, annotate)
    }

    fun dismiss() {
        dialog?.dismiss()
        dialog = null
    }

    private fun draw(
        capture: Boolean,
        annotate: Boolean,
    ) {
        tiles =
            Target.entries
                .filter {
                    when (it) {
                        Target.CAPTURE -> capture
                        Target.ANNOTATE -> annotate
                        else -> true
                    }
                }.map(::tile)
        keys = tiles.map { it.key }
        shown = DeckLayout.order(settings.deckOrder(DECK), keys)
        drawShown()
    }

    private fun drawShown() {
        val rows = (tiles.size + PER_ROW - 1) / PER_ROW
        val down = rows * ui.dp(ROW_DP) + (rows - 1) * ui.dp(Space.S)
        deck.render(
            across,
            down,
            shown.map { tiles[it] },
            labels = true,
            onMove = ::moved,
            onTap = { tile ->
                dismiss()
                onOpen(Target.entries[tile.tag])
            },
            rows = rows,
        )
    }

    /** The tile at display place [from] was dropped on [to]. Saved on every move, so leaving loses nothing. */
    private fun moved(
        from: Int,
        to: Int,
    ) {
        shown = DeckLayout.move(shown, from, to)
        settings.setDeckOrder(DECK, shown.map { keys[it] })
        drawShown()
    }

    private fun tile(target: Target) =
        DeckTile(
            key = target.name.lowercase(),
            label = ui.string(target.label),
            tag = target.ordinal,
            picture = StencilIcon(target.icon, ui.palette.ink, ui.palette.accent),
            spoken = R.string.controls_open,
        )
}
