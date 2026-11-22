package me.akshitbansal.edgepad.screens

import android.animation.ValueAnimator
import android.app.AlertDialog
import android.content.res.ColorStateList
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.view.WindowManager
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Toast
import me.akshitbansal.edgepad.Motion
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.UnavailableReason

/** Which deck this is, to [Settings]: the order is kept per deck. It is the id the Controls screen used. */
private const val DECK = "controls"

/** The edit hint is shown during the first edit session only. */
private const val EDIT_TIP = "controls_edit"

/** One tile, in dp: a fixed size, whatever the screen, with a 32 dp picture 8 dp above a 13 sp label. */
private const val TILE_WIDTH_DP = 104f
private const val TILE_HEIGHT_DP = 88f
private const val TILE_ICON_DP = 32f
private const val TILE_LABEL_SP = 13f
private const val TILE_ICON_GAP_DP = 8f

/** The popup's top padding: room for the pencil in the corner, which is all it has in place of a heading. */
private const val TOP_PAD_DP = 30f

/** The pencil: a 48 dp touch target with a 16 dp glyph 8 dp in from the top and right edges. */
private const val PENCIL_DP = 16f
private const val PENCIL_INSET_DP = 8f

/** What a closing popup shrinks to, matching what [Carry.reveal] grows from. */
private const val CLOSE_SCALE = 0.85f

/** The tag of the "Send a file" tile, past every [ControlsPopup.Target] ordinal. */
private const val SEND_TAG = 1000

/**
 * The string that says why a dimmed tile does nothing: the laptop's own reason when it gave one (TEXT 24), and the
 * older "needs a newer Edgepad" line when it said nothing, which is what a laptop that predates TEXT 24 does.
 */
internal fun reasonText(reason: UnavailableReason?): Int =
    when (reason) {
        UnavailableReason.UNSUPPORTED -> R.string.unavailable_unsupported
        UnavailableReason.OFF -> R.string.unavailable_off
        UnavailableReason.NO_TOOL -> R.string.unavailable_no_tool
        UnavailableReason.NO_PERMISSION -> R.string.unavailable_no_permission
        null -> R.string.controls_needs_newer
    }

/**
 * What the phone can drive besides the surface itself, as a small modal centred on the screen: the keyboard, the
 * gamepad, the macros, Simple stats and, when the laptop says it can, a capture and an annotation. A tile closes the
 * popup and opens that control; a tap outside or Back closes it. It holds the tiles and one pencil at its top right
 * corner, nothing else (owner, 2026-11-20): the switches that were chips here live on the Dials, Surface and System
 * settings pages.
 *
 * The tiles are a [Deck], so they keep drag-to-reorder and the order stored under [DECK]. A tile carries a
 * [Target] in its tag and nothing about where it is drawn, so a reordered deck opens the same screens.
 *
 * Capture and Annotate are always drawn. Until the laptop names the feature (and, for Annotate, its screen size,
 * since the pen pad is shaped to that screen) the tile is dimmed and a tap says why instead of opening it.
 *
 * Edit (the pencil) draws every tile, hidden ones dimmed and named Hidden; a tap hides or shows one and a drag
 * moves it. Hidden keys are stored per deck beside the order, and a closed popup always opens out of edit mode.
 * Stored keys for the old AI and GitHub tiles are simply never matched by [DeckLayout.order] again.
 */
