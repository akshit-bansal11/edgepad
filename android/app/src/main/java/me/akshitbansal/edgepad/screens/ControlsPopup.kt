package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.content.ActivityNotFoundException
import android.content.Intent
import android.hardware.Sensor
import android.hardware.SensorManager
import android.net.Uri
import android.util.Log
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import android.widget.Toast
import me.akshitbansal.edgepad.Motion
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.EndActionToken
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.UnavailableReason
import android.provider.Settings as SystemSettings

/** Which deck this is, to [Settings]: the order is kept per deck. It is the id the Controls screen used. */
private const val DECK = "controls"

/** The widest the popup grows upright, in dp: about a phone's portrait width. */
private const val MAX_WIDTH_DP = 360f

/** The widest the popup grows sideways, in dp. */
private const val MAX_WIDTH_SIDEWAYS_DP = 760f

/** The tag of the "Send a file" tile, past every [ControlsPopup.Target] ordinal. */
private const val SEND_TAG = 1000

private const val WRAP = ViewGroup.LayoutParams.WRAP_CONTENT

/** AND-A-29: how many openings say that a tile can be held and moved. */
private const val HINT_TIMES = 3

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
 * What the phone can drive besides the surface itself, as a small modal under the surface's top buttons: the
 * keyboard, the gamepad, the macros and, when the laptop says it can, a capture and an annotation. A tile
 * closes the popup and opens that control; a tap outside or Back closes it.
 *
 * The tiles are a [Deck], so they keep drag-to-reorder and the order stored under [DECK]. A tile carries a
 * [Target] in its tag and nothing about where it is drawn, so a reordered deck opens the same screens.
 *
 * Capture and Annotate are always drawn. Until the laptop names the feature (and, for Annotate, its screen size,
 * since the pen pad is shaped to that screen) the tile is dimmed and a tap says why instead of opening it.
 *
 * Edit (owner, 2026-11-20) draws every tile, hidden ones dimmed and named Hidden; a tap hides or shows one and a drag
 * moves it. Hidden keys are stored per deck beside the order, and a closed popup always opens out of edit mode.
 */
