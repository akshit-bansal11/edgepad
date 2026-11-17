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
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Switch
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

/** Tiles per row, and the height of a row in dp. The popup is meant to stay small. */
private const val PER_ROW = 3
private const val ROW_DP = 96f

/** The widest the popup grows upright, in dp: about a phone's portrait width. */
private const val MAX_WIDTH_DP = 360f

/** Sideways the tiles go in one row, so the popup is short enough to show whole: this wide at most, in dp. */
private const val MAX_WIDTH_ONE_ROW_DP = 760f

/** The tag of the "Send a file" tile, past every [ControlsPopup.Target] ordinal. */
private const val SEND_TAG = 1000

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
 */
class ControlsPopup(
    private val ui: Ui,
    private val settings: Settings,
    /** AND-A-16: whether the surface hides its dials now, and the switch that changes it. */
    private val dialsHidden: () -> Boolean,
    private val onHideDials: (Boolean) -> Unit,
    /** TEXT 24: why the laptop cannot offer a feature right now, by [Feature.token]. Empty from an older laptop. */
    private val reasons: () -> Map<String, UnavailableReason> = { emptyMap() },
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
    }

    private var dialog: AlertDialog? = null
    private var across = 0

    /** Sideways: every tile in one row (set by [show]). */
    private var oneRow = false
    private lateinit var deck: Deck

    /** The "Sleep when this ends" row and, under it, its choices or the armed line. Rebuilt by [drawEnd]. */
    private val endBox = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
    private var endChoices = false

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
        oneRow = owner.width > owner.height
        val widest = ui.dp(if (oneRow) MAX_WIDTH_ONE_ROW_DP else MAX_WIDTH_DP)
        val width = minOf(owner.width - left - right - 2 * margin, widest)
        val pad = ui.dp(Space.L)
        across = width - 2 * pad
        holder.orientation = LinearLayout.VERTICAL
        holder.setPadding(pad, pad, pad, pad)
        deck = Deck(ui, holder)
        draw(capture, annotate)
        // AND-A-29: the move gesture is told the first few times, under the tiles.
        val body = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        body.addView(holder)
        (endBox.parent as? ViewGroup)?.removeView(endBox)
        endBox.setPadding(pad, 0, pad, 0)
        endChoices = false
        drawEnd()
        body.addView(endBox)
        body.addView(
            ui.toggle(ui.string(R.string.controls_hide_dials), dialsHidden(), onHideDials).apply {
                setPadding(pad, 0, pad, 0)
            },
        )
        // Issue #56. The surface reads the setting again when the popup closes and hands the focus back.
        body.addView(
            ui.toggle(ui.string(R.string.controls_blackout), settings.blackout) { settings.blackout = it }.apply {
                setPadding(pad, 0, pad, 0)
            },
        )
        if (hasGyroscope()) {
            body.addView(
                ui.toggle(ui.string(R.string.controls_air_mouse), settings.airMouse) { settings.airMouse = it }.apply {
                    setPadding(pad, 0, pad, 0)
                },
            )
        } else {
            val none = ui.secondary(ui.string(R.string.air_no_sensor), Type.SMALL, ui.palette.dim)
            none.setPadding(pad, 0, pad, pad)
            body.addView(none)
        }
        body.addView(floatingToggle().apply { setPadding(pad, 0, pad, 0) })
        val floatingNote = ui.secondary(ui.string(R.string.floating_caption), Type.SMALL, ui.palette.dim)
        floatingNote.setPadding(pad, 0, pad, pad)
        body.addView(floatingNote)
        if (settings.controlsHintShown < HINT_TIMES) {
            settings.controlsHintShown += 1
            val note = ui.secondary(ui.string(R.string.controls_move_hint), Type.SMALL, ui.palette.dim)
            note.setPadding(pad, 0, pad, pad)
            body.addView(note)
        }
        // Whatever is below the tiles stays reachable on a short screen.
        val scroll =
            ScrollView(ui.context).apply {
                isVerticalFadingEdgeEnabled = true
                setFadingEdgeLength(ui.dp(FADE_EDGE_DP))
                addView(body)
            }
        val shownDialog =
            AlertDialog
                .Builder(ui.context)
                .setView(scroll)
                .openOver(owner)
        dialog = shownDialog
        shownDialog.window?.apply {
            // The window's own title is what a screen reader announces when it opens.
            setTitle(ui.string(R.string.controls_title))
            setBackgroundDrawable(ui.rounded(ui.palette.popover, Space.CARD_RADIUS))
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
        if (isOpen) {
            draw(capture, annotate)
            drawEnd()
        }
    }

    /** The laptop's armed state (TEXT 25) or its features changed: the end row follows. */
    fun refreshEnd() {
        if (isOpen) drawEnd()
    }

    /**
     * AU4: one row that opens two choices, or, while the laptop has one armed, the line saying so and Cancel. The
     * choices are plain text buttons, so nothing here is told by colour alone. They fade in over [Motion.REVEAL] from
     * the row; the tiles above stay instant.
     */
    private fun drawEnd() {
        endBox.removeAllViews()
        if (!endOffered()) {
            endBox.visibility = View.GONE
            return
        }
        endBox.visibility = View.VISIBLE
        val armed = endArmed()
        if (armed != EndActionToken.NONE) {
            val line = if (armed == EndActionToken.SLEEP) R.string.end_armed_sleep else R.string.end_armed_shutdown
            endBox.addView(ui.secondary(ui.string(line), Type.BODY, ui.palette.ink))
            endBox.addView(ui.button(ui.string(R.string.end_cancel), Ui.Style.OUTLINED) { onEnd(ActionId.END_CANCEL) })
            return
        }
        val timeline = hasTimeline()
        val why = if (timeline) null else ui.string(R.string.end_no_timeline)
        val row =
            ui.link(ui.string(R.string.end_action_title), why, {
                endChoices = !endChoices
                drawEnd()
            })
        // Dimmed but still tappable: the summary under the title says why there is nothing to wait for.
        if (!timeline) row.alpha = Ui.DISABLED_ALPHA
        endBox.addView(row)
        if (endChoices && timeline) {
            val choices = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
            choices.addView(ui.button(ui.string(R.string.end_sleep), Ui.Style.OUTLINED) { onEnd(ActionId.END_SLEEP) })
            choices.addView(
                ui.button(ui.string(R.string.end_shutdown), Ui.Style.OUTLINED) { onEnd(ActionId.END_SHUTDOWN) },
            )
            choices.alpha = 0f
            endBox.addView(choices)
            choices
                .animate()
                .alpha(1f)
                .setDuration(Motion.REVEAL)
                .start()
        }
    }

    private fun hasGyroscope(): Boolean =
        ui.context.getSystemService(SensorManager::class.java)?.getDefaultSensor(Sensor.TYPE_GYROSCOPE) != null

    /**
     * The floating dials need a connected laptop, Stay connected (the service keeps the process) and the
     * permission to draw over other apps. Missing one, the switch stays off and says which; the permission is
     * asked for from this tap only.
     */
    private fun floatingToggle(): Switch {
        lateinit var row: Switch
        row =
            ui.toggle(ui.string(R.string.controls_floating), settings.floatingDials) { on ->
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
                    row.isChecked = false
                    if (refusal == R.string.floating_permission) askOverlay()
                    Toast.makeText(ui.context, refusal, Toast.LENGTH_LONG).show()
                }
            }
        return row
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
        tiles = Target.entries.map { tile(it, available(it, capture, annotate)) } + sendTile()
        keys = tiles.map { it.key }
        shown = DeckLayout.order(settings.deckOrder(DECK), keys)
        drawShown()
    }

    private fun drawShown() {
        val rows = if (oneRow) 1 else (tiles.size + PER_ROW - 1) / PER_ROW
        val down = rows * ui.dp(ROW_DP) + (rows - 1) * ui.dp(Space.S)
        deck.render(
            across,
            down,
            shown.map { tiles[it] },
            labels = true,
            onMove = ::moved,
            onTap = { tile ->
                if (tile.note != null) {
                    // AND-A-15: the tile is there, dimmed, and says why it does nothing.
                    Toast.makeText(ui.context, tile.note, Toast.LENGTH_SHORT).show()
                } else if (tile.tag == SEND_TAG) {
                    dismiss()
                    onSendFile()
                } else {
                    dismiss()
                    onOpen(Target.entries[tile.tag])
                }
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

    private fun available(
        target: Target,
        capture: Boolean,
        annotate: Boolean,
    ): Boolean =
        when (target) {
            Target.CAPTURE -> capture
            Target.ANNOTATE -> annotate
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
            note = if (available) null else ui.string(reasonText(reasons()[featureOf(target)?.token])),
        )

    // Not a [Target]: those open MainActivity's screens, and a send opens the system picker.
    private fun sendTile(): DeckTile =
        DeckTile(
            key = "send",
            label = ui.string(R.string.xfer_send),
            tag = SEND_TAG,
            picture = StencilIcon(StencilIcon.Kind.CAPTURE, ui.palette.ink, ui.palette.accent),
            spoken = R.string.controls_open,
            note = if (laptopConnected()) null else ui.string(R.string.xfer_no_laptop),
        )

    private fun featureOf(target: Target): Feature? =
        when (target) {
            Target.CAPTURE -> Feature.CAPTURE
            Target.ANNOTATE -> Feature.ANNOTATE
            else -> null
        }
}