class ControlsPopup(
    private val ui: Ui,
    private val settings: Settings,
    /** TEXT 24: why the laptop cannot offer a feature right now, by [Feature.token]. Empty from an older laptop. */
    private val reasons: () -> Map<String, UnavailableReason> = { emptyMap() },
    /** The features the laptop named in TEXT 9: the Simple stats tile is dimmed without any of its pages. */
    private val features: () -> Set<Feature> = { emptySet() },
    /** AU6: whether a laptop is connected now; without one the "Send a file" tile says so instead of opening. */
    private val laptopConnected: () -> Boolean = { true },
    /** AU6: "Send a file" was tapped on a connected laptop; the callback's owner opens the system picker. */
    private val onSendFile: () -> Unit = {},
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
        PRESENTER(R.string.controls_presenter, StencilIcon.Kind.PRESENTER),
        CAPTURE(R.string.controls_capture, StencilIcon.Kind.CAPTURE),
        ANNOTATE(R.string.controls_annotate, StencilIcon.Kind.ANNOTATE),
        CLIPBOARD(R.string.controls_clipboard, StencilIcon.Kind.CLIPBOARD),

        /** Simple stats: the System, AI and Git pages. Its key stays "system", so a stored place is kept. */
        SYSTEM(R.string.controls_simple_stats, StencilIcon.Kind.SYSTEM),
    }

    private var dialog: AlertDialog? = null

    /**
     * The room the tiles have, in pixels, worked out by [fit] from [DeckLayout.fixedPlan]. Not left to a weight: the
     * dialog sizes its content to fit, so a weighted grid got no height at all and no tile was drawn.
     */
    private var across = 0
    private var down = 0
    private var popupWidth = 0

    /** The screen between the bars, and whether it is wider than tall. */
    private var screenWidth = 0
    private var screenHeight = 0
    private var sideways = false
    private lateinit var deck: Deck

    /** The tiles in the order they are drawn, as indexes into the tiles shown, and the stored-order key of each. */
    private var shown: List<Int> = emptyList()
    private var keys: List<String> = emptyList()
    private var tiles: List<DeckTile> = emptyList()

    /** Edit mode, and the keys of the tiles hidden from the deck. */
    private var editing = false
    private var hidden: Set<String> = emptySet()

    /** The entries of [shown] drawn now: all of them while [editing], the ones not [hidden] otherwise. */
    private var visible: List<Int> = emptyList()

    /**
     * The popup's only control, in its top right corner. A pencil turns edit mode on; in edit mode it is a check in
     * the accent. The touch target is 48 dp, the glyph 16 dp, set 8 dp in from the top and right edges.
     */
    private val editButton =
        ImageView(ui.context).apply {
            scaleType = ImageView.ScaleType.FIT_CENTER
            val inset = ui.dp(PENCIL_INSET_DP)
            val rest = ui.dp(Space.TOUCH) - ui.dp(PENCIL_DP) - inset
            setPadding(rest, inset, inset, rest)
            ui.tappable(this, Space.TOUCH / 2) { setEditing(!editing) }
        }

    /** At the top of the popup: the one place a note is shown. See [drawShown] for what it says and when. */
    private val tileNote = ui.secondary("", Type.SMALL, ui.palette.dim)

    /** The deck sees every touch before its tiles do, so a held tile keeps the finger once it leaves its cell. */
    private val holder =
        object : LinearLayout(ui.context) {
            override fun dispatchTouchEvent(event: MotionEvent): Boolean =
                deck.touch(event) || super.dispatchTouchEvent(event)
        }

    /** Holds the tiles; it only ever scrolls on a screen too short for the rows. */
    private val scroll =
        ScrollView(ui.context).apply {
            isVerticalScrollBarEnabled = false
            clipChildren = false
            clipToPadding = false
        }

    val isOpen: Boolean get() = dialog?.isShowing == true

    /** Opens the popup over [owner], centred on the screen, which closes it if it leaves the window. */
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
        val barTop = bars?.top ?: 0
        val barBottom = bars?.bottom ?: 0
        sideways = owner.width > owner.height
        screenWidth = owner.width - left - right
        screenHeight = owner.height - barTop - barBottom
        val pad = ui.dp(Space.L)
        popupWidth = planFor(1).width
        across = popupWidth - 2 * pad
        down = 0
        if (editing) settings.setTipSeen(EDIT_TIP)
        editing = false
        hidden = settings.deckHidden(DECK)
        holder.orientation = LinearLayout.VERTICAL
        deck = Deck(ui, holder)
        tileNote.setPadding(0, 0, 0, ui.dp(Space.S))
        // The views are reused from the last time the popup opened, so they are let go of before they are re-parented.
        for (view in listOf(tileNote, scroll, editButton)) (view.parent as? ViewGroup)?.removeView(view)
        scroll.removeAllViews()
        val match = ViewGroup.LayoutParams.MATCH_PARENT
        scroll.addView(holder, ViewGroup.LayoutParams(match, ViewGroup.LayoutParams.WRAP_CONTENT))
        val body =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                clipChildren = false
                clipToPadding = false
                setPadding(pad, ui.dp(TOP_PAD_DP), pad, pad)
                addView(tileNote)
                addView(scroll, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(Space.TOUCH)))
            }
        val root =
            FrameLayout(ui.context).apply {
                addView(body)
                addView(
                    editButton,
                    FrameLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH), Gravity.TOP or Gravity.END),
                )
            }
        draw(capture, annotate)
        val shownDialog =
            AlertDialog
                .Builder(ui.context)
                .setView(root)
                .openOver(owner)
        dialog = shownDialog
        shownDialog.window?.apply {
            // The window's own title is what a screen reader announces when it opens.
            setTitle(ui.string(R.string.controls_title))
            setBackgroundDrawable(ui.rounded(ui.palette.popover, Space.PANEL_RADIUS))
            setGravity(Gravity.CENTER)
            setLayout(popupWidth, ViewGroup.LayoutParams.WRAP_CONTENT)
        }
        holder.requestFocus()
    }

    /** The popup for [count] tiles on this screen: fixed-size tiles, five columns sideways and three upright. */
    private fun planFor(count: Int): DeckLayout.FixedPlan =
        DeckLayout.fixedPlan(
            count,
            sideways,
            screenWidth,
            screenHeight,
            ui.dp(Space.PAGE),
            ui.dp(TILE_WIDTH_DP),
            ui.dp(TILE_HEIGHT_DP),
            ui.dp(Space.S),
            ui.dp(Space.L),
            ui.dp(TOP_PAD_DP),
            ui.dp(Space.L),
        )

    /**
     * Sizes the grid's box for [plan], less what the note takes, and sets [down] to the grid's own height. The box
     * is shorter than the grid only on a screen too short for the rows, where it scrolls, and never under one touch
     * target. Run before every draw of the tiles, since the note comes and goes.
     */
    private fun fit(plan: DeckLayout.FixedPlan) {
        val wide = View.MeasureSpec.makeMeasureSpec(across, View.MeasureSpec.EXACTLY)
        val free = View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED)
        val taken =
            if (tileNote.visibility == View.GONE) {
                0
            } else {
                tileNote.measure(wide, free)
                tileNote.measuredHeight
            }
        down = plan.grid
        val room = screenHeight - 2 * ui.dp(Space.PAGE) - ui.dp(TOP_PAD_DP) - ui.dp(Space.L) - taken
        val params = scroll.layoutParams ?: return
        val height = minOf(plan.grid, maxOf(room, ui.dp(Space.TOUCH)))
        if (params.height != height) {
            params.height = height
            scroll.layoutParams = params
        }
    }

    /** The laptop named its features after the popup opened: the tiles are drawn again from them. */
    fun refresh(
        capture: Boolean,
        annotate: Boolean,
    ) {
        if (isOpen) draw(capture, annotate)
    }

    /** Closes the popup, shrinking it back toward the point it grew from unless animations are off. */
    fun dismiss() {
        val closing = dialog ?: return
        dialog = null
        val window = closing.window
        if (window == null || !closing.isShowing || !ValueAnimator.areAnimatorsEnabled()) {
            closing.dismiss()
            return
        }
        // A tap during the shrink would open a second control.
        val untouchable = WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE
        window.setFlags(untouchable, untouchable)
        window.decorView
            .animate()
            .alpha(0f)
            .scaleX(CLOSE_SCALE)
            .scaleY(CLOSE_SCALE)
            .setDuration(Motion.REVEAL)
            .withEndAction { closing.dismiss() }
            .start()
    }

    private fun draw(
        capture: Boolean,
        annotate: Boolean,
    ) {
        // Every tile shows; one the laptop cannot offer is dimmed with a reason. Send a file sits before Simple stats.
        tiles =
            buildList {
                for (target in Target.entries) {
                    if (target == Target.SYSTEM) add(sendTile())
                    add(tile(target, available(target, capture, annotate)))
                }
            }
        keys = tiles.map { it.key }
        shown = DeckLayout.order(settings.deckOrder(DECK), keys)
        drawShown()
    }

    private fun drawShown() {
        visible = if (editing) shown else shown.filter { keys[it] !in hidden }
        editButton.setImageResource(if (editing) R.drawable.ic_check else R.drawable.ic_tool_pen)
        editButton.imageTintList = ColorStateList.valueOf(if (editing) ui.palette.accent else ui.palette.ink)
        val label = ui.string(if (editing) R.string.editor_done else R.string.controls_edit)
        editButton.contentDescription = label
        editButton.tooltipText = label
        // The one place a note is shown: how edit mode works while editing, or how to bring hidden tiles back.
        val note =
            when {
                editing && !settings.tipSeen(EDIT_TIP) -> R.string.controls_edit_hint
                visible.isEmpty() -> R.string.controls_all_hidden
                else -> null
            }
        tileNote.visibility = if (note == null) View.GONE else View.VISIBLE
        if (note != null) tileNote.text = ui.string(note)
        val plan = planFor(visible.size)
        fit(plan)
        if (visible.isEmpty() || down <= 0) {
            deck.clear()
            return
        }
        deck.render(
            across,
            down,
            visible.map { drawn(tiles[it]) },
            labels = true,
            onMove = ::moved,
            onTap = ::tapped,
            opens = !editing,
            fixed =
                FixedCell(
                    plan.columns,
                    ui.dp(TILE_HEIGHT_DP),
                    ui.dp(TILE_ICON_DP),
                    TILE_LABEL_SP,
                    ui.dp(TILE_ICON_GAP_DP),
                ),
        )
    }

    /** In edit mode a tile's state is whether it is hidden, not whether the laptop offers it. */
    private fun drawn(tile: DeckTile): DeckTile =
        if (!editing) {
            tile
        } else {
            tile.copy(
                note = if (tile.key in hidden) ui.string(R.string.controls_hidden) else null,
                spoken = R.string.controls_toggle_tile,
            )
        }

    private fun tapped(tile: DeckTile) {
        when {
            editing -> {
                hidden = if (tile.key in hidden) hidden - tile.key else hidden + tile.key
                settings.setDeckHidden(DECK, hidden)
                drawShown()
            }

            // AND-A-15: the tile is there, dimmed, and says why it does nothing.
            tile.note != null -> {
                Toast.makeText(ui.context, tile.note, Toast.LENGTH_SHORT).show()
            }

            tile.tag == SEND_TAG -> {
                dismiss()
                onSendFile()
            }

            else -> {
                dismiss()
                onOpen(Target.entries[tile.tag])
            }
        }
    }

    private fun setEditing(on: Boolean) {
        if (!on) settings.setTipSeen(EDIT_TIP)
        editing = on
        drawShown()
    }

    /**
     * The tile at display place [from] was dropped on [to]: places among the tiles drawn, so a hidden tile keeps its
     * place. Saved on every move, so leaving loses nothing.
     */
    private fun moved(
        from: Int,
        to: Int,
    ) {
        shown = DeckLayout.moveShown(shown, visible, from, to)
        settings.setDeckOrder(DECK, shown.map { keys[it] })
        drawShown()
    }

    private fun available(
        target: Target,
        capture: Boolean,
        annotate: Boolean,
    ): Boolean =
        when (target) {
            Target.CAPTURE -> capture

            Target.ANNOTATE -> annotate

            // Any page: the AI and Git pages are named only with Developer mode on.
            Target.SYSTEM -> features().any { it in STATS_PAGES }

            else -> true
        }

    private fun tile(
        target: Target,
        available: Boolean,
    ): DeckTile =
        DeckTile(
            key = target.name.lowercase(),
            label = ui.string(target.label),
            tag = target.ordinal,
            picture = StencilIcon(target.icon, ui.palette.ink, ui.palette.accent),
            spoken = R.string.controls_open,
            note = if (available) null else ui.string(unavailableNote(target)),
        )

    // Not a [Target]: those open MainActivity's screens, and a send opens the system picker.
    private fun sendTile(): DeckTile =
        DeckTile(
            key = "send",
            label = ui.string(R.string.xfer_send),
            tag = SEND_TAG,
            picture = StencilIcon(StencilIcon.Kind.SEND, ui.palette.ink, ui.palette.accent),
            spoken = R.string.controls_open,
            note = if (laptopConnected()) null else ui.string(R.string.xfer_no_laptop),
        )

    // The laptop's own reason when it gave one; the dashboard tile says how to turn it on when it gave none.
    private fun unavailableNote(target: Target): Int {
        val reason = reasons()
        return when (target) {
            Target.CAPTURE -> reasonText(reason[Feature.CAPTURE.token])
            Target.ANNOTATE -> reasonText(reason[Feature.ANNOTATE.token])
            Target.SYSTEM -> dashNote(reason[Feature.STATS.token])
            else -> reasonText(null)
        }
    }

    private fun dashNote(reason: UnavailableReason?): Int =
        if (reason == null) R.string.controls_dash_off else reasonText(reason)
}

/** What the Simple stats tile needs one of: the System page's stats, or a developer integration's page. */
private val STATS_PAGES = setOf(Feature.STATS, Feature.CLAUDE, Feature.CODEX, Feature.GITHUB)
