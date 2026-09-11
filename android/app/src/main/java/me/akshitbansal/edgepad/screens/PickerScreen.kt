package me.akshitbansal.edgepad.screens

import android.bluetooth.BluetoothDevice
import android.content.Context
import android.content.Intent
import android.provider.Settings
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R

/** Pick the paired laptop; once connected, the way into the controls, and always the way into Settings. */
class PickerScreen(
    private val context: Context,
    private val onPick: (BluetoothDevice) -> Unit,
    private val onOpenControls: () -> Unit,
    private val onDisconnect: () -> Unit,
    private val onSettings: () -> Unit,
) {
    private val devices = LinearLayout(context).apply { orientation = LinearLayout.VERTICAL }
    private lateinit var status: TextView
    private val openControls =
        Button(context).apply {
            setText(R.string.open_controls)
            visibility = View.GONE
            setOnClickListener { onOpenControls() }
        }
    private val disconnect =
        Button(context).apply {
            setText(R.string.disconnect)
            visibility = View.GONE
            setOnClickListener { onDisconnect() }
        }

    val view: View =
        Page.build(context) {
            title(R.string.choose_laptop)
            body(R.string.choose_laptop_hint)
            addView(devices)
            status = body("")
            addView(openControls)
            addView(disconnect)
            gap()
            addView(
                Button(context).apply {
                    setText(R.string.open_bluetooth_settings)
                    setOnClickListener { context.startActivity(Intent(Settings.ACTION_BLUETOOTH_SETTINGS)) }
                },
            )
            addView(
                Button(context).apply {
                    setText(R.string.settings)
                    setOnClickListener { onSettings() }
                },
            )
        }

    fun showDevices(
        bonded: List<Pair<BluetoothDevice, String>>,
        remembered: String?,
    ) {
        devices.removeAllViews()
        for ((device, name) in bonded) {
            devices.addView(
                Button(context).apply {
                    text =
                        if (device.address == remembered) context.getString(R.string.remembered_laptop, name) else name
                    setOnClickListener { onPick(device) }
                },
            )
        }
    }

    fun setStatus(text: CharSequence) {
        status.text = text
    }

    fun setConnected(connected: Boolean) {
        openControls.visibility = if (connected) View.VISIBLE else View.GONE
        disconnect.visibility = if (connected) View.VISIBLE else View.GONE
    }
}