class ControlsPopup(
    private val ui: Ui,
    private val settings: Settings,
    /** AND-A-16: whether the surface hides its dials now, and the switch that changes it. */
    private val dialsHidden: () -> Boolean,
    private val onHideDials: (Boolean) -> Unit,
    /** TEXT 24: why the laptop cannot offer a feature right now, by [Feature.token]. Empty from an older laptop. */
    private val reasons: () -> Map<String, UnavailableReason> = { emptyMap() },
    /** The features the laptop named in TEXT 9: the System, AI and GitHub tiles are dimmed without theirs. */
    private val features: () -> Set<Feature> = { emptySet() },
    /** Whether TEXT 9 names [Feature.END_ACTION]: the "when this ends" row is hidden without it. */
    private val endOffered: () -> Boolean = { false },
    /** TEXT 25: what the laptop has armed. */
    private val endArmed: () -> EndActionToken = { EndActionToken.NONE },
    /** Whether the player reports an end time; without one there is nothing to wait for. */
    private val hasTimeline: () -> Boolean = { true },
    /** Sends [ActionId.END_SLEEP], [ActionId.END_SHUTDOWN] or [ActionId.END_CANCEL]. */
    private val onEnd: (ActionId) -> Unit = {},
    /** AU6: whether a laptop is connected now; without one the "Send a file" tile says so instead of opening. */
    private val laptopConnected: () -> Boolean = { true },
    /** AU6: "Send a file" was tapped on a connected laptop; the callback's owner opens the system picker. */
    private val onSendFile: () -> Unit = {},
    /** The Floating dials switch changed: the owner shows or hides them. */
    private val onFloatingChanged: () -> Unit = {},
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
        CLIPBOARD(R.string.controls_clipboard, StencilIcon.Kind.CLIPBOARD),
        PRESENTER(R.string.controls_presenter, StencilIcon.Kind.PRESENTER),
        SYSTEM(R.string.controls_system, StencilIcon.Kind.SYSTEM),
        AI(R.string.controls_ai, StencilIcon.Kind.AI),
        GITHUB(R.string.controls_github, StencilIcon.Kind.GITHUB),
    }

    private var dialog: AlertDialog? = null

    /**
     * The room the tiles have, in pixels, worked out by [fit]: the popup's [room] less the note, the end row and the
     * chips under the tiles. Not left to a weight: the dialog sizes its content to fit, so a weighted grid got no
     * height at all and no tile was drawn.
     */
    private var across = 0
    private var down = 0
    private var popupWidth = 0
    private var room = 0
    private val under = ArrayList<View>()
    private lateinit var deck: Deck

    /** Over the chips: the choices "Sleep when this ends" opens, or the armed line and Cancel. Rebuilt by [drawEnd]. */
    private val endBox = LinearLayout(ui.context).apply { orientation = LinearLayout.HORIZONTAL }
    private var endChoices = false
    private val endChip = ui.chip(ui.string(R.string.end_action_title)) { tappedEnd() }

    /** The tiles in the order they are drawn, as indexes into the tiles shown, and the stored-order key of each. */
    private var shown: List<Int> = emptyList()
    private var keys: List<String> = emptyList()
    private var tiles: List<DeckTile> = emptyList()

    /** Edit mode, and the keys of the tiles hidden from the deck. */
    private var editing = false
    private var hidden: Set<String> = emptySet()

    /** AND-A-29: this opening is one of the first few, which say how to move and hide tiles. */
    private var moveHint = false

    /** The entries of [shown] drawn now: all of them while [editing], the ones not [hidden] otherwise. */
    private var visible: List<Int> = emptyList()

    private val editChip = ui.chip("") { setEditing(!editing) }

    /** Under the tiles: how edit mode works, or, with every tile hidden, how to bring them back. */
    private val tileNote = ui.secondary("", Type.SMALL, ui.palette.dim)

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
        val bottom = bars?.bottom ?: 0
        val margin = ui.dp(Space.PAGE)
        val sideways = owner.width > owner.height
        val widest = ui.dp(if (sideways) MAX_WIDTH_SIDEWAYS_DP else MAX_WIDTH_DP)
        val width = minOf(owner.width - left - right - 2 * margin, widest)
        // Just under the top buttons, which sit a touch target below the system bar, and down to the page margin.
        val offset = top + ui.dp(Space.TOUCH) + ui.dp(Space.L)
        val height = owner.height - offset - bottom - margin
        val pad = ui.dp(Space.L)
        across = width - 2 * pad
        down = 0
        popupWidth = width
        room = height
        editing = false
        hidden = settings.deckHidden(DECK)
        endChoices = false
        // AND-A-29: the first few openings say how to move and hide tiles, under them.
        moveHint = settings.controlsHintShown < HINT_TIMES
        if (moveHint) settings.controlsHintShown += 1
        // Owner, 2026-11-20: the tiles take the room, as on the macro deck; the switches are one row of chips.
        holder.orientation = LinearLayout.VERTICAL
        holder.setPadding(pad, pad, pad, ui.dp(Space.S))
        deck = Deck(ui, holder)
        tileNote.setPadding(pad, 0, pad, ui.dp(Space.S))
        endBox.setPadding(pad, 0, pad, 0)
        val body = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        val chipRow = chips(pad)
        under.clear()
        under += listOf(tileNote, endBox, chipRow)
        body.addView(holder, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0))
        under.forEach { body.addView(it) }
        drawEnd()
        draw(capture, annotate)
        val shownDialog =
            AlertDialog
                .Builder(ui.context)
                .setView(body)
                .openOver(owner)
        dialog = shownDialog
        shownDialog.window?.apply {
            // The window's own title is what a screen reader announces when it opens.
            setTitle(ui.string(R.string.controls_title))
            setBackgroundDrawable(ui.rounded(ui.palette.popover, Space.CARD_RADIUS))
            setGravity(Gravity.TOP or Gravity.CENTER_HORIZONTAL)
            setLayout(width, ViewGroup.LayoutParams.WRAP_CONTENT)
            attributes = attributes.apply { y = offset }
        }
        holder.requestFocus()
    }

    /**
     * Gives the grid what the rows under it leave of [room], measured at the popup's width, and sets [down] to match.
     * Run before every draw of the tiles, since the note and the end row come and go.
     */
    private fun fit() {
        val wide = View.MeasureSpec.makeMeasureSpec(popupWidth, View.MeasureSpec.EXACTLY)
        val free = View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED)
        val taken =
            under.sumOf { view ->
                if (view.visibility == View.GONE) {
                    0
                } else {
                    view.measure(wide, free)
                    view.measuredHeight
                }
            }
        val grid = maxOf(room - taken, 0)
        down = maxOf(grid - holder.paddingTop - holder.paddingBottom, 0)
        val params = holder.layoutParams ?: return
        if (params.height != grid) {
            params.height = grid
            holder.layoutParams = params
        }
    }

    /**
     * The switches, as one row of chips under the tiles that scrolls sideways when it is wider than the popup: Edit,
     * Sleep when this ends, then the on/off ones. A chip that cannot be used here is dimmed and says why when tapped.
     */
    private fun chips(pad: Int): View {
        // Padded inside the row, not the scroll view, which draws its fades inside its own padding (see Ui.page).
        val row =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.HORIZONTAL
                setPadding(pad, 0, pad, pad)
            }
        val gap = ui.dp(Space.S)

        fun add(chip: View) {
            (chip.parent as? ViewGroup)?.removeView(chip)
            row.addView(
                chip,
                LinearLayout.LayoutParams(WRAP, WRAP).apply { if (row.childCount > 0) marginStart = gap },
            )
        }
        add(editChip)
        add(endChip)
        add(ui.toggleChip(ui.string(R.string.controls_hide_dials), dialsHidden()) { _, on -> onHideDials(on) })
        // Issue #56. The surface reads the setting again when the popup closes and hands the focus back.
        add(ui.toggleChip(ui.string(R.string.controls_blackout), settings.blackout) { _, on -> settings.blackout = on })
        add(airChip())
        add(floatingChip())
        return HorizontalScrollView(ui.context).apply {
            isHorizontalScrollBarEnabled = false
            isHorizontalFadingEdgeEnabled = true
            setFadingEdgeLength(ui.dp(FADE_EDGE_DP))
            addView(row)
        }
    }

    /**
     * Owner, 2026-11-18: nothing said how to use the air mouse. Turning it on says it, and a long press says it again;
     * without a gyroscope the chip is dimmed and says that instead.
     */
    private fun airChip(): View {
        val label = ui.string(R.string.controls_air_mouse)
        if (!hasGyroscope()) {
            return ui.chip(label) { toast(R.string.air_no_sensor) }.apply {
                alpha = Ui.DISABLED_ALPHA
                stateDescription = ui.string(R.string.air_no_sensor)
            }
        }
        val chip =
            ui.toggleChip(label, settings.airMouse) { _, on ->
                settings.airMouse = on
                if (on) toast(R.string.air_how)
            }
        chip.tooltipText = ui.string(R.string.air_how)
        return chip
    }

    private fun toast(text: Int) {
        Toast.makeText(ui.context, text, Toast.LENGTH_LONG).show()
    }

    /** The laptop named its features after the popup opened: the tiles are drawn again from them. */
    fun refresh(
        capture: Boolean,
        annotate: Boolean,
    ) {
        if (isOpen) {
            drawEnd()
            draw(capture, annotate)
        }
    }

    /** The laptop's armed state (TEXT 25) or its features changed: the end row follows. */
    fun refreshEnd() {
        if (isOpen) endChanged()
    }

    /**
     * AU4: the "Sleep when this ends" chip opens two choices over the chips, or, while the laptop has one armed, the
     * chip gives way to the line saying so and Cancel. The choices are plain text buttons, so nothing here is told by
     * colour alone. They fade in over [Motion.REVEAL]; the tiles above are planned again for the room left.
     */
    private fun drawEnd() {
        endBox.removeAllViews()
        val offered = endOffered()
        val armed = endArmed()
        endChip.visibility = if (offered && armed == EndActionToken.NONE) View.VISIBLE else View.GONE
        if (!offered) {
            endBox.visibility = View.GONE
            return
        }
        if (armed != EndActionToken.NONE) {
            endBox.visibility = View.VISIBLE
            val line = if (armed == EndActionToken.SLEEP) R.string.end_armed_sleep else R.string.end_armed_shutdown
            endBox.addView(
                ui.secondary(ui.string(line), Type.BODY, ui.palette.ink).apply { gravity = Gravity.CENTER_VERTICAL },
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f),
            )
            endBox.addView(ui.button(ui.string(R.string.end_cancel), Ui.Style.QUIET) { onEnd(ActionId.END_CANCEL) })
            return
        }
        // Dimmed but still tappable: a tap says why there is nothing to wait for.
        val timeline = hasTimeline()
        endChip.alpha = if (timeline) 1f else Ui.DISABLED_ALPHA
        endChip.stateDescription = if (timeline) null else ui.string(R.string.end_no_timeline)
        if (!endChoices || !timeline) {
            endBox.visibility = View.GONE
            return
        }
        endBox.visibility = View.VISIBLE
        val choices = LinearLayout(ui.context).apply { orientation = LinearLayout.HORIZONTAL }
        val half = { LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f) }
        choices.addView(
            ui.button(ui.string(R.string.end_sleep), Ui.Style.OUTLINED) { onEnd(ActionId.END_SLEEP) },
            half(),
        )
        choices.addView(
            ui.button(ui.string(R.string.end_shutdown), Ui.Style.OUTLINED) { onEnd(ActionId.END_SHUTDOWN) },
            half().apply { marginStart = ui.dp(Space.S) },
        )
        choices.alpha = 0f
        endBox.addView(choices, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, WRAP))
        choices
            .animate()
            .alpha(1f)
            .setDuration(Motion.REVEAL)
            .start()
    }

    private fun tappedEnd() {
        if (!hasTimeline()) {
            toast(R.string.end_no_timeline)
            return
        }
        endChoices = !endChoices
        endChanged()
    }

    /** The end row came, went or changed: it takes room from the tiles, which are planned again. */
    private fun endChanged() {
        drawEnd()
        if (tiles.isNotEmpty()) drawShown()
    }

    private fun hasGyroscope(): Boolean =
        ui.context.getSystemService(SensorManager::class.java)?.getDefaultSensor(Sensor.TYPE_GYROSCOPE) != null

    /**
     * The floating dials need a connected laptop, Stay connected (the service keeps the process) and the
     * permission to draw over other apps. Missing one, the chip stays off and says which; the permission is
     * asked for from this tap only. A long press says what the dials are and what they need.
     */
    private fun floatingChip(): View {
        val chip =
            ui.toggleChip(ui.string(R.string.controls_floating), settings.floatingDials) { box, on ->
                val refusal =
                    when {
                        !on -> null
                        !laptopConnected() -> R.string.floating_needs_link
                        !settings.stayConnected -> R.string.floating_needs_stay
                        !SystemSettings.canDrawOverlays(ui.context) -> R.string.floating_permission
                        else -> null
                    }
                if (refusal == null) {
                    settings.floatingDials = on
                    onFloatingChanged()
                } else {
                    box.isChecked = false
                    if (refusal == R.string.floating_permission) askOverlay()
                    toast(refusal)
                }
            }
        chip.tooltipText = ui.string(R.string.floating_caption)
        return chip
    }

    private fun askOverlay() {
        val context = ui.context
        val intent =
            Intent(SystemSettings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:" + context.packageName))
        try {
            context.startActivity(intent)
        } catch (e: ActivityNotFoundException) {
            Log.w("ControlsPopup", "no screen for the overlay permission", e)
        }
    }

    fun dismiss() {
        dialog?.dismiss()
        dialog = null
    }

    private fun draw(
        capture: Boolean,
        annotate: Boolean,
    ) {
        // AI and GitHub stay out of the deck until they are connected on the laptop; the rest show dimmed with a reason.
        val targets = Target.entries.filter { it !in CONNECT_FIRST || available(it, capture, annotate) }
        tiles = targets.map { tile(it, available(it, capture, annotate)) } + sendTile()
        keys = tiles.map { it.key }
        shown = DeckLayout.order(settings.deckOrder(DECK), keys)
        drawShown()
    }

    private fun drawShown() {
        visible = if (editing) shown else shown.filter { keys[it] !in hidden }
        editChip.text = ui.string(if (editing) R.string.editor_done else R.string.controls_edit)
        editChip.setTextColor(if (editing) ui.palette.accent else ui.palette.ink)
        val note =
            when {
                editing -> R.string.controls_edit_hint
                visible.isEmpty() -> R.string.controls_all_hidden
                moveHint -> R.string.controls_move_hint
                else -> null
            }
        tileNote.visibility = if (note == null) View.GONE else View.VISIBLE
        if (note != null) tileNote.text = ui.string(note)
        fit()
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
            Target.SYSTEM -> Feature.STATS in features()
            Target.AI -> Feature.CLAUDE in features() || Feature.CODEX in features()
            Target.GITHUB -> Feature.GITHUB in features()
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

    // The laptop's own reason when it gave one; the dashboard tiles say how to turn them on when it gave none.
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

/** Tiles that appear only once their integration is connected on the laptop. */
private val CONNECT_FIRST = setOf(ControlsPopup.Target.AI, ControlsPopup.Target.GITHUB)
