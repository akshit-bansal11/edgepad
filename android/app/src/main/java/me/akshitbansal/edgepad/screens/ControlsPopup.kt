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
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Toast
import me.akshitbansal.edgepad.Motion
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.GitHost
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.UnavailableReason

/** Which deck this is, to [Settings]: the order is kept per deck. It is the id the Controls screen used. */
private const val DECK = "controls"

/** The edit hint is shown during the first edit session only. */
private const val EDIT_TIP = "controls_edit"

/**
 * One Medium tile, in dp: 128 by 112, a 40 dp picture 8 dp above a 15 sp label. [ControlsPlan.scale] gives the
 * other sizes, and the columns are as many as the popup's width holds.
 */
private const val TILE_WIDTH_DP = 128f
private const val TILE_HEIGHT_DP = 112f
private const val TILE_ICON_DP = 40f
private const val TILE_LABEL_SP = 15f
private const val TILE_ICON_GAP_DP = 8f

/** How large the popup's tiles are drawn; [zoom] scales the Medium size, which is the 5.1 one. */
enum class ControlsIconSize(
    val zoom: Float,
) {
    SMALL(0.8f),
    MEDIUM(1f),
    LARGE(1.2f),
}

/** The popup's sizes worked out apart from the views, so a JVM test holds them. */
object ControlsPlan {
    const val DEFAULT_PER_PAGE = 9
    val PER_PAGE = 2..12

    /** The popup takes about this much of the screen's width and at most this much of its height. */
    private const val WIDTH_PERCENT = 92
    private const val HEIGHT_PERCENT = 80

    /** One tile's measures: the tile in dp, the label in sp. */
    class Scale(
        val tileWidth: Float,
        val tileHeight: Float,
        val icon: Float,
        val labelSp: Float,
    )

    fun scale(size: ControlsIconSize): Scale =
        Scale(
            TILE_WIDTH_DP * size.zoom,
            TILE_HEIGHT_DP * size.zoom,
            TILE_ICON_DP * size.zoom,
            TILE_LABEL_SP * size.zoom,
        )

    /** The tallest the popup may be on a screen [screenHeight] tall. */
    fun maxHeight(screenHeight: Int): Int = screenHeight * HEIGHT_PERCENT / 100

    /**
     * The popup for [count] tiles of [tileWidth] by [tileHeight], [gap] apart, [perPage] to a page, inside [padSide],
     * [padTop] and [padBottom], on a screen [screenWidth] by [screenHeight]. As many columns as the width holds; a
     * page is planned for its own tiles, or for [perPage] of them when there is more than one page, so a tile keeps
     * its size from page to page. [DeckLayout.FixedPlan.scrolls] when the rows do not fit the most the popup may be.
     */
    fun plan(
        count: Int,
        perPage: Int,
        screenWidth: Int,
        screenHeight: Int,
        tileWidth: Int,
        tileHeight: Int,
        gap: Int,
        padSide: Int,
        padTop: Int,
        padBottom: Int,
    ): DeckLayout.FixedPlan {
        val width = screenWidth * WIDTH_PERCENT / 100
        val columns = ((width - 2 * padSide + gap) / (tileWidth + gap)).coerceAtLeast(1)
        val cells = minOf(count, perPage)
        val rows = if (cells <= 0) 0 else (cells + columns - 1) / columns
        val tile = ((width - 2 * padSide - (columns - 1) * gap) / columns).coerceAtLeast(1)
        val grid = if (rows == 0) 0 else rows * tileHeight + (rows - 1) * gap
        val wanted = padTop + grid + padBottom
        val room = maxHeight(screenHeight)
        return DeckLayout.FixedPlan(columns, rows, width, minOf(wanted, room), grid, tile, wanted > room)
    }
}

/** The header row, which is all of the popup's top: the heading at its start and the pencil at its end. */
private const val HEADER_DP = 48f
private const val HEADING_SP = 17f

