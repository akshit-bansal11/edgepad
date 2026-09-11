package me.akshitbansal.edgepad

import android.Manifest
import android.app.Activity
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.content.pm.PackageManager
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.KeyEvent
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import me.akshitbansal.edgepad.link.LaptopLink
import me.akshitbansal.edgepad.link.RttStats
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.screens.OnboardingScreen
import me.akshitbansal.edgepad.screens.PickerScreen
import me.akshitbansal.edgepad.screens.ReconnectingScreen
import me.akshitbansal.edgepad.screens.SettingsScreen
import me.akshitbansal.edgepad.surface.ControlSurface
import kotlin.concurrent.thread

/**
 * One activity, five screens: onboarding on the first run, the laptop picker, settings, the control
 * surface, and a reconnecting screen when the link drops on its own. The link outlives the activity
 * across a rotation or a theme change, and is closed when the app leaves the foreground.
 */
class MainActivity :
    Activity(),
    LaptopLink.Listener {
    private enum class Screen { ONBOARDING, PICKER, SETTINGS, SURFACE, RECONNECTING }

    /** What survives the activity being rebuilt for a configuration change. */
    private class Retained(
        val link: LaptopLink?,
        val screen: Screen,
        val laptopName: String,
    )

    private val handler = Handler(Looper.getMainLooper())
    private val rtt = RttStats()
    private lateinit var settings: Settings
    private lateinit var picker: PickerScreen
    private var link: LaptopLink? = null
    private var laptopName = ""
    private var screen = Screen.PICKER
    private var surface: ControlSurface? = null
    private var started = false

    private val pinger =
        object : Runnable {
            override fun run() {
                link?.send(Frame.Ping(System.nanoTime()))
                handler.postDelayed(this, PING_INTERVAL_MS)
            }
        }

    private val reconnect =
        Runnable {
            val device = if (started && link == null) rememberedDevice() else null
            if (device != null) connect(device) else show(Screen.PICKER)
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        settings = Settings(this)
        picker =
            PickerScreen(
                this,
                onPick = ::connect,
                onOpenControls = { show(Screen.SURFACE) },
                onDisconnect = { link?.close(getString(R.string.status_disconnected)) },
                onSettings = { show(Screen.SETTINGS) },
            )
        val retained = lastNonConfigurationInstance as? Retained
        link = retained?.link?.also { it.listener = this }
        laptopName = retained?.laptopName ?: ""
        show(
            when {
                !settings.onboarded -> Screen.ONBOARDING
                retained != null -> retained.screen
                else -> Screen.PICKER
            },
        )
    }

    override fun onRetainNonConfigurationInstance(): Any? = Retained(link, screen, laptopName)

    override fun onStart() {
        super.onStart()
        started = true
        val current = link
        if (current != null) {
            if (current.connected) handler.post(pinger)
        } else if (screen != Screen.ONBOARDING) {
            rememberedDevice()?.let(::connect)
        }
        refreshPicker()
    }

    override fun onStop() {
        super.onStop()
        started = false
        handler.removeCallbacks(reconnect)
        handler.removeCallbacks(pinger)
        // The link only lives while the app is in front, except across a rebuild for rotation or theme.
        if (!isChangingConfigurations) link?.close(getString(R.string.status_disconnected))
    }

    override fun onKeyDown(
        keyCode: Int,
        event: KeyEvent?,
    ): Boolean {
        if (keyCode == KeyEvent.KEYCODE_BACK && screen != Screen.PICKER && screen != Screen.ONBOARDING) {
            if (screen == Screen.RECONNECTING) handler.removeCallbacks(reconnect)
            show(Screen.PICKER)
            return true
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) refreshPicker()
    }

    private fun show(next: Screen) {
        screen = next
        surface = null
        val view: View =
            when (next) {
                Screen.ONBOARDING -> {
                    OnboardingScreen.build(this) {
                        settings.onboarded = true
                        show(Screen.PICKER)
                        rememberedDevice()?.let(::connect)
                    }
                }

                Screen.PICKER -> {
                    picker.view.also { refreshPicker() }
                }

                Screen.SETTINGS -> {
                    SettingsScreen.build(this, settings) { settings.laptop = null }
                }

                Screen.RECONNECTING -> {
                    ReconnectingScreen.build(this, laptopName) {
                        handler.removeCallbacks(reconnect)
                        show(Screen.PICKER)
                    }
                }

                Screen.SURFACE -> {
                    ControlSurface(this, settings) { frame -> link?.send(frame) }.also { surface = it }
                }
            }
        setContentView(view)
        window.insetsController?.apply {
            if (next == Screen.SURFACE) {
                hide(WindowInsets.Type.systemBars())
                systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            } else {
                show(WindowInsets.Type.systemBars())
            }
        }
    }

    private fun refreshPicker() {
        val adapter = getSystemService(BluetoothManager::class.java)?.adapter
        when {
            adapter == null || !adapter.isEnabled -> {
                picker.setStatus(getString(R.string.bluetooth_off))
            }

            checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED -> {
                picker.setStatus(getString(R.string.permission_needed))
                requestPermissions(arrayOf(Manifest.permission.BLUETOOTH_CONNECT), REQUEST_BLUETOOTH)
            }

            else -> {
                val bonded = bonded()
                picker.showDevices(bonded.map { it to nameOf(it) }.sortedBy { it.second }, settings.laptop)
                if (bonded.isEmpty()) picker.setStatus(getString(R.string.no_paired))
            }
        }
        val current = link
        picker.setConnected(current?.connected == true)
        if (current?.connected == true) picker.setStatus(getString(R.string.status_connected_to, laptopName))
    }

    private fun bonded(): List<BluetoothDevice> {
        val adapter = getSystemService(BluetoothManager::class.java)?.adapter ?: return emptyList()
        if (!adapter.isEnabled) return emptyList()
        if (checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
            return emptyList()
        }
        return try {
            adapter.bondedDevices.orEmpty().toList()
        } catch (e: SecurityException) {
            emptyList()
        }
    }

    private fun rememberedDevice(): BluetoothDevice? {
        val address = settings.laptop ?: return null
        return bonded().firstOrNull { it.address == address }
    }

    private fun nameOf(device: BluetoothDevice): String =
        try {
            device.name ?: device.address
        } catch (e: SecurityException) {
            device.address
        }

    private fun connect(device: BluetoothDevice) {
        handler.removeCallbacks(reconnect)
        link?.close(getString(R.string.status_disconnected))
        settings.laptop = device.address
        laptopName = nameOf(device)
        rtt.clear()
        picker.setStatus(getString(R.string.status_connecting, laptopName))
        val next = LaptopLink(device, this)
        link = next
        thread(name = "edgepad-link") { next.open() }
    }

    override fun onConnected(link: LaptopLink) =
        runOnUiThread {
            if (link != this.link) return@runOnUiThread
            handler.post(pinger)
            show(Screen.SURFACE)
        }

    override fun onFrame(frame: Frame) {
        when (frame) {
            is Frame.Pong -> {
                val ms = (System.nanoTime() - frame.time) / NANOS_PER_MS
                runOnUiThread {
                    rtt.add(ms)
                    surface?.status = getString(R.string.surface_status, rtt.median())
                }
            }

            is Frame.StateReport -> {
                runOnUiThread { surface?.onState(frame.control, frame.value, frame.flags) }
            }

            is Frame.Text -> {
                runOnUiThread { surface?.onText(frame.kind, frame.text) }
            }

            else -> {
                Unit
            }
        }
    }

    override fun onClosed(
        link: LaptopLink,
        reason: String,
    ) = runOnUiThread {
        if (link != this.link) return@runOnUiThread
        handler.removeCallbacks(pinger)
        this.link = null
        val dropped = link.wasConnected && reason != getString(R.string.status_disconnected)
        if (started && (dropped || screen == Screen.RECONNECTING)) {
            // A link that dropped on its own is retried until it is back or the user stops it; one the
            // user ended is not.
            if (screen != Screen.RECONNECTING) show(Screen.RECONNECTING)
            handler.postDelayed(reconnect, RECONNECT_DELAY_MS)
        } else {
            if (screen == Screen.SURFACE || screen == Screen.RECONNECTING) show(Screen.PICKER)
            refreshPicker()
            picker.setStatus(getString(R.string.status_closed, reason))
        }
    }

    private companion object {
        const val REQUEST_BLUETOOTH = 1
        const val PING_INTERVAL_MS = 500L
        const val RECONNECT_DELAY_MS = 2_000L
        const val NANOS_PER_MS = 1_000_000.0
    }
}
