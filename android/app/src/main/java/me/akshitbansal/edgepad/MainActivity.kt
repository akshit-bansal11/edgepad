package me.akshitbansal.edgepad

import android.Manifest
import android.app.Activity
import android.bluetooth.BluetoothClass
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.content.Intent
import android.content.pm.ActivityInfo
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.view.KeyEvent
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.window.OnBackInvokedCallback
import android.window.OnBackInvokedDispatcher
import me.akshitbansal.edgepad.gamepad.GamepadStore
import me.akshitbansal.edgepad.link.LaptopLink
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.RttStats
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.ProtocolConstants
import me.akshitbansal.edgepad.screens.AppearanceScreen
import me.akshitbansal.edgepad.screens.CornersScreen
import me.akshitbansal.edgepad.screens.DialFeelScreen
import me.akshitbansal.edgepad.screens.GamepadLayoutScreen
import me.akshitbansal.edgepad.screens.GamepadScreen
import me.akshitbansal.edgepad.screens.GestureScreen
import me.akshitbansal.edgepad.screens.GuideScreen
import me.akshitbansal.edgepad.screens.KeyboardScreen
import me.akshitbansal.edgepad.screens.MediaLayoutScreen
import me.akshitbansal.edgepad.screens.PickerScreen
import me.akshitbansal.edgepad.screens.ReconnectingScreen
import me.akshitbansal.edgepad.screens.SettingsScreen
import me.akshitbansal.edgepad.screens.Ui
import me.akshitbansal.edgepad.surface.ControlSurface
import java.io.IOException
import kotlin.concurrent.thread

/**
 * One activity, five screens: onboarding on the first run, finding the laptop, settings, the control
 * surface, and a connection-lost screen that retries after the link drops on its own. The link and what
 * the laptop has reported outlive the activity across a rotation or a theme change; the link closes when
 * the app leaves the foreground.
 */
