package me.akshitbansal.edgepad.screens

import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/**
 * The paired laptops. Tapping one connects to it, or opens the controls when it is already connected; the
 * connected one shows its live round trip. Refresh, Bluetooth settings and Settings sit in the nav bar, and
 * the mark stands beside the large title.
 *
 * The list is filtered to computers, because headsets, watches and mice are paired too and none of them runs
 * the laptop app. Bluetooth's idea of a device's class is the device's own claim, though, and a laptop that
 * misreports it would be missing with no way to reach it, so [onShowAll] lifts the filter.
 */
class PickerScreen(
    private val ui: Ui,
    private val onConnect: (String) -> Unit,
    private val onOpenControls: () -> Unit,
    onBluetoothSettings: () -> Unit,
    onSettings: () -> Unit,
    private val onRefresh: () -> Unit,
    private val onShowAll: (Boolean) -> Unit,
) {
    class Device(
        val address: String,
        val name: String,
    )

    private var devices: List<Device> = emptyList()
    private var remembered: String? = null
    private var connected: String? = null
    private var connecting: String? = null
    private var autoConnect = true
    private var hidden = 0
    private var showingAll = false
    private var messageAction: (() -> Unit)? = null
    private var rtt = Double.NaN
    private var rttTag: TextView? = null

    private val refresh = ui.icon(Glyph.Shape.REFRESH, ui.string(R.string.refresh)) { spinAndRefresh() }
    private lateinit var list: LinearLayout
    private lateinit var legend: TextView
    private lateinit var message: TextView
    private lateinit var messageButton: TextView
    private lateinit var filter: TextView
    private lateinit var action: TextView
    private lateinit var actionHint: TextView

    val view: View =
        ui.page(
            ui.bar(
                ui.string(R.string.pairing_title),
                null,
                refresh,
                ui.icon(Glyph.Shape.BLUETOOTH, ui.string(R.string.open_bluetooth_settings), onBluetoothSettings),
                ui.icon(Glyph.Shape.GEAR, ui.string(R.string.settings_title), onSettings),
            ),
        ) {
            add(title())
            columns(
                {
                    section(ui.string(R.string.paired_laptops))
                    list = card {}
                    legend = footnote("")
                    filter = add(ui.button("", Ui.Style.QUIET) { onShowAll(!showingAll) }, Space.S)
                    message = body("", Space.L).apply { visibility = View.GONE }
                    messageButton =
                        add(ui.button("", Ui.Style.QUIET) { messageAction?.invoke() }).apply {
                            visibility = View.GONE
                        }
                },
                {
                    // Sideways, the button lines up with the top of the card beside it, under the section header.
                    val top = if (ui.landscape) BUTTON_TOP_SIDEWAYS_DP else Space.XL
                    action = add(ui.button(ui.string(R.string.open_controls), Ui.Style.FILLED, onOpenControls), top)
                    // A greyed-out button says it cannot be pressed and not why; this says why.
                    actionHint = footnote(ui.string(R.string.open_controls_hint))
                },
            )
        }

    init {
        render()
    }

    /**
     * [devices] is what the list shows. [hidden] counts the paired devices the computer filter kept out of it,
     * and [showingAll] says the filter is off; between them they decide what the filter button offers.
     */
    fun showDevices(
        devices: List<Device>,
        remembered: String?,
        connected: String?,
        autoConnect: Boolean,
        hidden: Int,
        showingAll: Boolean,
    ) {
        this.devices = devices
        this.remembered = remembered
        this.connected = connected
        this.autoConnect = autoConnect
        this.hidden = hidden
        this.showingAll = showingAll
        render()
    }

    /**
     * A reason shown under the list: Bluetooth off, nothing paired, why the last attempt failed. Empty hides
     * it. [actionLabel] and [onAction] add the one thing that gets the user past it, where there is one, so
     * a message never names a problem and leaves them to find the way out alone.
     */
    fun setMessage(
        text: CharSequence,
        actionLabel: CharSequence? = null,
        onAction: (() -> Unit)? = null,
    ) {
        message.text = text
        message.visibility = if (text.isEmpty()) View.GONE else View.VISIBLE
        messageAction = onAction
        messageButton.text = actionLabel
        val offered = text.isNotEmpty() && actionLabel != null && onAction != null
        messageButton.visibility = if (offered) View.VISIBLE else View.GONE
    }

    /** The address a connection is being made to, or null when none is. */
    fun setConnecting(address: String?) {
        connecting = address
        render()
    }

    /** The connected laptop's median round trip in milliseconds; NaN before the first answer. */
    fun setRtt(ms: Double) {
        rtt = ms
        rttTag?.text = rttText()
    }

    /** The large title with the mark beside it, as the top of the app's first screen. */
    private fun title(): View =
        LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            setPadding(ui.dp(Space.XS), 0, 0, ui.dp(Space.S))
            addView(
                MarkView(ui.context),
                LinearLayout.LayoutParams(ui.dp(MARK_DP), ui.dp(MARK_DP)).apply { marginEnd = ui.dp(Space.M) },
            )
            addView(
                ui
                    .text(
                        ui.string(R.string.pairing_title),
                        Type.LARGE_TITLE,
                        ui.palette.ink,
                        Type.TRACKING_TIGHT,
                        Type.black,
                    ).apply { isAccessibilityHeading = true },
            )
        }

    private fun render() {
        list.removeAllViews()
        rttTag = null
        devices.forEachIndexed { i, device ->
            if (i > 0) list.addView(ui.hairline(), ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(Space.HAIR))
            list.addView(row(device))
        }
        val any = devices.isNotEmpty()
        list.visibility = if (any) View.VISIBLE else View.GONE
        legend.visibility = if (any) View.VISIBLE else View.GONE
        val remembers = autoConnect && devices.any { it.address == remembered }
        legend.text = ui.string(if (remembers) R.string.devices_legend else R.string.devices_legend_off)
        filter.visibility = if (showingAll || hidden > 0) View.VISIBLE else View.GONE
        filter.text =
            if (showingAll) {
                ui.string(R.string.show_laptops_only)
            } else {
                ui.context.resources.getQuantityString(R.plurals.show_all_paired, hidden, hidden)
            }
        actionHint.visibility = if (connected != null) View.GONE else View.VISIBLE
        val ready = connected != null
        action.isEnabled = ready
        action.alpha = if (ready) 1f else DISABLED_ALPHA
    }

    private fun row(device: Device): View {
        val isConnected = device.address == connected
        val isConnecting = device.address == connecting
        val live = isConnected || isConnecting
        val status =
            when {
                isConnected -> R.string.device_connected
                isConnecting -> R.string.device_connecting
                device.address == remembered -> R.string.device_remembered
                else -> null
            }
        val tile =
            FrameLayout(ui.context).apply {
                background = ui.rounded(if (isConnected) ui.palette.accent else ui.palette.faint, TILE_RADIUS_DP)
                addView(Glyph(ui.context, Glyph.Shape.LAPTOP, if (isConnected) ui.palette.onAccent else ui.palette.dim))
            }
        val sub =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                setPadding(0, ui.dp(SUB_GAP_DP), 0, 0)
                if (live) {
                    val dot =
                        Pulse(ui.context, ui.palette.accent, ui.palette.accent).apply {
                            lit = true
                            pulsing = isConnecting
                        }
                    addView(dot, LinearLayout.LayoutParams(ui.dp(PULSE_DP), ui.dp(PULSE_DP)))
                }
                addView(
                    ui.mono(
                        status?.let { ui.string(it) } ?: device.address,
                        Type.SMALL,
                        if (live) ui.palette.accent else ui.palette.dim,
                    ),
                )
            }
        val text =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(0, ui.dp(Space.M), 0, ui.dp(Space.M))
                addView(ui.text(device.name, Type.LEAD, ui.palette.ink, face = Type.bold))
                addView(sub)
            }
        val start =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(
                    tile,
                    LinearLayout.LayoutParams(ui.dp(TILE_DP), ui.dp(TILE_DP)).apply { marginEnd = ui.dp(Space.M) },
                )
                addView(text, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            }
        val tag = ui.mono(if (isConnected) rttText() else "", Type.SMALL, ui.palette.dim)
        if (isConnected) rttTag = tag
        return ui.row(start, tag).apply {
            minimumHeight = ui.dp(ROW_DP)
            ui.tappable(this) { if (isConnected) onOpenControls() else onConnect(device.address) }
        }
    }

    private fun rttText(): String = if (rtt.isNaN()) "" else ui.string(R.string.device_rtt, rtt)

    /** A quarter-second turn of the arrow, so a list that did not change still shows it was read again. */
    private fun spinAndRefresh() {
        refresh
            .animate()
            .rotationBy(FULL_TURN)
            .setDuration(SPIN_MS)
            .withEndAction { refresh.rotation = 0f }
            .start()
        onRefresh()
    }

    private companion object {
        const val ROW_DP = 64f
        const val MARK_DP = 34f
        const val TILE_DP = 36f
        const val TILE_RADIUS_DP = 10f

        /** The dot is drawn 6 dp across; the rest of its box is room for the ring it sends out while connecting. */
        const val PULSE_DP = 14f
        const val SUB_GAP_DP = 2f

        /** The section header's height, so a sideways button starts level with the card beside it. */
        const val BUTTON_TOP_SIDEWAYS_DP = 56f
        const val DISABLED_ALPHA = 0.3f
        const val FULL_TURN = 360f
        const val SPIN_MS = 600L
    }
}
