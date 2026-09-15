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
 * connected one shows its live round trip. Refresh, Bluetooth settings and Settings sit in the title row.
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

    private val list = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
    private val refresh = ui.icon(Glyph.Shape.REFRESH, ui.string(R.string.refresh)) { spinAndRefresh() }
    private lateinit var count: TextView
    private lateinit var legend: View
    private lateinit var legendText: TextView
    private lateinit var message: TextView
    private lateinit var action: TextView

    val view: View =
        FrameLayout(ui.context).apply {
            val bar =
                ui.bar(
                    ui.string(R.string.pairing_title),
                    null,
                    refresh,
                    ui.icon(Glyph.Shape.BLUETOOTH, ui.string(R.string.open_bluetooth_settings), onBluetoothSettings),
                    ui.icon(Glyph.Shape.GEAR, ui.string(R.string.settings_title), onSettings),
                    lead = MarkView(ui.context),
                )
            val page =
                ui.page(bar) {
                    columns(
                        {
                            count = add(ui.section(""))
                            add(list)
                            legend = add(legendRow())
                            message = body("", Space.L).apply { visibility = View.GONE }
                        },
                        {
                            action =
                                add(
                                    ui.button(ui.string(R.string.open_controls), Ui.Style.FILLED, onOpenControls),
                                    Space.L,
                                )
                        },
                    )
                }
            addView(page)
            addView(EdgeRule(ui.context))
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

    private fun render() {
        list.removeAllViews()
        rttTag = null
        count.text = ui.string(R.string.pairing_count, devices.size)
        for (device in devices) {
            list.addView(row(device))
            list.addView(ui.hairline(), ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(Space.HAIR))
        }
        legend.visibility = if (devices.any { it.address == remembered }) View.VISIBLE else View.GONE
        legendText.text = ui.string(if (autoConnect) R.string.devices_legend else R.string.devices_legend_off)
        val ready = connected != null
        action.isEnabled = ready
        action.alpha = if (ready) 1f else DISABLED_ALPHA
    }

    private fun row(device: Device): View {
        val isConnected = device.address == connected
        val isConnecting = device.address == connecting
        val status =
            when {
                isConnected -> R.string.device_connected
                isConnecting -> R.string.device_connecting
                device.address == remembered -> R.string.device_remembered
                else -> null
            }
        val sub =
            if (status ==
                null
            ) {
                device.address
            } else {
                ui.string(R.string.device_sub, device.address, ui.string(status))
            }
        val dot =
            Pulse(ui.context, ui.palette.ink, ui.palette.line).apply {
                lit = isConnected || device.address == remembered
                pulsing = isConnecting
            }
        val start =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(
                    dot,
                    LinearLayout.LayoutParams(ui.dp(PULSE_DP), ui.dp(PULSE_DP)).apply {
                        marginEnd =
                            ui.dp(Space.M)
                    },
                )
                addView(
                    ui.stack(device.name, sub),
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
                )
            }
        val tag = ui.mono(if (isConnected) rttText() else "", Type.MICRO, ui.palette.dim, Type.TRACKING_ROW)
        if (isConnected) rttTag = tag
        return ui.row(start, tag).apply {
            minimumHeight = ui.dp(ROW_DP)
            ui.tappable(this) { if (isConnected) onOpenControls() else onConnect(device.address) }
        }
    }

    private fun legendRow(): View =
        LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            setPadding(0, ui.dp(Space.M), 0, ui.dp(Space.M))
            addView(
                Pulse(ui.context, ui.palette.ink, ui.palette.line).apply { lit = true },
                LinearLayout.LayoutParams(ui.dp(PULSE_DP), ui.dp(PULSE_DP)).apply { marginEnd = ui.dp(Space.S) },
            )
            legendText = ui.mono("", Type.MICRO, ui.palette.dim, Type.TRACKING_ROW)
            addView(legendText)
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
        const val ROW_DP = 60f
        const val PULSE_DP = 18f
        const val DISABLED_ALPHA = 0.3f
        const val FULL_TURN = 360f
        const val SPIN_MS = 600L
    }
}
