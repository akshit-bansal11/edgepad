package me.akshitbansal.edgepad

import android.Manifest
import android.app.Activity
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.content.pm.PackageManager
import android.graphics.Typeface
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.util.TypedValue
import android.view.WindowInsets
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import me.akshitbansal.edgepad.link.LaptopLink
import me.akshitbansal.edgepad.link.RttStats
import me.akshitbansal.edgepad.protocol.Frame
import kotlin.concurrent.thread

/** M1 screen: pick the paired laptop, connect, and watch the measured round trip. */
class MainActivity :
    Activity(),
    LaptopLink.Listener {
    private val handler = Handler(Looper.getMainLooper())
    private val rtt = RttStats()
    private var link: LaptopLink? = null

    private lateinit var devices: LinearLayout
    private lateinit var status: TextView
    private lateinit var rttView: TextView
    private lateinit var disconnect: Button

    private val pinger =
        object : Runnable {
            override fun run() {
                link?.send(Frame.Ping(System.nanoTime()))
                handler.postDelayed(this, PING_INTERVAL_MS)
            }
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val pad = (PADDING_DP * resources.displayMetrics.density).toInt()
        devices = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        status = TextView(this)
        rttView =
            TextView(this).apply {
                setTextSize(TypedValue.COMPLEX_UNIT_SP, RTT_TEXT_SP)
                typeface = Typeface.MONOSPACE
            }
        disconnect =
            Button(this).apply {
                setText(R.string.disconnect)
                isEnabled = false
                setOnClickListener { link?.close(getString(R.string.status_disconnected)) }
            }
        val column =
            LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                addView(
                    TextView(context).apply {
                        setText(R.string.choose_laptop)
                        setTextSize(TypedValue.COMPLEX_UNIT_SP, TITLE_TEXT_SP)
                    },
                )
                addView(TextView(context).apply { setText(R.string.choose_laptop_hint) })
                addView(devices)
                addView(status)
                addView(rttView)
                addView(disconnect)
            }
        val root =
            ScrollView(this).apply {
                addView(column)
                // Edge-to-edge is enforced from targetSdk 35: keep content clear of the system bars.
                setOnApplyWindowInsetsListener { view, insets ->
                    val bars = insets.getInsets(WindowInsets.Type.systemBars())
                    view.setPadding(pad + bars.left, pad + bars.top, pad + bars.right, pad + bars.bottom)
                    insets
                }
            }
        setContentView(root)
    }

    override fun onStart() {
        super.onStart()
        refresh()
    }

    override fun onStop() {
        super.onStop()
        link?.close(getString(R.string.status_disconnected))
    }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) refresh()
    }

    private fun refresh() {
        devices.removeAllViews()
        val adapter = getSystemService(BluetoothManager::class.java)?.adapter
        when {
            adapter == null || !adapter.isEnabled -> {
                status.setText(R.string.bluetooth_off)
            }

            checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED -> {
                status.setText(R.string.permission_needed)
                requestPermissions(arrayOf(Manifest.permission.BLUETOOTH_CONNECT), REQUEST_BLUETOOTH)
            }

            else -> {
                listBonded(adapter)
            }
        }
    }

    private fun listBonded(adapter: BluetoothAdapter) {
        val bonded =
            try {
                adapter.bondedDevices.orEmpty()
            } catch (e: SecurityException) {
                emptySet()
            }
        if (bonded.isEmpty()) {
            status.setText(R.string.no_paired)
            return
        }
        status.text = ""
        for (device in bonded.sortedBy(::nameOf)) {
            devices.addView(
                Button(this).apply {
                    text = nameOf(device)
                    setOnClickListener { connect(device) }
                },
            )
        }
    }

    private fun nameOf(device: BluetoothDevice): String =
        try {
            device.name ?: device.address
        } catch (e: SecurityException) {
            device.address
        }

    private fun connect(device: BluetoothDevice) {
        link?.close(getString(R.string.status_disconnected))
        rtt.clear()
        rttView.text = ""
        status.text = getString(R.string.status_connecting, nameOf(device))
        val next = LaptopLink(device, this)
        link = next
        thread(name = "edgepad-link") { next.open() }
    }

    override fun onConnected(link: LaptopLink) =
        runOnUiThread {
            if (link != this.link) return@runOnUiThread
            status.setText(R.string.status_connected)
            disconnect.isEnabled = true
            handler.post(pinger)
        }

    override fun onFrame(frame: Frame) {
        if (frame !is Frame.Pong) return
        val ms = (System.nanoTime() - frame.time) / NANOS_PER_MS
        runOnUiThread {
            rtt.add(ms)
            rttView.text = getString(R.string.rtt, rtt.median(), rtt.best)
        }
    }

    override fun onClosed(
        link: LaptopLink,
        reason: String,
    ) = runOnUiThread {
        if (link != this.link) return@runOnUiThread
        handler.removeCallbacks(pinger)
        this.link = null
        disconnect.isEnabled = false
        status.text = getString(R.string.status_closed, reason)
    }

    private companion object {
        const val REQUEST_BLUETOOTH = 1
        const val PING_INTERVAL_MS = 500L
        const val NANOS_PER_MS = 1_000_000.0
        const val PADDING_DP = 16
        const val TITLE_TEXT_SP = 22f
        const val RTT_TEXT_SP = 28f
    }
}