/** The pencil: a 48 dp touch target with a 16 dp glyph in its centre. */
private const val PENCIL_DP = 16f

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
 * gamepad, the macros, Stats and, with Developer mode on, Developer, and, when the laptop says it can, a capture and
 * an annotation. A tile closes the popup and opens that control; a tap outside or Back closes it. It holds the tiles
 * and one header row, the heading Controls with the pencil at its end, nothing else (owner, 2026-11-20): the switches
 * that were chips here live on the Dials, Surface and System settings pages.
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
    /** The features the laptop named in TEXT 9: the Developer tile is dimmed without any of its pages. */
    private val features: () -> Set<Feature> = { emptySet() },
    /** Whether Developer mode is on: the Developer tile is drawn only then. */
    private val developer: () -> Boolean = { false },
    /** AU6: whether a laptop is connected now; without one the "Send a file" tile says so instead of opening. */
    private val laptopConnected: () -> Boolean = { true },
    /** AU6: "Send a file" was tapped on a connected laptop; the callback's owner opens the system picker. */
    private val onSendFile: () -> Unit = {},
    /** The gear was tapped: the popup is closed and its settings page is the owner's to open. */
    private val onSettings: () -> Unit = {},
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

        /** Stats: the System and Network pages. Its key stays "system", so a stored place is kept. */
        SYSTEM(R.string.sys_title, StencilIcon.Kind.SYSTEM),

        /** Developer: the AI and Git pages. Drawn only with Developer mode on. */
        DEVELOPER(R.string.dev_title, StencilIcon.Kind.DEVELOPER),

        /** Power: lock, sleep, hibernate, restart and shut down. Always drawn, for the page names what it can do. */
        POWER(R.string.controls_power, StencilIcon.Kind.POWER),
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

    /** The popup's heading, at the start of the header row. */
    private val heading =
        ui.text(ui.string(R.string.controls_title), HEADING_SP, ui.palette.ink, face = Type.bold).apply {
            isAccessibilityHeading = true
        }

    /**
     * The popup's only control, at the end of the header row. A pencil turns edit mode on; in edit mode it is a check
     * in the accent. The touch target is 48 dp, the glyph 16 dp in its centre.
     */
    private val editButton =
        ImageView(ui.context).apply {
            scaleType = ImageView.ScaleType.FIT_CENTER
            val side = (ui.dp(Space.TOUCH) - ui.dp(PENCIL_DP)) / 2
            setPadding(side, side, side, side)
            ui.tappable(this, Space.TOUCH / 2) { setEditing(!editing) }
        }

    /** The popup's settings, a gear on its own 48 dp touch target, left of the pencil. */
    private val gear =
        ui.icon(Glyph.Shape.GEAR, ui.string(R.string.controls_settings_open), ui.palette.ink) {
            dismiss()
            onSettings()
        }

    /** The page showing once the tiles fall on more than one. */
    private var page = 0

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
        page = 0
        if (editing) settings.setTipSeen(EDIT_TIP)
        editing = false
        hidden = settings.deckHidden(DECK)
        holder.orientation = LinearLayout.VERTICAL
        deck = Deck(ui, holder)
        tileNote.setPadding(0, 0, 0, ui.dp(Space.S))
        // The views are reused from the last time the popup opened, so they are let go of before they are re-parented.
        for (view in listOf(tileNote, scroll, gear, editButton, heading)) (view.parent as? ViewGroup)?.removeView(view)
        scroll.removeAllViews()
        val match = ViewGroup.LayoutParams.MATCH_PARENT
        scroll.addView(holder, ViewGroup.LayoutParams(match, ViewGroup.LayoutParams.WRAP_CONTENT))
        // One row: the heading at the start, the pencil at the end, with no padding above or beside the tiles' own.
        val header =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                setPaddingRelative(pad, 0, 0, 0)
                addView(heading, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
                addView(gear, LinearLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH)))
                addView(editButton, LinearLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH)))
            }
        val body =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                clipChildren = false
                clipToPadding = false
                setPadding(pad, 0, pad, pad)
                addView(tileNote)
                addView(scroll, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(Space.TOUCH)))
            }
        val root =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(header, LinearLayout.LayoutParams(match, ui.dp(HEADER_DP)))
                addView(body)
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

    /** The popup for [count] tiles on this screen: [ControlsPlan] for the chosen size and the tiles to a page. */
    private fun planFor(count: Int): DeckLayout.FixedPlan {
        val cell = ControlsPlan.scale(settings.controlsIconSize)
        return ControlsPlan.plan(
            count,
            settings.controlsPerPage,
            screenWidth,
            screenHeight,
            ui.dp(cell.tileWidth),
            ui.dp(cell.tileHeight),
            ui.dp(Space.S),
            ui.dp(Space.L),
            ui.dp(HEADER_DP),
            ui.dp(Space.L),
        )
    }

    /**
     * Sizes the grid's box for [plan], less what the note takes, and sets [down] to the grid's own height. The box
     * is shorter than the grid only on a screen too short for the rows, where it scrolls, and never under one touch
     * target. Run before every draw of the tiles, since the note comes and goes.
     */
    private fun fit(
        plan: DeckLayout.FixedPlan,
        dots: Int,
    ) {
        val wide = View.MeasureSpec.makeMeasureSpec(across, View.MeasureSpec.EXACTLY)
        val free = View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED)
        val taken =
            if (tileNote.visibility == View.GONE) {
                0
            } else {
                tileNote.measure(wide, free)
                tileNote.measuredHeight
            }
        down = plan.grid + dots
        val room = ControlsPlan.maxHeight(screenHeight) - ui.dp(HEADER_DP) - ui.dp(Space.L) - taken
        val params = scroll.layoutParams ?: return
        val height = minOf(down, maxOf(room, ui.dp(Space.TOUCH)))
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
        val decor = window.decorView
        // The owner leaving the window closes the dialog under the animation: stop it rather than finish it.
        decor.addOnAttachStateChangeListener(
            object : View.OnAttachStateChangeListener {
                override fun onViewAttachedToWindow(v: View) = Unit

                override fun onViewDetachedFromWindow(v: View) = v.animate().cancel()
            },
        )
        decor
            .animate()
            .alpha(0f)
            .scaleX(CLOSE_SCALE)
            .scaleY(CLOSE_SCALE)
            .setDuration(Motion.REVEAL)
            .withEndAction { if (closing.isShowing && decor.isAttachedToWindow) closing.dismiss() }
            .start()
    }

    private fun draw(
        capture: Boolean,
        annotate: Boolean,
    ) {
        // Every tile shows; one the laptop cannot offer is dimmed with a reason. Send a file sits before System.
        tiles =
            buildList {
                for (target in Target.entries) {
                    if (!tileDrawn(target, developer())) continue
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
        val pages = DeckLayout.pages(visible.size, settings.controlsPerPage)
        val dots = if (pages.size > 1) ui.dp(Space.XL) else 0
        fit(plan, dots)
        if (visible.isEmpty() || down <= 0) {
            deck.clear()
            return
        }
        val cell = ControlsPlan.scale(settings.controlsIconSize)
        val fixed =
            FixedCell(
                plan.columns,
                ui.dp(cell.tileHeight),
                ui.dp(cell.icon),
                cell.labelSp,
                ui.dp(TILE_ICON_GAP_DP),
            )
        val labels = settings.controlsLabels
        val drawnTiles = visible.map { drawn(tiles[it]) }
        if (pages.size <= 1) {
            deck.render(
                across,
                down,
                drawnTiles,
                labels = labels,
                onMove = ::moved,
                onTap = ::tapped,
                opens = !editing,
                fixed = fixed,
            )
            return
        }
        // A tile moves within its own page, as on the macro screen; its place is offset by where the page starts.
        deck.clear()
        page =
            drawPager(ui, holder, across, pages, page, { page = it }, plan.grid) { pageHolder, range ->
                pageHolder.deck.render(
                    across,
                    plan.grid,
                    drawnTiles.slice(range),
                    labels = labels,
                    onMove = { from, to -> moved(range.first + from, range.first + to) },
                    onTap = ::tapped,
                    opens = !editing,
                    fixed = fixed,
                )
            }
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

            // Any of the developer integrations.
            Target.DEVELOPER -> features().any { it in DEVELOPER_PAGES }

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
            Target.DEVELOPER -> githubNote(reason) ?: dashNote(reason[Feature.CLAUDE.token])
            else -> reasonText(null)
        }
    }

    // GitHub refused (a sign-in from before the read-only App, or one that could not be renewed): say what to do.
    private fun githubNote(reason: Map<String, UnavailableReason>): Int? =
        if (reason[Feature.GITHUB.token] == UnavailableReason.NO_PERMISSION) {
            R.string.unavailable_github_resign
        } else {
            null
        }

    private fun dashNote(reason: UnavailableReason?): Int =
        if (reason == null) R.string.controls_dash_off else reasonText(reason)
}

/** Whether a tile is drawn at all: every one is, except Developer, which needs Developer mode. */
internal fun tileDrawn(
    target: ControlsPopup.Target,
    developerMode: Boolean,
): Boolean = target != ControlsPopup.Target.DEVELOPER || developerMode

/** What the Developer tile needs one of: a developer integration that is on and usable. */
private val DEVELOPER_PAGES = setOf(Feature.CLAUDE, Feature.CODEX) + GitHost.entries.map { it.feature }
