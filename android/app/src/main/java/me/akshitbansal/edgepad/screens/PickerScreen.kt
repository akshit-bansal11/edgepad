package me.akshitbansal.edgepad.screens

import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.LaptopRow
import me.akshitbansal.edgepad.link.RowStatus
import me.akshitbansal.edgepad.link.Transport

/**
 * The laptops: paired over Bluetooth, found on the Wi-Fi, found over USB tethering, or added by address, one row each
 * however many ways a laptop was seen. Tapping one connects to it, opens the controls when it is connected, or raises
 * the prompt it is waiting on; under each is the rest of what can be done to it. The active one shows its live round
 * trip. Refresh, Bluetooth settings and Settings sit in the nav bar, and the mark stands beside the large title.
 *
 * The list is filtered to computers, because headsets, watches and mice are paired too and none of them runs
 * the laptop app. Bluetooth's idea of a device's class is the device's own claim, though, and a laptop that
 * misreports it would be missing with no way to reach it, so [onShowAll] lifts the filter.
 */
class PickerScreen(
    private val ui: Ui,
    private val onTap: (Device) -> Unit,
    private val onVia: (Device, Transport) -> Unit,
    private val onMakeActive: (Device) -> Unit,
    private val onDisconnect: (Device) -> Unit,
    private val onForget: (Device) -> Unit,
    onOpenControls: () -> Unit,
    onBluetoothSettings: () -> Unit,
    onSettings: () -> Unit,
    private val onRefresh: () -> Unit,
    private val onShowAll: (Boolean) -> Unit,
    private val onAddAddress: () -> Unit,
    private val onUsbHelp: () -> Unit,
) {
    /** One row. [active] says it is the laptop the screens and every frame go to. */
    class Device(
        val row: LaptopRow,
        val active: Boolean,
    )

    private var devices: List<Device> = emptyList()
    private var canOpen = false
    private var autoConnect = true
    private var hidden = 0
    private var showingAll = false
    private var messageAction: (() -> Unit)? = null
    private var rtt = Double.NaN
    private var rttTag: TextView? = null

    private val refresh = ui.icon(Glyph.Shape.REFRESH, ui.string(R.string.refresh)) { spinAndRefresh() }
    private lateinit var list: LinearLayout
    private lateinit var legend: TextView
    private lateinit var sweepLine: TextView
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
                    footnote(ui.string(R.string.devices_multi_hint))
                    sweepLine = footnote("")
                    filter = add(ui.button("", Ui.Style.QUIET) { onShowAll(!showingAll) }, Space.S)
                    add(ui.button(ui.string(R.string.add_by_address), Ui.Style.QUIET, onAddAddress), Space.S)
                    add(ui.button(ui.string(R.string.usb_tethering), Ui.Style.QUIET, onUsbHelp), Space.S)
                    message = body("", Space.L).apply { visibility = View.GONE }
                    messageButton =
                        add(ui.button("", Ui.Style.QUIET) { messageAction?.invoke() }).apply {
                            visibility = View.GONE
                        }
                },
                {
                    // Sideways, the button lines up with the top of the card beside it: under a copy of that
                    // card's section header, held in place but not drawn, so it is exactly as tall at any size.
                    if (ui.landscape) {
                        add(ui.section(ui.string(R.string.paired_laptops))).visibility = View.INVISIBLE
                    }
                    val top = if (ui.landscape) 0f else Space.XL
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
     * [devices] is what the list shows, and [canOpen] says the active laptop is connected. [hidden] counts the
     * paired devices the computer filter kept out of it, and [showingAll] says the filter is off; between them
     * they decide what the filter button offers.
     */
    fun showDevices(
        devices: List<Device>,
        canOpen: Boolean,
        autoConnect: Boolean,
        hidden: Int,
        showingAll: Boolean,
    ) {
        this.devices = devices
        this.canOpen = canOpen
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

    /** One line under the list about what the last network sweep saw; empty hides it. */
    fun setStatus(text: CharSequence) {
        sweepLine.text = text
        sweepLine.visibility = if (text.isEmpty()) View.GONE else View.VISIBLE
    }

    /** The active laptop's median round trip in milliseconds; NaN before the first answer. */
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
        val remembers = autoConnect && devices.any { it.row.saved?.autoConnect == true }
        legend.text = ui.string(if (remembers) R.string.devices_legend else R.string.devices_legend_off)
        filter.visibility = if (showingAll || hidden > 0) View.VISIBLE else View.GONE
        filter.text =
            if (showingAll) {
                ui.string(R.string.show_laptops_only)
            } else {
                ui.context.resources.getQuantityString(R.plurals.show_all_paired, hidden, hidden)
            }
        actionHint.visibility = if (canOpen) View.GONE else View.VISIBLE
        ui.enable(action, canOpen)
    }

    private fun row(device: Device): View {
        val status = device.row.status
        val isConnected = status == RowStatus.CONNECTED
        val live = status !in setOf(RowStatus.NONE, RowStatus.REMEMBERED, RowStatus.FAILED)
        val sub =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                if (live) {
                    val dot = Pulse(ui.context, ui.palette.accent).apply { pulsing = !isConnected }
                    addView(dot, LinearLayout.LayoutParams(ui.dp(PULSE_DP), ui.dp(PULSE_DP)))
                }
                addView(
                    ui.secondary(
                        statusText(device.row),
                        Type.SMALL,
                        if (live) ui.palette.accent else ui.palette.dim,
                    ),
                )
            }
        val timed = isConnected && device.active
        val tag = ui.secondary(if (timed) rttText() else "", Type.SMALL, ui.palette.dim)
        if (timed) rttTag = tag
        val end =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                device.row.via?.let { addView(transportChip(it)) }
                val wrap = ViewGroup.LayoutParams.WRAP_CONTENT
                addView(tag, LinearLayout.LayoutParams(wrap, wrap).apply { marginStart = ui.dp(Space.S) })
            }
        val line = ui.laptop(device.row.name, sub, isConnected, end).apply { ui.tappable(this) { onTap(device) } }
        return LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            addView(line)
            actions(device)?.let { addView(it) }
        }
    }

    private fun statusText(row: LaptopRow): String =
        when (row.status) {
            RowStatus.NONE -> row.detail
            RowStatus.REMEMBERED -> ui.string(R.string.device_remembered)
            RowStatus.CONNECTING -> ui.string(R.string.device_connecting)
            RowStatus.AWAITING -> ui.string(R.string.status_accept_on_laptop)
            RowStatus.ASKING -> ui.string(R.string.device_asking)
            RowStatus.RECONNECTING -> ui.string(R.string.device_reconnecting)
            RowStatus.CONNECTED -> ui.string(R.string.device_connected_via, row.via?.let(::transportName).orEmpty())
            RowStatus.FAILED -> ui.string(R.string.device_failed)
        }

    /**
     * The line under a row: Connect, the Active badge or Make active, any other transport to connect over, Disconnect
     * and Forget, as far as each applies. It scrolls sideways on a narrow phone; null when nothing applies.
     */
    private fun actions(device: Device): View? {
        val row = device.row
        val laptop = row.laptop
        val line = LinearLayout(ui.context).apply { gravity = Gravity.CENTER_VERTICAL }

        fun add(view: View) {
            val wrap = ViewGroup.LayoutParams.WRAP_CONTENT
            line.addView(view, LinearLayout.LayoutParams(wrap, wrap).apply { marginEnd = ui.dp(Space.S) })
        }

        // The one way to connect that says so; a tap on the row does the same. Not while something is going on already.
        if (row.status in setOf(RowStatus.NONE, RowStatus.REMEMBERED, RowStatus.FAILED)) {
            row.via?.let { via -> add(ui.chip(ui.string(R.string.device_connect)) { onVia(device, via) }) }
        }
        if (laptop?.connected == true && device.active) {
            add(ui.secondary(ui.string(R.string.device_active), Type.SMALL, ui.palette.accent))
        } else if (laptop?.connected == true) {
            add(ui.chip(ui.string(R.string.device_make_active)) { onMakeActive(device) })
        }
        for (transport in row.reach - setOfNotNull(row.via)) {
            val name = transportName(transport)
            add(
                ui.chip(name) { onVia(device, transport) }.apply {
                    contentDescription = ui.string(R.string.connect_via, name)
                },
            )
        }
        if (laptop != null && !laptop.idle) {
            add(ui.chip(ui.string(R.string.device_disconnect)) { onDisconnect(device) })
        }
        if (laptop != null || row.saved != null) {
            add(
                ui.chip(ui.string(R.string.forget), danger = true) {
                    // Over the list and not this chip: a rescan rebuilds the rows, and the question would go with it.
                    ui.confirm(
                        list,
                        ui.string(R.string.forget_laptop_title, row.name),
                        ui.string(R.string.forget_laptop_body),
                        ui.string(R.string.forget),
                    ) { onForget(device) }
                },
            )
        }
        if (line.childCount == 0) return null
        return HorizontalScrollView(ui.context).apply {
            isHorizontalScrollBarEnabled = false
            setPadding(ui.dp(Space.L), 0, ui.dp(Space.L), ui.dp(Space.XS))
            addView(line)
        }
    }

    /** The small label naming how a row is reached. */
    private fun transportChip(transport: Transport): View =
        ui.secondary(transportName(transport), Type.SMALL, ui.palette.ink).apply {
            background = ui.rounded(ui.palette.faint, CHIP_RADIUS_DP)
            val across = ui.dp(Space.S)
            setPadding(across, ui.dp(Space.XS), across, ui.dp(Space.XS))
        }

    private fun transportName(transport: Transport): String = ui.string(transport.label)

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
        const val MARK_DP = 34f
        const val CHIP_RADIUS_DP = 8f

        /** The dot is drawn 6 dp across; the rest of its box is room for the ring it sends out while connecting. */
        const val PULSE_DP = 14f
        const val FULL_TURN = 360f
        const val SPIN_MS = 600L
    }
}

/** The name of how a laptop is reached, as a string resource. */
internal val Transport.label: Int
    get() =
        when (this) {
            Transport.BLUETOOTH -> R.string.transport_bluetooth
            Transport.WIFI -> R.string.transport_wifi
            Transport.USB -> R.string.transport_usb
        }
