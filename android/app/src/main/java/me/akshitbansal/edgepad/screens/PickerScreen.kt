package me.akshitbansal.edgepad.screens

import android.animation.ValueAnimator
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.animation.PathInterpolator
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/**
 * Step two: pick the paired laptop and connect. The band under the title scans while a connection is
 * being made. Once connected, the main button opens the controls instead; Settings is always one tap away.
 */
class PickerScreen(
    private val ui: Ui,
    private val onConnect: (String) -> Unit,
    private val onOpenControls: () -> Unit,
    private val onBluetoothSettings: () -> Unit,
    private val onSettings: () -> Unit,
) {
    class Device(
        val address: String,
        val name: String,
    )

    private var devices: List<Device> = emptyList()
    private var remembered: String? = null
    private var connected: String? = null
    private var selected: String? = null
    private var connectingTo: String? = null
    private var scan: ValueAnimator? = null

    private val list = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
    private val scanLine =
        View(ui.context).apply {
            setBackgroundColor(ui.palette.ink)
            alpha = SCAN_ALPHA
            visibility = View.INVISIBLE
        }
    private val band =
        ui
            .mono(
                "",
                Type.SMALL,
                ui.palette.dim,
                BAND_TRACKING,
            ).apply { gravity = Gravity.CENTER_VERTICAL }
    private lateinit var count: TextView
    private lateinit var message: TextView
    private lateinit var action: TextView

    val view: View =
        ui.page {
            mono(ui.string(R.string.pairing_step))
            headline(ui.string(R.string.pairing_title), Type.TITLE, Space.M)
            hairline(Space.XXL)
            val frame =
                FrameLayout(ui.context).apply {
                    addView(
                        band,
                        FrameLayout.LayoutParams(
                            ViewGroup.LayoutParams.MATCH_PARENT,
                            ViewGroup.LayoutParams.MATCH_PARENT,
                        ),
                    )
                    addView(scanLine, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(Space.HAIR)))
                }
            add(frame, height = ui.dp(BAND_DP))
            hairline()
            count = mono("", topDp = Space.XXL)
            add(list, Space.M)
            message = body("", Space.L)
            grow()
            mono(
                ui.string(R.string.pairing_footnote),
                Type.SMALL,
                topDp = Space.XL,
            ).setLineSpacing(0f, FOOTNOTE_LEADING)
            action = add(ui.button(ui.string(R.string.connect), Ui.Style.FILLED) { act() }, Space.L)
            add(ui.button(ui.string(R.string.open_bluetooth_settings), Ui.Style.QUIET, onBluetoothSettings), Space.M)
            add(ui.link(ui.string(R.string.settings_link), onSettings), Space.S)
        }

    init {
        view.addOnAttachStateChangeListener(
            object : View.OnAttachStateChangeListener {
                override fun onViewAttachedToWindow(v: View) = updateScan()

                override fun onViewDetachedFromWindow(v: View) = stopScan()
            },
        )
        render()
    }

    fun showDevices(
        devices: List<Device>,
        remembered: String?,
        connected: String?,
    ) {
        this.devices = devices
        this.remembered = remembered
        this.connected = connected
        if (devices.none { it.address == selected }) {
            selected =
                connected ?: remembered?.takeIf { r -> devices.any { it.address == r } }
                    ?: devices.firstOrNull()?.address
        }
        render()
    }

    /** A reason shown under the list: Bluetooth off, nothing paired, why the last attempt failed. Empty hides it. */
    fun setMessage(text: CharSequence) {
        message.text = text
        message.visibility = if (text.isEmpty()) View.GONE else View.VISIBLE
    }

    /** The laptop a connection is being made to, or null when none is. */
    fun setConnecting(name: String?) {
        connectingTo = name
        render()
        updateScan()
    }

    private fun act() {
        val address = selected ?: return
        if (address == connected) onOpenControls() else onConnect(address)
    }

    private fun render() {
        list.removeAllViews()
        count.text = ui.string(R.string.pairing_count, devices.size)
        for (device in devices) {
            list.addView(ui.hairline(), ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(Space.HAIR))
            list.addView(row(device))
        }
        if (devices.isNotEmpty()) list.addView(ui.hairline(), ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(Space.HAIR))
        val controls = selected != null && selected == connected
        action.text = ui.string(if (controls) R.string.open_controls else R.string.connect)
        action.isEnabled = selected != null && connectingTo == null
        action.alpha = if (action.isEnabled) 1f else DISABLED_ALPHA
        band.text =
            connectingTo?.let { ui.string(R.string.pairing_connecting, it.uppercase()) }
                ?: ui.string(R.string.pairing_band)
    }

    private fun row(device: Device): View {
        val status =
            when (device.address) {
                connected -> R.string.device_connected
                remembered -> R.string.device_remembered
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
        val chosen = device.address == selected
        val end =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                val dot = Glyph(ui.context, Glyph.Shape.DOT, ui.palette.ink)
                dot.visibility = if (chosen) View.VISIBLE else View.INVISIBLE
                addView(dot, LinearLayout.LayoutParams(ui.dp(DOT_BOX_DP), ui.dp(DOT_BOX_DP)))
                addView(
                    Glyph(ui.context, Glyph.Shape.CHEVRON_RIGHT, ui.palette.ink),
                    LinearLayout.LayoutParams(ui.dp(Space.XL), ui.dp(Space.XL)),
                )
            }
        return ui.row(ui.stack(device.name, sub, Type.LEAD), end).apply {
            isSelected = chosen
            minimumHeight = ui.dp(ROW_DP)
            ui.tappable(this) {
                selected = device.address
                render()
            }
        }
    }

    private fun updateScan() {
        if (connectingTo == null || !view.isAttachedToWindow) {
            stopScan()
            return
        }
        scanLine.visibility = View.VISIBLE
        // With animations removed in system settings, the line simply sits still.
        if (scan != null || !ValueAnimator.areAnimatorsEnabled()) return
        val travel = (ui.dp(BAND_DP) - ui.dp(Space.HAIR)).toFloat()
        scan =
            ValueAnimator.ofFloat(0f, 1f).apply {
                duration = SCAN_MS
                repeatCount = ValueAnimator.INFINITE
                interpolator = PathInterpolator(EASE_X1, EASE_Y1, EASE_X2, EASE_Y2)
                addUpdateListener { scanLine.translationY = it.animatedFraction * travel }
                start()
            }
    }

    private fun stopScan() {
        scan?.cancel()
        scan = null
        scanLine.visibility = View.INVISIBLE
    }

    private companion object {
        const val BAND_DP = 64f
        const val ROW_DP = 64f
        const val DOT_BOX_DP = 14f
        const val BAND_TRACKING = 0.12f
        const val FOOTNOTE_LEADING = 1.6f
        const val SCAN_ALPHA = 0.45f
        const val SCAN_MS = 1900L
        const val DISABLED_ALPHA = 0.4f
        const val EASE_X1 = 0.4f
        const val EASE_Y1 = 0f
        const val EASE_X2 = 0.2f
        const val EASE_Y2 = 1f
    }
}
