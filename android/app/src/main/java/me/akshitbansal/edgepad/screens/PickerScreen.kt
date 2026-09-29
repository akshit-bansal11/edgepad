package me.akshitbansal.edgepad.screens

import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/**
 * The paired laptops. Tapping one connects to it, or opens the controls when it is already connected; the
 * connected one shows its live round trip. Refresh, Bluetooth settings and Settings sit in the nav bar, and
 * the mark stands beside the large title.
 */
class PickerScreen(
    private val ui: Ui,
    private val onConnect: (String) -> Unit,
    private val onOpenControls: () -> Unit,
    onBluetoothSettings: () -> Unit,
    onSettings: () -> Unit,
    private val onRefresh: () -> Unit,
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
    private var rtt = Double.NaN
    private var rttTag: TextView? = null

    private val refresh = ui.icon(Glyph.Shape.REFRESH, ui.string(R.string.refresh)) { spinAndRefresh() }
    private lateinit var list: LinearLayout
    private lateinit var legend: TextView
    private lateinit var message: TextView
    private lateinit var action: TextView

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
                    message = body("", Space.L).apply { visibility = View.GONE }
                },
                {
                    // Sideways, the button lines up with the top of the card beside it: under a copy of that
                    // card's section header, held in place but not drawn, so it is exactly as tall at any size.
                    if (ui.landscape) {
                        add(ui.section(ui.string(R.string.paired_laptops))).visibility = View.INVISIBLE
                    }
                    val top = if (ui.landscape) 0f else Space.XL
                    action = add(ui.button(ui.string(R.string.open_controls), Ui.Style.FILLED, onOpenControls), top)
                },
            )
        }

    init {
        render()
    }

    fun showDevices(
        devices: List<Device>,
        remembered: String?,
        connected: String?,
        autoConnect: Boolean,
    ) {
        this.devices = devices
        this.remembered = remembered
        this.connected = connected
        this.autoConnect = autoConnect
        render()
    }

    /** A reason shown under the list: Bluetooth off, nothing paired, why the last attempt failed. Empty hides it. */
    fun setMessage(text: CharSequence) {
        message.text = text
        message.visibility = if (text.isEmpty()) View.GONE else View.VISIBLE
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
        ui.enable(action, connected != null)
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
        val sub =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                if (live) {
                    val dot = Pulse(ui.context, ui.palette.accent).apply { pulsing = isConnecting }
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
        val tag = ui.mono(if (isConnected) rttText() else "", Type.SMALL, ui.palette.dim)
        if (isConnected) rttTag = tag
        return ui.laptop(device.name, sub, isConnected, tag).apply {
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
        const val MARK_DP = 34f

        /** The dot is drawn 6 dp across; the rest of its box is room for the ring it sends out while connecting. */
        const val PULSE_DP = 14f
        const val FULL_TURN = 360f
        const val SPIN_MS = 600L
    }
}