class MainActivity :
    Activity(),
    LaptopLink.Listener {
    private enum class Screen {
        ONBOARDING,
        GUIDE,
        PAIRING,
        SETTINGS,
        CORNERS,
        GESTURES,
        DIAL_FEEL,
        APPEARANCE,
        MEDIA_LAYOUT,
        GAMEPAD_LAYOUT,
        KEYBOARD,
        GAMEPAD,
        SURFACE,
        RECONNECTING,
    }

    /** What survives the activity being rebuilt for a rotation or a theme change. */
    private class Retained(
        val link: LaptopLink?,
        val state: LaptopState,
        val screen: Screen,
        val settingsReturn: Screen,
        val layoutReturn: Screen,
        val laptopName: String,
        val laptopAddress: String,
        val attempts: Int,
        val lostAt: Long,
    )

    private val handler = Handler(Looper.getMainLooper())
    private val rtt = RttStats()
    private lateinit var settings: Settings
    private lateinit var ui: Ui
    private lateinit var picker: PickerScreen
    private var state = LaptopState()
    private var link: LaptopLink? = null
    private var surface: ControlSurface? = null
    private var reconnecting: ReconnectingScreen? = null
    private var laptopName = ""
    private var laptopAddress = ""
    private var screen = Screen.PAIRING
    private var settingsReturn = Screen.PAIRING

    /** Where the gamepad layout editor goes back to: Settings, or the gamepad it was opened from. */
    private var layoutReturn = Screen.SETTINGS
    private lateinit var gamepads: GamepadStore
    private var started = false
    private var attempts = 0
    private var lostAt = 0L
    private var backCallback: Any? = null

    private val pinger =
        object : Runnable {
            override fun run() {
                link?.send(Frame.Ping(System.nanoTime()))
                handler.postDelayed(this, PING_INTERVAL_MS)
            }
        }

    private val retry = Runnable { attemptReconnect() }

    private val ticker =
        object : Runnable {
            override fun run() {
                updateReconnecting()
                handler.postDelayed(this, TICK_MS)
            }
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        settings = Settings(this)
        gamepads = GamepadStore(this)
        Type.load(this)
        ui = Ui(this)
        picker =
            PickerScreen(
                ui,
                onConnect = ::connectTo,
                onOpenControls = { goTo(Screen.SURFACE) },
                onBluetoothSettings = { startActivity(Intent(android.provider.Settings.ACTION_BLUETOOTH_SETTINGS)) },
                onSettings = ::openSettings,
                onRefresh = ::refreshPicker,
            )
        val retained = lastNonConfigurationInstance as? Retained
        if (retained != null) {
            link = retained.link?.also { it.listener = this }
            state = retained.state
            settingsReturn = retained.settingsReturn
            layoutReturn = retained.layoutReturn
            laptopName = retained.laptopName
            laptopAddress = retained.laptopAddress
            attempts = retained.attempts
            lostAt = retained.lostAt
        }
        goTo(
            when {
                !settings.onboarded -> Screen.ONBOARDING
                retained != null -> retained.screen
                else -> Screen.PAIRING
            },
        )
    }

    override fun onRetainNonConfigurationInstance(): Any =
        Retained(link, state, screen, settingsReturn, layoutReturn, laptopName, laptopAddress, attempts, lostAt)

    override fun onStart() {
        super.onStart()
        started = true
        val current = link
        when {
            current != null -> if (current.connected) handler.post(pinger)
            screen == Screen.RECONNECTING -> scheduleRetry(0L)
            screen != Screen.ONBOARDING && settings.reconnect -> rememberedDevice()?.let(::connect)
        }
        if (screen == Screen.RECONNECTING) handler.post(ticker)
        refreshPicker()
    }

    override fun onStop() {
        super.onStop()
        started = false
        handler.removeCallbacks(pinger)
        handler.removeCallbacks(retry)
        handler.removeCallbacks(ticker)
        // The link only lives while the app is in front, except across a rebuild for rotation or theme.
        if (!isChangingConfigurations) link?.close(getString(R.string.status_disconnected))
    }

    // Before Android 16 the back key still arrives here; from 16 it goes to the callback in updateBack().
    override fun onKeyDown(
        keyCode: Int,
        event: KeyEvent?,
    ): Boolean {
        if (keyCode == KeyEvent.KEYCODE_BACK && navigateBack()) return true
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

    private fun goTo(next: Screen) {
        screen = next
        surface = null
        reconnecting = null
        handler.removeCallbacks(ticker)
        val view: View =
            when (next) {
                Screen.ONBOARDING -> {
                    GuideScreen.build(ui, settings, intro = true, onFinish = {
                        settings.onboarded = true
                        goTo(Screen.PAIRING)
                        if (settings.reconnect) rememberedDevice()?.let(::connect)
                    }) { navigateBack() }
                }

                Screen.GUIDE -> {
                    GuideScreen.build(ui, settings, intro = false, onFinish = {}) { navigateBack() }
                }

                Screen.PAIRING -> {
                    picker.view.also { refreshPicker() }
                }

                Screen.SETTINGS -> {
                    SettingsScreen.build(
                        ui,
                        settings,
                        connectionInfo(),
                        gamepads.current.name,
                        versionLine(),
                        SettingsScreen.Routes(
                            forget = ::forget,
                            corners = { goTo(Screen.CORNERS) },
                            gestures = { goTo(Screen.GESTURES) },
                            dialFeel = { goTo(Screen.DIAL_FEEL) },
                            mediaLayout = { goTo(Screen.MEDIA_LAYOUT) },
                            gamepadLayout = {
                                layoutReturn = Screen.SETTINGS
                                goTo(Screen.GAMEPAD_LAYOUT)
                            },
                            appearance = { goTo(Screen.APPEARANCE) },
                            guide = { goTo(Screen.GUIDE) },
                            landscape = { on ->
                                settings.landscape = on
                                applyOrientation()
                            },
                            back = { navigateBack() },
                        ),
                    )
                }

                Screen.CORNERS -> {
                    CornersScreen.build(ui, settings) { navigateBack() }
                }

                Screen.GESTURES -> {
                    GestureScreen.build(ui, settings) { navigateBack() }
                }

                Screen.DIAL_FEEL -> {
                    DialFeelScreen.build(ui, settings) { navigateBack() }
                }

                Screen.APPEARANCE -> {
                    AppearanceScreen.build(ui, settings, ::pickImage) { navigateBack() }
                }

                Screen.MEDIA_LAYOUT -> {
                    MediaLayoutScreen.build(ui, settings) { navigateBack() }
                }

                Screen.GAMEPAD_LAYOUT -> {
                    GamepadLayoutScreen.build(ui, gamepads) { navigateBack() }
                }

                Screen.KEYBOARD -> {
                    KeyboardScreen.build(ui, onKey = ::key) { navigateBack() }
                }

                Screen.GAMEPAD -> {
                    GamepadScreen.build(
                        ui,
                        gamepads.current,
                        onKey = ::key,
                        onBack = { navigateBack() },
                        onEdit = {
                            layoutReturn = Screen.GAMEPAD
                            goTo(Screen.GAMEPAD_LAYOUT)
                        },
                    )
                }

                Screen.RECONNECTING -> {
                    val lost =
                        ReconnectingScreen(ui, laptopName, laptopAddress, onRetry = ::retryNow) {
                            stopRetrying()
                            goTo(Screen.PAIRING)
                        }
                    reconnecting = lost
                    if (started) handler.post(ticker)
                    lost.view
                }

                Screen.SURFACE -> {
                    ControlSurface(
                        this,
                        settings,
                        state,
                        { frame -> link?.send(frame) },
                        ::openSettings,
                        onOpenKeyboard = { goTo(Screen.KEYBOARD) },
                        onOpenGamepad = { goTo(Screen.GAMEPAD) },
                    ).also { surface = it }
                }
            }
        setContentView(view)
        applyOrientation()
        window.insetsController?.let { bars ->
            val light =
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or
                    WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS
            bars.setSystemBarsAppearance(if (ui.palette.dark) 0 else light, light)
            if (next == Screen.SURFACE || next == Screen.KEYBOARD || next == Screen.GAMEPAD) {
                bars.hide(WindowInsets.Type.systemBars())
                bars.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            } else {
                bars.show(WindowInsets.Type.systemBars())
            }
        }
        updateBack()
    }

    /** Held one way: the keyboard and gamepad sideways, everything else as the setting says. A change rebuilds the activity; the link survives it. */
    private fun applyOrientation() {
        val sideways =
            screen == Screen.KEYBOARD || screen == Screen.GAMEPAD || screen == Screen.GAMEPAD_LAYOUT ||
                settings.landscape
        requestedOrientation =
            if (sideways) {
                ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
            } else {
                ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
            }
    }

    /** From Android 16 back arrives only through a callback; it is registered while there is a screen to go back from. */
    private fun updateBack() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.BAKLAVA) return
        val callback =
            backCallback as? OnBackInvokedCallback
                ?: OnBackInvokedCallback { navigateBack() }.also { backCallback = it }
        onBackInvokedDispatcher.unregisterOnBackInvokedCallback(callback)
        if (screen != Screen.PAIRING && screen != Screen.ONBOARDING) {
            onBackInvokedDispatcher.registerOnBackInvokedCallback(OnBackInvokedDispatcher.PRIORITY_DEFAULT, callback)
        }
    }

    /** One step back through the app. False at the first screen, where back leaves the app as usual. */
    private fun navigateBack(): Boolean {
        when (screen) {
            Screen.SURFACE -> {
                goTo(Screen.PAIRING)
            }

            Screen.SETTINGS -> {
                goTo(
                    if (settingsReturn == Screen.SURFACE &&
                        link?.connected != true
                    ) {
                        Screen.PAIRING
                    } else {
                        settingsReturn
                    },
                )
            }

            Screen.RECONNECTING -> {
                stopRetrying()
                goTo(Screen.PAIRING)
            }

            Screen.CORNERS, Screen.GESTURES, Screen.DIAL_FEEL, Screen.APPEARANCE, Screen.GUIDE, Screen.MEDIA_LAYOUT -> {
                goTo(Screen.SETTINGS)
            }

            Screen.GAMEPAD_LAYOUT -> {
                goTo(layoutReturn)
            }

            Screen.KEYBOARD, Screen.GAMEPAD -> {
                goTo(if (link?.connected == true) Screen.SURFACE else Screen.PAIRING)
            }

            else -> {
                return false
            }
        }
        return true
    }

    /** Asks the system's picker for an image; the copy lands in app storage so the surface can read it any time. */
    private fun pickImage() {
        val intent =
            Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
                addCategory(Intent.CATEGORY_OPENABLE)
                type = "image/*"
            }
        @Suppress("DEPRECATION")
        startActivityForResult(intent, REQUEST_IMAGE)
    }

    @Deprecated("The platform result API; the app has no library that wraps it.")
    override fun onActivityResult(
        requestCode: Int,
        resultCode: Int,
        data: Intent?,
    ) {
        @Suppress("DEPRECATION")
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != REQUEST_IMAGE || resultCode != RESULT_OK) return
        val uri = data?.data ?: return
        try {
            contentResolver.openInputStream(uri)?.use { input ->
                settings.backgroundImage.outputStream().use { input.copyTo(it) }
            }
            settings.background = Settings.Background.IMAGE
        } catch (e: IOException) {
            settings.backgroundImage.delete()
        }
        if (screen == Screen.APPEARANCE) goTo(Screen.APPEARANCE)
    }

    /** A key pressed or released on the keyboard or gamepad screen. */
    private fun key(
        code: Int,
        down: Boolean,
    ) {
        link?.send(Frame.Key(code, down))
    }

    private fun versionLine(): String =
        getString(R.string.settings_version, getString(R.string.app_version), ProtocolConstants.VERSION)

    private fun openSettings() {
        if (screen != Screen.SETTINGS) settingsReturn = screen
        goTo(Screen.SETTINGS)
    }

    private fun refreshPicker() {
        val adapter = getSystemService(BluetoothManager::class.java)?.adapter
        val problem =
            when {
                adapter == null || !adapter.isEnabled -> {
                    getString(R.string.bluetooth_off)
                }

                checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED -> {
                    requestPermissions(arrayOf(Manifest.permission.BLUETOOTH_CONNECT), REQUEST_BLUETOOTH)
                    getString(R.string.permission_needed)
                }

                else -> {
                    null
                }
            }
        val devices = bonded().map { PickerScreen.Device(it.address, nameOf(it)) }.sortedBy { it.name }
        picker.showDevices(
            devices,
            settings.laptop,
            if (link?.connected == true) laptopAddress else null,
            settings.reconnect,
        )
        picker.setMessage(problem ?: if (devices.isEmpty()) getString(R.string.no_paired) else "")
    }

    private fun bonded(): List<BluetoothDevice> {
        val adapter = getSystemService(BluetoothManager::class.java)?.adapter ?: return emptyList()
        if (!adapter.isEnabled) return emptyList()
        if (checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
            return emptyList()
        }
        return try {
            // Only things that can run the laptop app: headsets, mice and keyboards are paired too.
            adapter.bondedDevices.orEmpty().filter {
                it.bluetoothClass?.majorDeviceClass ==
                    BluetoothClass.Device.Major.COMPUTER
            }
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

    private fun connectTo(address: String) {
        val device = bonded().firstOrNull { it.address == address }
        if (device == null) picker.setMessage(getString(R.string.not_paired)) else connect(device)
    }

    private fun connect(device: BluetoothDevice) {
        handler.removeCallbacks(retry)
        val old = link
        link = null
        old?.close(getString(R.string.status_disconnected))
        // What one laptop reported says nothing about another.
        if (device.address != laptopAddress) state = LaptopState()
        settings.laptop = device.address
        laptopName = nameOf(device)
        laptopAddress = device.address
        rtt.clear()
        picker.setRtt(Double.NaN)
        picker.setMessage("")
        picker.setConnecting(laptopAddress)
        val next = LaptopLink(device, this)
        link = next
        thread(name = "edgepad-link") { next.open() }
    }

    override fun onConnected(link: LaptopLink) =
        runOnUiThread {
            if (link != this.link) return@runOnUiThread
            attempts = 0
            picker.setConnecting(null)
            handler.post(pinger)
            when (screen) {
                Screen.PAIRING, Screen.RECONNECTING -> goTo(Screen.SURFACE)
                Screen.SETTINGS -> settingsReturn = Screen.SURFACE
                else -> Unit
            }
        }

    override fun onFrame(frame: Frame) {
        if (frame is Frame.Pong) {
            val ms = (System.nanoTime() - frame.time) / NANOS_PER_MS
            runOnUiThread {
                rtt.add(ms)
                picker.setRtt(rtt.median())
            }
            return
        }
        runOnUiThread { if (state.take(frame)) surface?.stateChanged() }
    }

    override fun onClosed(
        link: LaptopLink,
        reason: String,
    ) = runOnUiThread {
        if (link != this.link) return@runOnUiThread
        handler.removeCallbacks(pinger)
        this.link = null
        picker.setConnecting(null)
        val userEnded = reason == getString(R.string.status_disconnected)
        when {
            userEnded -> {
                if (screen == Screen.SURFACE || screen == Screen.RECONNECTING || screen == Screen.KEYBOARD ||
                    screen == Screen.GAMEPAD
                ) {
                    goTo(Screen.PAIRING)
                }
                if (screen == Screen.SETTINGS) {
                    if (settingsReturn == Screen.SURFACE) settingsReturn = Screen.PAIRING
                    goTo(Screen.SETTINGS)
                }
                refreshPicker()
            }

            // It was working and dropped on its own: say so and try to get it back.
            link.wasConnected -> {
                lostAt = SystemClock.elapsedRealtime()
                attempts = 0
                goTo(Screen.RECONNECTING)
                scheduleRetry(RETRY_DELAY_MS)
            }

            screen == Screen.RECONNECTING -> {
                attempts++
                scheduleRetry(RETRY_DELAY_MS)
                updateReconnecting()
            }

            else -> {
                refreshPicker()
                picker.setMessage(getString(R.string.status_closed, reason))
            }
        }
    }

    private fun scheduleRetry(delay: Long) {
        handler.removeCallbacks(retry)
        if (started && settings.reconnect && attempts < MAX_ATTEMPTS) handler.postDelayed(retry, delay)
    }

    private fun attemptReconnect() {
        if (!started || link != null) return
        val device = rememberedDevice()
        if (device == null) {
            attempts = MAX_ATTEMPTS
        } else {
            connect(device)
        }
        updateReconnecting()
    }

    private fun retryNow() {
        handler.removeCallbacks(retry)
        if (link != null) return
        if (attempts >= MAX_ATTEMPTS) attempts = 0
        attemptReconnect()
    }

    private fun stopRetrying() {
        handler.removeCallbacks(retry)
        val attempt = link
        if (attempt != null && !attempt.connected) {
            link = null
            attempt.close(getString(R.string.status_disconnected))
        }
    }

    private fun updateReconnecting() {
        val lost = reconnecting ?: return
        val status =
            when {
                !settings.reconnect && link == null -> ReconnectingScreen.State.OFF
                attempts >= MAX_ATTEMPTS -> ReconnectingScreen.State.STOPPED
                else -> ReconnectingScreen.State.TRYING
            }
        val seconds = (SystemClock.elapsedRealtime() - lostAt) / MILLIS_PER_SECOND
        lost.update((attempts + 1).coerceAtMost(MAX_ATTEMPTS), MAX_ATTEMPTS, status, seconds)
    }

    private fun forget() {
        settings.laptop = null
        val current = link
        if (current != null) current.close(getString(R.string.status_disconnected)) else goTo(Screen.SETTINGS)
    }

    private fun connectionInfo(): SettingsScreen.Connection {
        val remembered = settings.laptop ?: return SettingsScreen.Connection(null, "")
        val name =
            if (remembered == laptopAddress && laptopName.isNotEmpty()) {
                laptopName
            } else {
                bonded().firstOrNull { it.address == remembered }?.let(::nameOf) ?: remembered
            }
        val median = rtt.median()
        val detail =
            when {
                link?.connected == true && !median.isNaN() -> getString(R.string.detail_connected_rtt, median)
                link?.connected == true -> getString(R.string.detail_connected)
                else -> getString(R.string.detail_remembered)
            }
        return SettingsScreen.Connection(name, detail)
    }

    private companion object {
        const val REQUEST_BLUETOOTH = 1
        const val REQUEST_IMAGE = 2
        const val PING_INTERVAL_MS = 500L
        const val TICK_MS = 1_000L
        const val RETRY_DELAY_MS = 2_000L
        const val MAX_ATTEMPTS = 10
        const val MILLIS_PER_SECOND = 1_000L
        const val NANOS_PER_MS = 1_000_000.0
    }
}
