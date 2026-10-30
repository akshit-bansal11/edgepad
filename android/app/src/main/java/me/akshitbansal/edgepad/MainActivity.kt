package me.akshitbansal.edgepad

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.bluetooth.BluetoothClass
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.content.ActivityNotFoundException
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ActivityInfo
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.provider.Settings.Global
import android.text.InputType
import android.util.Log
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import android.widget.EditText
import android.widget.TextView
import android.widget.Toast
import android.window.OnBackInvokedCallback
import android.window.OnBackInvokedDispatcher
import me.akshitbansal.edgepad.gamepad.GamepadStore
import me.akshitbansal.edgepad.link.BluetoothTransport
import me.akshitbansal.edgepad.link.Discovery
import me.akshitbansal.edgepad.link.Found
import me.akshitbansal.edgepad.link.HostPort
import me.akshitbansal.edgepad.link.LaptopLink
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.LinkTransport
import me.akshitbansal.edgepad.link.LocalNetwork
import me.akshitbansal.edgepad.link.PickerHint
import me.akshitbansal.edgepad.link.RttStats
import me.akshitbansal.edgepad.link.Sweep
import me.akshitbansal.edgepad.link.Target
import me.akshitbansal.edgepad.link.TcpTransport
import me.akshitbansal.edgepad.link.TetherProbe
import me.akshitbansal.edgepad.link.mergeFound
import me.akshitbansal.edgepad.link.net.LinkKeys
import me.akshitbansal.edgepad.link.net.Sas
import me.akshitbansal.edgepad.link.otherTransport
import me.akshitbansal.edgepad.link.parseHostPort
import me.akshitbansal.edgepad.link.pickerHint
import me.akshitbansal.edgepad.link.probeAddress
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.ProtocolConstants
import me.akshitbansal.edgepad.protocol.SignStatus
import me.akshitbansal.edgepad.protocol.TextKind
import me.akshitbansal.edgepad.screens.AnnotateScreen
import me.akshitbansal.edgepad.screens.AppearanceScreen
import me.akshitbansal.edgepad.screens.CaptureScreen
import me.akshitbansal.edgepad.screens.ControlsPopup
import me.akshitbansal.edgepad.screens.CornersScreen
import me.akshitbansal.edgepad.screens.DialFeelScreen
import me.akshitbansal.edgepad.screens.GamepadLayoutScreen
import me.akshitbansal.edgepad.screens.GamepadScreen
import me.akshitbansal.edgepad.screens.GamepadSettingsScreen
import me.akshitbansal.edgepad.screens.GestureScreen
import me.akshitbansal.edgepad.screens.GuideScreen
import me.akshitbansal.edgepad.screens.KeyboardScreen
import me.akshitbansal.edgepad.screens.KeyboardSettingsScreen
import me.akshitbansal.edgepad.screens.MacroScreen
import me.akshitbansal.edgepad.screens.MacroSettingsScreen
import me.akshitbansal.edgepad.screens.MediaLayoutScreen
import me.akshitbansal.edgepad.screens.PairCodeScreen
import me.akshitbansal.edgepad.screens.PickerScreen
import me.akshitbansal.edgepad.screens.PinScreen
import me.akshitbansal.edgepad.screens.ReconnectingScreen
import me.akshitbansal.edgepad.screens.SettingsScreen
import me.akshitbansal.edgepad.screens.ShapeDrawScreen
import me.akshitbansal.edgepad.screens.ShapesScreen
import me.akshitbansal.edgepad.screens.SurfaceSettingsScreen
import me.akshitbansal.edgepad.screens.Ui
import me.akshitbansal.edgepad.screens.showOver
import me.akshitbansal.edgepad.surface.BackgroundImage
import me.akshitbansal.edgepad.surface.ControlSurface
import me.akshitbansal.edgepad.update.Installer
import me.akshitbansal.edgepad.update.Updates
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import kotlin.concurrent.thread

/**
 * One activity for every screen: the guide on the first run and from Settings, the Devices list, the
 * control surface, the settings pages, the keyboard, the gamepad, the macros, the two layout editors, the
 * laptop's PIN, and the connection-lost screen that retries after a link drops on its own. [Screen] is the
 * whole set and [goTo] the only way between them — there are no fragments and no back stack, so
 * [navigateBack] is where every screen's way out is written down.
 *
 * The link and what the laptop has reported outlive the activity across a rotation or a theme change,
 * through [onRetainNonConfigurationInstance]. That matters because changing the theme or the orientation
 * setting rebuilds the activity, and a link dropped there would make every settings change look like a
 * disconnection.
 *
 * The link against the activity's lifecycle, which is the whole of it:
 *
 * - Started (onStart to onStop): the link is used, and pinged every [PING_FAST_MS] on a screen that shows
 *   the round trip and every [PING_SLOW_MS] elsewhere, where the ping only proves the link is still there.
 * - Stopped, and not for a rebuild: whatever a finger holds is let go at once ([releaseHeldInput]); the
 *   link stays up for [LINGER_MS], pinged slowly. The image picker, Bluetooth settings, the documentation
 *   in a browser and the screen going off are all a stop, and until 3.2 every one of them dropped the link.
 * - Started again inside [LINGER_MS]: nothing happened; the link carries on.
 * - [LINGER_MS] passes: the link closes as [LaptopLink.Cause.ENDED], which is what onStop did at once
 *   before 3.2.
 * - Destroyed, and not for a rebuild (backed out of, or finished): closed at once.
 * - Rebuilt: handed over in [Retained], its listener swapped in onCreate. The link posts every callback
 *   to the main thread and reads the listener when the callback runs, so nothing lands on the old activity.
 *
 * There is one pinger, and [startPinger] is the only way to start it: it clears any tick already waiting,
 * so replacing a link or reconnecting can never leave two ping loops running side by side.
 */
class MainActivity :
    Activity(),
    LaptopLink.Listener {
    private enum class Screen {
        ONBOARDING,
        GUIDE,
        PAIRING,
        SETTINGS,
        SURFACE_SETTINGS,
        CORNERS,
        GESTURES,
        SHAPES,
        SHAPE_DRAW,
        DIAL_FEEL,
        APPEARANCE,
        MEDIA_LAYOUT,
        GAMEPAD_LAYOUT,
        KEYBOARD_SETTINGS,
        GAMEPAD_SETTINGS,
        MACRO_SETTINGS,
        CAPTURE,
        ANNOTATE,
        KEYBOARD,
        GAMEPAD,
        MACROS,
        SURFACE,
        RECONNECTING,
        PIN,
        PAIR_CODE,
    }

    /**
     * What survives the activity being rebuilt for a rotation or a theme change. Never the PIN: the digits
     * typed so far go with the screen that held them, and the person types them again.
     */
    private class Retained(
        val link: LaptopLink?,
        val state: LaptopState,
        val screen: Screen,
        val settingsReturn: Screen,
        val laptopName: String,
        val laptopAddress: String,
        val attempts: Int,
        val lostAt: Long,
        val rtt: RttStats,
        val pinAttemptsLeft: Int,
        val pinAnswered: Boolean,
        val pairCode: Sas?,
        val pairDeadline: Long,
        val pairAsking: Boolean,
        val lastTarget: Target?,
    )

    private val handler = Handler(Looper.getMainLooper())
    private var rtt = RttStats()
    private lateinit var settings: Settings
    private lateinit var ui: Ui
    private lateinit var picker: PickerScreen
    private var state = LaptopState()
    private var link: LaptopLink? = null
    private var surface: ControlSurface? = null

    /** The gamepad while it is the screen, so the laptop's answer about its controller can reach it. */
    private var gamepad: GamepadScreen? = null

    /** The annotate screen while it is the screen, so a changed laptop display can redraw its pad. */
    private var annotate: AnnotateScreen? = null

    /** True from ANNOTATE_BEGIN until the one ANNOTATE_END that answers it, however the screen is left. */
    private var annotating = false
    private var reconnecting: ReconnectingScreen? = null

    /** The macro grid while it is the screen, so a new list or a finished icon can be handed to it. */
    private var macros: MacroScreen? = null

    /** The Controls popup over the surface, while it is open. */
    private var controls: ControlsPopup? = null
    private var laptopName = ""
    private var laptopAddress = ""
    private var screen = Screen.PAIRING
    private var settingsReturn = Screen.PAIRING
    private lateinit var gamepads: GamepadStore
    private var started = false
    private var attempts = 0
    private var lostAt = 0L
    private var connectedAt = 0L

    /** What the laptop's last PIN question allowed, for the PIN screen; a count, never a digit. */
    private var pinAttemptsLeft = 0

    /** True once the PIN screen sent its answer to the current question, so a rebuild cannot send a second. */
    private var pinAnswered = false

    /** The code a network pairing shows, and when its countdown ends; null when no pairing is waiting. */
    private var pairCode: Sas? = null
    private var pairDeadline = 0L

    /** True once the laptop answered the pairing and the owner is asked whether it accepted the code. */
    private var pairAsking = false

    /** What the current or last connect aimed at, so a dropped network link can be retried like a Bluetooth one. */
    private var lastTarget: Target? = null

    /** This phone's network link id and pairing keys, opened the first time a network laptop is tried. */
    private val linkKeys by lazy { LinkKeys.open(this) }

    /**
     * A tap on a laptop has one quick retry coming if its connect never reaches the laptop: the first connect
     * after a long idle often fails once and then works. Set by [connectTo] only, so the retry itself, the
     * reconnect screen and the automatic connect at start never earn another.
     */
    private var quickRetryLeft = false
    private var backCallback: Any? = null

    /** Paired devices of every kind in the list, not only the ones that say they are computers. */
    private var showAllDevices = false

    /** Nearby devices has been asked for once by this activity; after that only a tap asks again. */
    private var askedPermission = false

    /** A check with GitHub is in flight, or the last one got no answer. Neither outlives the activity. */
    private var checkingUpdate = false
    private var updateCheckFailed = false

    /** How much of an update has been downloaded, as a percentage; null when none is being downloaded. */
    private var updateProgress: Int? = null

    /** The Updates row's summary while Settings is showing, rewritten in place as a check finishes. */
    private var updateSummaryView: TextView? = null

    /** The system's permission prompt is up, so the Devices list offers no way round it underneath. */
    private var permissionPending = false

    /** The local-network permission has been asked for once, so the list offers the way back and does not nag. */
    private var askedLocalNetwork = false

    /** Laptops found by NsdManager and by the tether probe, and by the last address typed; the picker merges them. */
    private var wifiFound: List<Found> = emptyList()
    private var usbFound: List<Found> = emptyList()

    /** What the last sweep saw and whether a USB cable is in; the Devices list says both under the list. */
    private var sweep: Sweep? = null
    private var usbCable = false
    private var discovery: Discovery? = null

    /** The network probe's pass under way, a fresh one each time; null when none is, or [stopScan] abandoned it. */
    private var probe: TetherProbe? = null

    /** Rescans every [SCAN_EVERY_MS] while the Devices list is on screen, and only then. */
    private val rescan =
        object : Runnable {
            override fun run() {
                if (!started || screen != Screen.PAIRING) return
                discovery?.start()
                usbCable = usbCableIn()
                probeTether()
                refreshPicker()
                handler.postDelayed(this, SCAN_EVERY_MS)
            }
        }

    private val pinger =
        object : Runnable {
            override fun run() {
                val current = link ?: return
                if (!current.connected) return
                current.send(Frame.Ping(System.nanoTime()))
                handler.postDelayed(this, if (started && screen in showsRtt) PING_FAST_MS else PING_SLOW_MS)
            }
        }

    private val linger = Runnable { link?.close(LaptopLink.Closure(LaptopLink.Cause.ENDED)) }

    private val retry = Runnable { attemptReconnect() }

    /** The one automatic second try of a first connect that never reached the laptop; see [quickRetryLeft]. */
    private val quickRetry = Runnable { if (started && link == null) rememberedTarget()?.let(::connect) }

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
                onBluetoothSettings = ::openBluetoothSettings,
                onSettings = ::openSettings,
                onRefresh = {
                    refreshPicker()
                    startScan()
                },
                onShowAll = { all ->
                    showAllDevices = all
                    refreshPicker()
                },
                onAddAddress = ::askAddress,
                onUsbHelp = ::openTetherSettings,
            )
        discovery =
            Discovery(this) { list ->
                handler.post {
                    wifiFound = list
                    if (screen == Screen.PAIRING) refreshPicker()
                }
            }
        val retained = lastNonConfigurationInstance as? Retained
        if (retained != null) {
            link = retained.link?.also { it.listener = this }
            state = retained.state
            settingsReturn = retained.settingsReturn
            laptopName = retained.laptopName
            laptopAddress = retained.laptopAddress
            attempts = retained.attempts
            lostAt = retained.lostAt
            // Carried over, or the readout would stand blank after a rotation and then restart from one sample.
            rtt = retained.rtt
            picker.setRtt(rtt.median())
            pinAttemptsLeft = retained.pinAttemptsLeft
            pinAnswered = retained.pinAnswered
            pairCode = retained.pairCode
            pairDeadline = retained.pairDeadline
            pairAsking = retained.pairAsking
            lastTarget = retained.lastTarget
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
        Retained(
            link,
            state,
            screen,
            settingsReturn,
            laptopName,
            laptopAddress,
            attempts,
            lostAt,
            rtt,
            pinAttemptsLeft,
            pinAnswered,
            pairCode,
            pairDeadline,
            pairAsking,
            lastTarget,
        )

    override fun onStart() {
        super.onStart()
        handler.removeCallbacks(linger)
        started = true
        // Back from a detour with the pad still on screen: the laptop has no session, so begin one.
        if (screen == Screen.ANNOTATE && !annotating) goTo(Screen.ANNOTATE)
        val current = link
        when {
            // Restarted rather than left running: the tick waiting may be a slow one from the background.
            current != null -> if (current.connected) startPinger()

            screen == Screen.RECONNECTING -> scheduleRetry(0L)

            screen != Screen.ONBOARDING && settings.reconnect -> rememberedTarget()?.let(::connect)
        }
        if (screen == Screen.RECONNECTING) handler.post(ticker)
        refreshPicker()
        startScan()
        val sinceCheck = System.currentTimeMillis() - settings.lastUpdateCheck
        if (settings.autoUpdateCheck && sinceCheck >= Updates.CHECK_EVERY_MS) checkForUpdate()
    }

    override fun onStop() {
        super.onStop()
        started = false
        releaseHeldInput()
        // The laptop drops its overlay when told; a rotation rebuild begins a fresh session in onCreate.
        endAnnotate()
        handler.removeCallbacks(retry)
        handler.removeCallbacks(quickRetry)
        handler.removeCallbacks(ticker)
        stopScan()
        // A detour keeps the link for a while, and a rebuild keeps it outright; see the class comment.
        if (!isChangingConfigurations && link != null) handler.postDelayed(linger, LINGER_MS)
    }

    override fun onDestroy() {
        super.onDestroy()
        // A rebuilt activity's waiting ticks go with it; the new one starts its own in onStart.
        handler.removeCallbacksAndMessages(null)
        if (isChangingConfigurations) return
        val current = link
        link = null
        current?.close(LaptopLink.Closure(LaptopLink.Cause.ENDED))
    }

    /** Tells the laptop to take its overlay down, once per session, and not at all when there is no link to tell. */
    private fun endAnnotate() {
        if (!annotating) return
        annotating = false
        link?.send(ActionId.ANNOTATE_END.frame())
    }

    /**
     * Lets go of every key, button and stick a finger is holding on the screen, by handing the screen the
     * cancel a finger taken away mid-press produces. The platform usually sends one itself when the app loses
     * the screen; this does not rely on it. Before 3.2 it did not matter, because the link closed on the spot
     * and the laptop let go of everything with it. A link kept through a detour would instead leave a held
     * key repeating on the laptop for as long as the detour lasted.
     */
    private fun releaseHeldInput() {
        val now = SystemClock.uptimeMillis()
        val cancel = MotionEvent.obtain(now, now, MotionEvent.ACTION_CANCEL, 0f, 0f, 0)
        window.decorView.dispatchTouchEvent(cancel)
        cancel.recycle()
    }

    private fun startPinger() {
        handler.removeCallbacks(pinger)
        handler.post(pinger)
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
        if (requestCode != REQUEST_BLUETOOTH && requestCode != REQUEST_LOCAL_NETWORK) return
        permissionPending = false
        // Refused as well as granted: a refusal changes what the list offers, from nothing to a way back.
        refreshPicker()
        // Browsing does nothing until the permission is held, so it begins now that it may be.
        startScan()
    }

    /** Begins looking for laptops on the network, if the Devices list is what is showing. */
    private fun startScan() {
        if (!started || screen != Screen.PAIRING) return
        handler.removeCallbacks(rescan)
        handler.post(rescan)
    }

    private fun stopScan() {
        handler.removeCallbacks(rescan)
        discovery?.stop()
        probe?.stop()
        probe = null
        wifiFound = emptyList()
        usbFound = emptyList()
        sweep = null
        usbCable = false
    }

    /**
     * One pass of the network probe on its own thread; a pass still running is left to finish. One that [stopScan]
     * abandoned is no longer [probe], so what it found goes nowhere and the next pass need not wait for it.
     */
    private fun probeTether() {
        if (probe != null || !LocalNetwork.granted(this)) return
        val pass = TetherProbe()
        probe = pass
        val mdnsFound = wifiFound.isNotEmpty()
        thread(name = "edgepad-probe") {
            val result = pass.run(mdnsFound = mdnsFound)
            handler.post {
                if (probe === pass) {
                    probe = null
                    if (started && screen == Screen.PAIRING) {
                        usbFound = result.found
                        sweep = result
                        refreshPicker()
                    }
                }
            }
        }
    }

    private fun goTo(next: Screen) {
        // The virtual controller is plugged in for exactly as long as the gamepad is on screen. Asked for
        // here rather than inside the screen because leaving it is also a way of arriving somewhere else,
        // and because a preset change rebuilds the same screen: that is not a trip out and back.
        if (screen == Screen.GAMEPAD && next != Screen.GAMEPAD) link?.send(ActionId.PAD_DETACH.frame())
        if (next == Screen.GAMEPAD && screen != Screen.GAMEPAD) link?.send(ActionId.PAD_ATTACH.frame())
        // Annotating is a session on the laptop: begun on the way in, ended on the way out by any route.
        if (screen == Screen.ANNOTATE && next != Screen.ANNOTATE) endAnnotate()
        if (next == Screen.ANNOTATE && !annotating && link?.connected == true) {
            annotating = true
            link?.send(ActionId.ANNOTATE_BEGIN.frame())
        }
        // Onto a screen that shows the round trip, the pings speed up now rather than at the next slow tick.
        if (next in showsRtt && screen !in showsRtt && link?.connected == true) startPinger()
        screen = next
        surface = null
        gamepad = null
        annotate = null
        reconnecting = null
        macros = null
        controls = null
        handler.removeCallbacks(ticker)
        if (next == Screen.PAIRING) startScan() else stopScan()
        val view: View =
            when (next) {
                Screen.ONBOARDING -> {
                    GuideScreen.build(ui, settings, intro = true, onFinish = {
                        settings.onboarded = true
                        goTo(Screen.PAIRING)
                        if (settings.reconnect) rememberedTarget()?.let(::connect)
                    }) { navigateBack() }
                }

                Screen.GUIDE -> {
                    GuideScreen.build(ui, settings, intro = false, onFinish = {}) { navigateBack() }
                }

                Screen.PAIRING -> {
                    picker.view.also { refreshPicker() }
                }

                Screen.SETTINGS -> {
                    val backLabel =
                        when (settingsBack()) {
                            Screen.SURFACE -> R.string.settings_surface
                            Screen.SURFACE_SETTINGS -> R.string.settings_surface_title
                            else -> R.string.pairing_title
                        }
                    SettingsScreen.build(
                        ui,
                        settings,
                        connectionInfo(),
                        showAllDevices,
                        versionLine(),
                        getString(backLabel),
                        SettingsScreen.Update(updateSummary()) { view ->
                            // The summary changes with nobody touching it, so a screen reader is told when it does.
                            view.accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
                            updateSummaryView = view
                        },
                        SettingsScreen.Routes(
                            forget = ::forget,
                            showAll = { all ->
                                showAllDevices = all
                                refreshPicker()
                            },
                            guide = { goTo(Screen.GUIDE) },
                            documentation = { open(getString(R.string.documentation_url)) },
                            update = ::openUpdate,
                            sideways = { on ->
                                settings.landscape = on
                                applyOrientation()
                            },
                            back = { navigateBack() },
                        ),
                    )
                }

                Screen.SURFACE_SETTINGS -> {
                    SurfaceSettingsScreen.build(
                        ui,
                        settings,
                        getString(R.string.settings_surface),
                        SurfaceSettingsScreen.Routes(
                            corners = { goTo(Screen.CORNERS) },
                            gestures = { goTo(Screen.GESTURES) },
                            shapes = { goTo(Screen.SHAPES) },
                            dialFeel = { goTo(Screen.DIAL_FEEL) },
                            mediaLayout = { goTo(Screen.MEDIA_LAYOUT) },
                            appearance = { goTo(Screen.APPEARANCE) },
                            app = ::openSettings,
                            back = { navigateBack() },
                        ),
                    )
                }

                Screen.CORNERS -> {
                    CornersScreen.build(ui, settings, { goTo(Screen.CORNERS) }) { navigateBack() }
                }

                Screen.GESTURES -> {
                    GestureScreen.build(ui, settings) { navigateBack() }
                }

                Screen.SHAPES -> {
                    // Rebuilt rather than re-filled after a deletion: the list and the empty state are two
                    // different pages, and the last shape going takes the screen from one to the other.
                    ShapesScreen.build(
                        ui,
                        settings,
                        state.macros,
                        onDraw = { goTo(Screen.SHAPE_DRAW) },
                        onChanged = { goTo(Screen.SHAPES) },
                        onBack = { navigateBack() },
                    )
                }

                Screen.SHAPE_DRAW -> {
                    ShapeDrawScreen.build(
                        ui,
                        settings,
                        state.macros,
                        onSaved = { goTo(Screen.SHAPES) },
                        onBack = { navigateBack() },
                    )
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

                Screen.CAPTURE -> {
                    CaptureScreen(ui, settings, state, { frame -> link?.send(frame) }) { navigateBack() }.view
                }

                Screen.ANNOTATE -> {
                    AnnotateScreen(ui, state, { frame -> link?.send(frame) }) { navigateBack() }
                        .also { annotate = it }
                        .view
                }

                Screen.KEYBOARD -> {
                    KeyboardScreen.build(
                        ui,
                        settings.keyTextScale,
                        onKey = ::key,
                        onSettings = { goTo(Screen.KEYBOARD_SETTINGS) },
                    ) { navigateBack() }
                }

                Screen.KEYBOARD_SETTINGS -> {
                    KeyboardSettingsScreen.build(ui, settings) { navigateBack() }
                }

                Screen.GAMEPAD -> {
                    GamepadScreen(
                        ui,
                        gamepads.current,
                        state.padStatus,
                        onKey = ::key,
                        onPad = { pad -> link?.send(pad) },
                        onBack = { navigateBack() },
                        onSettings = { goTo(Screen.GAMEPAD_SETTINGS) },
                    ).also { gamepad = it }.view
                }

                Screen.GAMEPAD_SETTINGS -> {
                    GamepadSettingsScreen.build(
                        ui,
                        gamepads.all.map { it.name },
                        gamepads.current.name,
                        onPick = { name ->
                            gamepads.choose(name)
                            goTo(Screen.GAMEPAD_SETTINGS)
                        },
                        onEdit = { goTo(Screen.GAMEPAD_LAYOUT) },
                    ) { navigateBack() }
                }

                Screen.MACROS -> {
                    MacroScreen(
                        ui,
                        settings,
                        state.macros,
                        icon = state::macroIcon,
                        labels = settings.macroLabels,
                        onRun = ::runMacro,
                        onSettings = { goTo(Screen.MACRO_SETTINGS) },
                    ) { navigateBack() }.also { macros = it }.view
                }

                Screen.MACRO_SETTINGS -> {
                    MacroSettingsScreen.build(ui, settings) { navigateBack() }
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

                Screen.PIN -> {
                    PinScreen(
                        ui,
                        pinAttemptsLeft,
                        pinAnswered,
                        onAnswer = { pin ->
                            pinAnswered = true
                            link?.answerPin(pin)
                        },
                        onCancel = { navigateBack() },
                    ).view
                }

                Screen.PAIR_CODE -> {
                    PairCodeScreen(
                        ui,
                        pairCode?.display().orEmpty(),
                        laptopName,
                        pairDeadline,
                        pairAsking,
                        onYes = { link?.answerPairing(true) },
                        onCancel = { navigateBack() },
                    ).view
                }

                Screen.SURFACE -> {
                    ControlSurface(
                        this,
                        settings,
                        state,
                        { frame -> link?.send(frame) },
                        { goTo(Screen.SURFACE_SETTINGS) },
                        onOpenControls = ::openControls,
                    ).also { surface = it }
                }
            }
        // No screenshot, screen recording or recents thumbnail of the PIN or pairing code, and only of those screens.
        if (next == Screen.PIN || next == Screen.PAIR_CODE) {
            window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        } else {
            window.clearFlags(WindowManager.LayoutParams.FLAG_SECURE)
        }
        setContentView(view)
        applyOrientation()
        // The bars are see-through over the page, whose own background is the ground, so they take its colour.
        // Left to the default, three-button navigation would lay a translucent scrim over the ground instead.
        window.isNavigationBarContrastEnforced = false
        window.insetsController?.let { bars ->
            val light =
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or
                    WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS
            bars.setSystemBarsAppearance(if (ui.palette.dark) 0 else light, light)
            // The surface and everything laid out to match it hide the bars, so the proportions agree.
            if (next in immersive) {
                bars.hide(WindowInsets.Type.systemBars())
                bars.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            } else {
                bars.show(WindowInsets.Type.systemBars())
            }
        }
        updateBack()
    }

    /**
     * Two answers, not three. The keyboard, the gamepad and its layout editor are drawn sideways and only
     * sideways. Every other screen is held the way the Orientation setting says, so a phone turned mid-edit
     * cannot quietly change a media layout that is stored per orientation since 2.3.0, and a shape is never
     * drawn at a different pad proportion from the one it is saved for. A change rebuilds the activity; the
     * link survives it.
     */
    private fun applyOrientation() {
        requestedOrientation =
            when {
                screen in alwaysSideways -> ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
                settings.landscape -> ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
                else -> ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
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
                goTo(settingsBack())
            }

            Screen.RECONNECTING -> {
                stopRetrying()
                goTo(Screen.PAIRING)
            }

            // The surface's own pages go back to the surface's settings, which they are reached from.
            Screen.CORNERS, Screen.GESTURES, Screen.DIAL_FEEL, Screen.APPEARANCE, Screen.MEDIA_LAYOUT,
            Screen.SHAPES,
            -> {
                goTo(Screen.SURFACE_SETTINGS)
            }

            Screen.GUIDE -> {
                goTo(Screen.SETTINGS)
            }

            Screen.SHAPE_DRAW -> {
                goTo(Screen.SHAPES)
            }

            Screen.GAMEPAD_LAYOUT -> {
                goTo(Screen.GAMEPAD_SETTINGS)
            }

            Screen.KEYBOARD_SETTINGS -> {
                goTo(Screen.KEYBOARD)
            }

            Screen.GAMEPAD_SETTINGS -> {
                goTo(Screen.GAMEPAD)
            }

            Screen.MACRO_SETTINGS -> {
                goTo(Screen.MACROS)
            }

            // Back is Cancel: the attempt ends, and onClosed takes the screen to Devices.
            Screen.PIN, Screen.PAIR_CODE -> {
                val current = link
                if (current != null) {
                    current.close(LaptopLink.Closure(LaptopLink.Cause.ENDED))
                } else {
                    goTo(Screen.PAIRING)
                }
            }

            // These are opened from the Controls popup over the surface, so back returns to it.
            Screen.KEYBOARD, Screen.GAMEPAD, Screen.MACROS, Screen.CAPTURE, Screen.ANNOTATE -> {
                goTo(if (link?.connected == true) Screen.SURFACE else Screen.PAIRING)
            }

            // Opened from the surface, which is only there while the link holds.
            Screen.SURFACE_SETTINGS -> {
                goTo(if (link?.connected == true) Screen.SURFACE else Screen.PAIRING)
            }

            else -> {
                return false
            }
        }
        return true
    }

    /**
     * Where back from Settings leads, which its back link is also named for: the surface's settings or the
     * Devices list it was opened from, or the surface once a link made from there holds, and the Devices
     * list once it no longer does.
     */
    private fun settingsBack(): Screen =
        if (settingsReturn == Screen.SURFACE && link?.connected != true) Screen.PAIRING else settingsReturn

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
        // A cancelled picker is the user changing their mind, and needs no answer.
        if (requestCode != REQUEST_IMAGE || resultCode != RESULT_OK) return
        val uri = data?.data
        if (uri == null) {
            // The picker said yes and handed back nothing, which some providers do. Silence would read as success.
            imageRefused(R.string.image_unreadable)
            return
        }
        thread(name = "edgepad-image") {
            val refusal = importImage(uri)
            runOnUiThread {
                // A rotation mid-copy rebuilt the activity; the setting is saved, and the new one reads it.
                if (isDestroyed) return@runOnUiThread
                if (refusal != 0) {
                    imageRefused(refusal)
                } else if (screen == Screen.APPEARANCE) {
                    goTo(Screen.APPEARANCE)
                }
            }
        }
    }

    /**
     * Copies a picked image into app storage, on the caller's thread, which is never the UI's: a photo is
     * megabytes, and the provider behind the Uri may be a cloud drive. Returns 0, or the message saying why
     * nothing changed.
     *
     * The copy goes to a file beside the real one and is renamed over it only once it is whole and decodes,
     * so a copy that fails half way, or a file that is no picture at all, leaves the previous image exactly
     * as it was. Until 3.2 the old file was emptied before the copy began and deleted when it failed, while
     * the setting went on saying IMAGE. Decoding here also leaves [BackgroundImage] holding the picture, so
     * the screen that shows it next finds it ready.
     */
    private fun importImage(uri: Uri): Int {
        val target = settings.backgroundImage
        val part = File(target.path + PART_SUFFIX)
        val refusal =
            try {
                val copied =
                    contentResolver.openInputStream(uri)?.use { from ->
                        part.outputStream().use { to -> copyCapped(from, to) }
                    }
                when {
                    copied == null -> R.string.image_unreadable
                    !copied -> R.string.image_too_large
                    BackgroundImage.load(part) == null -> R.string.image_unreadable
                    !part.renameTo(target) -> R.string.image_unreadable
                    else -> 0
                }
            } catch (e: IOException) {
                Log.w("Edgepad", "Could not copy the picked image", e)
                R.string.image_unreadable
            } catch (e: SecurityException) {
                // The provider took back its grant between the pick and the copy.
                Log.w("Edgepad", "Could not read the picked image", e)
                R.string.image_unreadable
            }
        if (refusal == 0) settings.background = Settings.Background.IMAGE else part.delete()
        return refusal
    }

    /** Copies at most [MAX_IMAGE_MB] and says whether that was all of it. */
    private fun copyCapped(
        from: InputStream,
        to: OutputStream,
    ): Boolean {
        val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
        var total = 0L
        while (true) {
            val read = from.read(buffer)
            if (read < 0) return true
            total += read
            if (total > MAX_IMAGE_MB * BYTES_PER_MB) return false
            to.write(buffer, 0, read)
        }
    }

    private fun imageRefused(message: Int) {
        val text =
            when (message) {
                R.string.image_too_large -> getString(R.string.image_too_large, MAX_IMAGE_MB)
                else -> getString(message)
            }
        Toast.makeText(this, text, Toast.LENGTH_LONG).show()
    }

    /** The laptop's answer to SIGN_SAVE, as the toast the person is waiting for. */
    private fun signed(status: SignStatus?) {
        val text =
            when (status) {
                SignStatus.SAVED -> R.string.sign_saved
                SignStatus.EMPTY -> R.string.sign_empty
                else -> R.string.sign_failed
            }
        Toast.makeText(this, text, Toast.LENGTH_LONG).show()
    }

    /** A key pressed or released on the keyboard or gamepad screen. */
    private fun key(
        code: Int,
        down: Boolean,
    ) {
        link?.send(Frame.Key(code, down))
    }

    /**
     * Runs the laptop's macro in slot [index]. The frame carries that index and nothing else — never a path,
     * a command line or a URL. The laptop alone decides what a slot launches, so a phone that is lost,
     * borrowed or tampered with can only ask for something its owner already set up there. Sending the target
     * would look like a simplification and would hand any phone on the link arbitrary execution.
     */
    private fun runMacro(index: Int) {
        link?.send(Frame.RunAction(ActionId.MACRO_BASE.id + index))
    }

    private fun controlsCapture() = Feature.CAPTURE in state.features

    private fun controlsAnnotate() = Feature.ANNOTATE in state.features && state.screenSize != null

    /** Opens the Controls popup over the surface. A tile closes it and opens that control. */
    private fun openControls() {
        val owner = surface ?: return
        controls?.dismiss()
        controls =
            ControlsPopup(ui, settings) { target ->
                when (target) {
                    ControlsPopup.Target.KEYBOARD -> goTo(Screen.KEYBOARD)
                    ControlsPopup.Target.GAMEPAD -> goTo(Screen.GAMEPAD)
                    ControlsPopup.Target.MACROS -> openMacros()
                    ControlsPopup.Target.CAPTURE -> goTo(Screen.CAPTURE)
                    ControlsPopup.Target.ANNOTATE -> goTo(Screen.ANNOTATE)
                }
            }.also { it.show(owner, controlsCapture(), controlsAnnotate()) }
    }

    /** Opens the macro grid. Its pictures are already cached: the link asks for them as each macro list arrives. */
    private fun openMacros() {
        goTo(Screen.MACROS)
    }

    private fun versionLine(): String =
        getString(R.string.settings_version, getString(R.string.app_version), ProtocolConstants.VERSION)

    private fun openSettings() {
        if (screen != Screen.SETTINGS) settingsReturn = screen
        goTo(Screen.SETTINGS)
    }

    /**
     * Asks GitHub which release is newest, off the main thread, and remembers the answer. The answer is
     * stored from the worker so that it survives a rotation mid-check, which the row's text does not.
     */
    private fun checkForUpdate() {
        if (checkingUpdate) return
        checkingUpdate = true
        updateCheckFailed = false
        updateSummaryView?.text = updateSummary()
        thread(name = "edgepad-update") {
            val latest = Updates.latest()
            if (latest != null) {
                settings.latestVersion = latest
                settings.lastUpdateCheck = System.currentTimeMillis()
            }
            runOnUiThread {
                checkingUpdate = false
                updateCheckFailed = latest == null
                updateSummaryView?.text = updateSummary()
            }
        }
    }

    private fun updateAvailable(): Boolean = Updates.isNewer(settings.latestVersion, getString(R.string.app_version))

    /** What the Updates row says. Empty until GitHub has answered once, because nothing is known before that. */
    private fun updateSummary(): String =
        when {
            updateProgress != null -> getString(R.string.updates_downloading, updateProgress)
            checkingUpdate -> getString(R.string.updates_checking)
            updateCheckFailed -> getString(R.string.updates_failed)
            updateAvailable() -> getString(R.string.updates_available, settings.latestVersion)
            settings.lastUpdateCheck == 0L -> ""
            else -> getString(R.string.updates_current)
        }

    /** The Updates row: fetches the newest release when there is one to get, and otherwise checks afresh. */
    private fun openUpdate() {
        when {
            updateProgress != null -> Unit
            updateAvailable() -> installUpdate(settings.latestVersion)
            else -> checkForUpdate()
        }
    }

    /**
     * Downloads [version] and hands it to the system. Once that is done the row goes back to offering the
     * update: what happens next is the system's question or the app being replaced, and a refusal is
     * reported by [Installer.StatusReceiver], so there is nothing more for the row to wait on.
     */
    private fun installUpdate(version: String) {
        updateProgress = 0
        updateSummaryView?.text = updateSummary()
        thread(name = "edgepad-update") {
            val handed =
                Installer.install(applicationContext, version) { percent ->
                    runOnUiThread {
                        updateProgress = percent
                        updateSummaryView?.text = updateSummary()
                    }
                }
            runOnUiThread {
                updateProgress = null
                updateSummaryView?.text = updateSummary()
                if (!handed && !isDestroyed) {
                    Toast.makeText(this, R.string.updates_download_failed, Toast.LENGTH_LONG).show()
                }
            }
        }
    }

    /**
     * A page in whatever browser the phone has: the documentation site, which the laptop's tray menu also
     * offers so the two halves point at one page rather than each explaining itself, or the newest release.
     */
    private fun open(url: String) {
        try {
            startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
        } catch (e: ActivityNotFoundException) {
            // A phone with no browser at all. Nothing to fall back to, and a settings row must not
            // take the app down with it.
            Log.w("Edgepad", "No activity to open $url", e)
        }
    }

    private fun refreshPicker() {
        val adapter = getSystemService(BluetoothManager::class.java)?.adapter
        val permitted = checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED
        // Asked for once by itself, where it used to be asked for on every refresh; after a refusal the
        // Devices list offers the way back instead, which after "don't ask again" is the app's settings page,
        // since asking again would then be refused without a word.
        if (adapter != null && adapter.isEnabled && !permitted && !askedPermission) askPermission()
        val paired = bonded()
        val laptops = paired.filter { isComputer(it) || it.address == settings.laptop }
        val shown = if (showAllDevices) paired else laptops
        if (!permissionPending && !askedLocalNetwork && LocalNetwork.required && !LocalNetwork.granted(this)) {
            askLocalNetwork()
        }
        val network = mergeFound(wifiFound + usbFound)
        val hint =
            pickerHint(
                permissionPending = permissionPending,
                bluetoothOn = adapter?.isEnabled ?: true,
                bluetoothPermitted = permitted || adapter == null,
                localNetworkGranted = LocalNetwork.granted(this),
                usbCable = usbCable,
                usbUp = sweep?.let { it.usb != null },
                anyDevices = network.isNotEmpty() || shown.isNotEmpty(),
            )
        val devices =
            (shown.map { PickerScreen.Device(it.address, nameOf(it)) } + network.map { deviceOf(it, network) })
                .sortedBy { it.name.lowercase() }
        picker.showDevices(
            devices,
            settings.laptop,
            if (link?.connected == true) laptopAddress else null,
            settings.reconnect,
            hidden = paired.size - laptops.size,
            showingAll = showAllDevices,
        )
        when (hint) {
            // First, because Wi-Fi and USB laptops cannot be found without it.
            PickerHint.LOCAL_NETWORK -> {
                if (shouldShowRequestPermissionRationale(LocalNetwork.PERMISSION)) {
                    val allow = getString(R.string.allow_local_network)
                    picker.setMessage(getString(R.string.local_network_needed), allow) { askLocalNetwork() }
                } else {
                    picker.setMessage(getString(R.string.local_network_needed), getString(R.string.open_app_settings)) {
                        openAppSettings()
                    }
                }
            }

            PickerHint.USB_TETHER_OFF -> {
                picker.setMessage(getString(R.string.usb_cable_tether_off), getString(R.string.usb_tethering)) {
                    openTetherSettings()
                }
            }

            PickerHint.BLUETOOTH_OFF -> {
                picker.setMessage(getString(R.string.bluetooth_off), getString(R.string.open_bluetooth_settings)) {
                    openBluetoothSettings()
                }
            }

            PickerHint.BLUETOOTH_PERMISSION -> {
                if (shouldShowRequestPermissionRationale(Manifest.permission.BLUETOOTH_CONNECT)) {
                    picker.setMessage(getString(R.string.permission_needed), getString(R.string.allow_permission)) {
                        askPermission()
                    }
                } else {
                    picker.setMessage(getString(R.string.permission_needed), getString(R.string.open_app_settings)) {
                        openAppSettings()
                    }
                }
            }

            PickerHint.NOTHING_FOUND -> {
                if (paired.isEmpty()) {
                    picker.setMessage(getString(R.string.no_paired), getString(R.string.open_bluetooth_settings)) {
                        openBluetoothSettings()
                    }
                } else {
                    // Paired devices, none of them claiming to be a computer: the list's own button shows them all.
                    picker.setMessage(getString(R.string.no_laptop_paired))
                }
            }

            PickerHint.NONE -> {
                picker.setMessage("")
            }
        }
        picker.setStatus(sweepStatus())
    }

    /** What the last sweep saw, in one line; it is also the diagnostic, as this machine has no adb. */
    private fun sweepStatus(): String {
        val last = sweep ?: return ""
        val wifi = last.wifi?.let { getString(R.string.status_wifi, it) } ?: getString(R.string.status_no_wifi)
        val usb =
            when {
                last.usb != null -> getString(R.string.status_usb_on, last.usb)
                usbCable -> getString(R.string.status_usb_off)
                else -> null
            }
        return listOfNotNull(wifi, usb).joinToString(" ")
    }

    /**
     * Whether a USB cable is in, from the sticky `USB_STATE` broadcast. A null receiver only reads the last one, so
     * nothing is left registered and the exported flag, which is for receivers and needs API 33, does not apply.
     */
    private fun usbCableIn(): Boolean {
        val state = registerReceiver(null, IntentFilter("android.hardware.usb.action.USB_STATE"))
        return state?.getBooleanExtra("connected", false) == true
    }

    private fun askPermission() {
        askedPermission = true
        permissionPending = true
        requestPermissions(arrayOf(Manifest.permission.BLUETOOTH_CONNECT), REQUEST_BLUETOOTH)
    }

    private fun openBluetoothSettings() {
        startActivity(Intent(android.provider.Settings.ACTION_BLUETOOTH_SETTINGS))
    }

    /** This app's page in the system settings, where a permission refused for good can still be granted. */
    private fun openAppSettings() {
        val intent =
            Intent(
                android.provider.Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                Uri.fromParts("package", packageName, null),
            )
        try {
            startActivity(intent)
        } catch (e: ActivityNotFoundException) {
            Log.w("Edgepad", "No activity to open the app's settings", e)
        }
    }

    /**
     * Every paired device, of every kind. The Devices list narrows it to computers for itself; everything
     * that connects looks through all of them, so a laptop reached through "show all" once is still found
     * by the reconnect that follows, however its Bluetooth class reads.
     */
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

    /** Whether [device] says it is a computer: headsets, watches, mice and keyboards are paired too. */
    private fun isComputer(device: BluetoothDevice): Boolean =
        try {
            device.bluetoothClass?.majorDeviceClass == BluetoothClass.Device.Major.COMPUTER
        } catch (e: SecurityException) {
            false
        }

    private fun rememberedDevice(): BluetoothDevice? {
        val address = settings.laptop ?: return null
        return bonded().firstOrNull { it.address == address }
    }

    /** The network laptop last connected to, or else the remembered Bluetooth one if it is still bonded. */
    private fun rememberedTarget(): Target? =
        lastTarget as? Target.Network ?: rememberedDevice()?.let { Target.Bluetooth(it) }

    private fun nameOf(device: BluetoothDevice): String =
        try {
            device.name ?: device.address
        } catch (e: SecurityException) {
            device.address
        }

    /** A row for a laptop found on the network; [all] is every row, to say where else the same laptop is. */
    private fun deviceOf(
        found: Found,
        all: List<Found>,
    ): PickerScreen.Device {
        val host =
            found.hosts
                .firstOrNull()
                ?.hostAddress
                .orEmpty()
        return PickerScreen.Device(
            address = targetOf(found).key,
            name = found.name,
            transport = found.transport,
            detail = host,
            found = found,
            alsoVia = otherTransport(found, all),
        )
    }

    private fun targetOf(found: Found) = Target.Network(found.laptopId, found.name, found.hosts, found.port)

    private fun connectTo(device: PickerScreen.Device) {
        // A tap starts a fresh set of attempts; onConnected no longer does, so a link that drops at once cannot.
        attempts = 0
        val found = device.found
        if (found != null) {
            connect(targetOf(found))
            quickRetryLeft = true
            return
        }
        val bonded = bonded().firstOrNull { it.address == device.address }
        if (bonded == null) {
            picker.setMessage(getString(R.string.not_paired))
        } else {
            connect(Target.Bluetooth(bonded))
            quickRetryLeft = true
        }
    }

    private fun askLocalNetwork() {
        askedLocalNetwork = true
        permissionPending = true
        requestPermissions(arrayOf(LocalNetwork.PERMISSION), REQUEST_LOCAL_NETWORK)
    }

    /** "Add by address": a host and optional port typed by hand, asked INFO of off the main thread, then connected. */
    private fun askAddress() {
        val field =
            EditText(this).apply {
                setSingleLine()
                inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI
                hint = getString(R.string.add_by_address_hint)
                importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
                val pad = ui.dp(Space.L)
                setPadding(pad, pad, pad, pad)
            }
        AlertDialog
            .Builder(this)
            .setTitle(R.string.add_by_address)
            .setView(field)
            .setPositiveButton(android.R.string.ok) { _, _ ->
                val address = parseHostPort(field.text.toString())
                if (address == null) {
                    picker.setMessage(getString(R.string.address_invalid))
                } else {
                    probeManual(address)
                }
            }.setNegativeButton(android.R.string.cancel, null)
            .showOver(picker.view)
    }

    private fun probeManual(address: HostPort) {
        picker.setMessage("")
        thread(name = "edgepad-probe-address") {
            val found = probeAddress(address)
            handler.post {
                if (found == null) {
                    picker.setMessage(getString(R.string.address_no_laptop))
                } else {
                    attempts = 0
                    connect(targetOf(found))
                    quickRetryLeft = true
                }
            }
        }
    }

    /** The system's tethering page, where USB tethering is switched on; failing that, the wireless settings. */
    private fun openTetherSettings() {
        for (action in listOf("android.settings.TETHER_SETTINGS", android.provider.Settings.ACTION_WIRELESS_SETTINGS)) {
            try {
                startActivity(Intent(action))
                return
            } catch (e: ActivityNotFoundException) {
                Log.w("Edgepad", "No activity for $action", e)
            }
        }
    }

    /** One link at a time: a connect ends whatever link there was first. */
    private fun connect(target: Target) {
        handler.removeCallbacks(retry)
        handler.removeCallbacks(quickRetry)
        quickRetryLeft = false
        handler.removeCallbacks(pinger)
        val old = link
        link = null
        old?.close(LaptopLink.Closure(LaptopLink.Cause.ENDED))
        // What one laptop reported says nothing about another.
        if (target.key != laptopAddress) state = LaptopState()
        state.forgetFeatures()
        // A retry resumes: "Pair again" is the user's choice for one attempt, never something a retry repeats.
        lastTarget =
            if (target is Target.Network && target.pairAgain) {
                Target.Network(target.laptopId, target.name, target.hosts, target.port)
            } else {
                target
            }
        laptopName =
            when (target) {
                is Target.Bluetooth -> {
                    settings.laptop = target.device.address
                    nameOf(target.device)
                }

                is Target.Network -> {
                    target.name
                }
            }
        laptopAddress = target.key
        rtt.clear()
        picker.setRtt(Double.NaN)
        picker.setMessage("")
        picker.setConnecting(laptopAddress)
        val next = LaptopLink(transportFor(target), this)
        link = next
        thread(name = "edgepad-link") { next.open() }
    }

    private fun transportFor(target: Target): LinkTransport =
        when (target) {
            is Target.Bluetooth -> {
                BluetoothTransport(target.device)
            }

            is Target.Network -> {
                TcpTransport(target.hosts, target.port, target.laptopId, linkKeys, phoneName(), target.pairAgain)
            }
        }

    /** What the laptop's pairing prompt calls this phone: the name set in the phone's settings, else its model. */
    private fun phoneName(): String = Global.getString(contentResolver, Global.DEVICE_NAME) ?: Build.MODEL

    override fun onConnected(link: LaptopLink) {
        if (link != this.link) return
        connectedAt = SystemClock.elapsedRealtime()
        pairCode = null
        pairAsking = false
        picker.setConnecting(null)
        // The trust hint, if it was showing, has been answered.
        picker.setMessage("")
        startPinger()
        when (screen) {
            Screen.PAIRING, Screen.RECONNECTING, Screen.PIN, Screen.PAIR_CODE -> {
                goTo(Screen.SURFACE)
            }

            // Opened from Devices, back now leads to the surface the connect would have gone to.
            Screen.SETTINGS -> {
                if (settingsReturn == Screen.PAIRING) settingsReturn = Screen.SURFACE
            }

            else -> {}
        }
    }

    override fun onAwaitingLaptop(link: LaptopLink) {
        if (link == this.link) picker.setMessage(getString(R.string.status_accept_on_laptop))
    }

    /**
     * From any screen: the laptop waits for this answer and gives up on its own. While the PIN screen is up
     * the attempt's link is set, so no retry can start a second connect behind it ([attemptReconnect]).
     */
    override fun onPinRequest(
        link: LaptopLink,
        attemptsLeft: Int,
    ) {
        if (link != this.link) return
        pinAttemptsLeft = attemptsLeft
        pinAnswered = false
        // The trust hint, if it was showing, has been answered.
        picker.setMessage("")
        goTo(Screen.PIN)
    }

    /** A network pairing waits on the laptop's owner typing [code] there; the countdown matches the link's wait. */
    override fun onPairingCode(
        link: LaptopLink,
        code: Sas,
    ) {
        if (link != this.link) return
        pairCode = code
        pairDeadline = SystemClock.elapsedRealtime() + PAIR_CODE_MS
        pairAsking = false
        picker.setMessage("")
        goTo(Screen.PAIR_CODE)
    }

    /** The laptop answered the pairing; only the owner's Yes on this screen keeps it ([LaptopLink.answerPairing]). */
    override fun onPairingConfirm(link: LaptopLink) {
        if (link != this.link) return
        pairAsking = true
        goTo(Screen.PAIR_CODE)
    }

    override fun onRoundTrip(ms: Double) {
        rtt.add(ms)
        picker.setRtt(rtt.median())
    }

    override fun onFrame(frame: Frame) {
        if (!state.take(frame)) return
        surface?.stateChanged()
        // Capture is drawn from the features the laptop named, which it sends once after the handshake: one that
        // lands while it is open rebuilds it rather than leaving it saying "none". The Controls popup is redrawn.
        val sized = frame is Frame.Text && (frame.kind == TextKind.FEATURES.id || frame.kind == TextKind.SCREEN_SIZE.id)
        if (screen == Screen.CAPTURE && sized) goTo(screen)
        if (sized) controls?.refresh(controlsCapture(), controlsAnnotate())
        if (frame is Frame.Text && frame.kind == TextKind.SCREEN_SIZE.id) annotate?.refresh()
        if (frame is Frame.Text && frame.kind == TextKind.SIGN_STATUS.id) signed(state.signStatus)
        // The pad decides which of its two modes it is in from this answer, which usually lands a few
        // milliseconds after PAD_ATTACH went out — by then the screen is already up, so it is handed
        // over rather than rebuilt: rebuilding would drop whatever fingers are on the glass.
        if (frame is Frame.Text && frame.kind == TextKind.PAD_STATUS.id) gamepad?.setStatus(state.padStatus)
        // The macro grid is handed what changed rather than built again, which would throw away where
        // TalkBack was and decode every picture over again for each one that arrived.
        val grid = macros
        if (grid != null && frame is Frame.Text) {
            when (frame.kind) {
                TextKind.MACROS.id -> {
                    // The link has already asked for this list's pictures; they arrive as MACRO_ICON frames.
                    grid.setMacros(state.macros)
                }

                TextKind.MACRO_ICON.id -> {
                    grid.iconArrived(state.lastIcon)
                }
            }
        }
    }

    override fun onClosed(
        link: LaptopLink,
        closure: LaptopLink.Closure,
    ) {
        if (link != this.link) return
        handler.removeCallbacks(pinger)
        this.link = null
        picker.setConnecting(null)
        pairCode = null
        pairAsking = false
        // Devices is where the reason is shown, so an attempt that ends at the PIN or a pairing code goes there first.
        if (screen == Screen.PIN || screen == Screen.PAIR_CODE) goTo(Screen.PAIRING)
        when {
            closure.cause == LaptopLink.Cause.ENDED -> {
                // A link ended on purpose starts the next one with a full set of attempts.
                attempts = 0
                if (screen == Screen.SURFACE || screen == Screen.RECONNECTING ||
                    screen == Screen.CAPTURE || screen == Screen.ANNOTATE || screen == Screen.KEYBOARD ||
                    screen == Screen.GAMEPAD || screen == Screen.MACROS || screen in settingsOfControls
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
                val now = SystemClock.elapsedRealtime()
                lostAt = now
                if (now - connectedAt >= STABLE_LINK_MS) attempts = 0 else attempts++
                goTo(Screen.RECONNECTING)
                scheduleRetry(RETRY_DELAY_MS)
            }

            screen == Screen.RECONNECTING -> {
                attempts++
                scheduleRetry(RETRY_DELAY_MS)
                updateReconnecting()
            }

            // The first connect of a tap never reached the laptop: once more before saying anything.
            closure.cause == LaptopLink.Cause.UNREACHABLE && quickRetryLeft -> {
                quickRetryLeft = false
                refreshPicker()
                picker.setConnecting(laptopAddress)
                handler.postDelayed(quickRetry, QUICK_RETRY_MS)
            }

            else -> {
                refreshPicker()
                picker.setMessage(getString(R.string.status_closed, describe(closure)))
            }
        }
    }

    /** Why an attempt ended, in the user's language. The link names the cause; the words are the app's. */
    private fun describe(closure: LaptopLink.Closure): String =
        when (closure.cause) {
            LaptopLink.Cause.ENDED, LaptopLink.Cause.LOST -> getString(R.string.link_lost)
            LaptopLink.Cause.UNREACHABLE -> getString(R.string.link_unreachable)
            LaptopLink.Cause.REFUSED -> getString(R.string.link_refused)
            LaptopLink.Cause.MISMATCH -> getString(R.string.link_mismatch, closure.detail, ProtocolConstants.VERSION)
            LaptopLink.Cause.UNEXPECTED -> getString(R.string.link_unexpected, closure.detail)
            LaptopLink.Cause.NO_PERMISSION -> getString(R.string.link_no_permission)
            LaptopLink.Cause.NO_ANSWER -> getString(R.string.link_no_answer)
            LaptopLink.Cause.PIN_LOCKED -> lockedFor(closure.detail.toIntOrNull() ?: 0)
            LaptopLink.Cause.NOT_PAIRED -> getString(R.string.link_not_paired)
            LaptopLink.Cause.NOT_THIS_LAPTOP -> getString(R.string.link_not_this_laptop)
            LaptopLink.Cause.PAIRING_FAILED -> getString(R.string.link_pairing_failed)
            LaptopLink.Cause.PAIRING_LOCKED -> getString(R.string.link_pairing_locked)
        }

    /** The lock-out wait as a person reads it: an hour is 60 minutes, not 3600 s. Minutes round up, never down. */
    private fun lockedFor(seconds: Int): String {
        if (seconds < SECONDS_PER_MINUTE) {
            return resources.getQuantityString(R.plurals.link_pin_locked_seconds, seconds, seconds)
        }
        val minutes = (seconds + SECONDS_PER_MINUTE - 1) / SECONDS_PER_MINUTE
        return resources.getQuantityString(R.plurals.link_pin_locked_minutes, minutes, minutes)
    }

    private fun scheduleRetry(delay: Long) {
        handler.removeCallbacks(retry)
        if (started && settings.reconnect && attempts < MAX_ATTEMPTS) handler.postDelayed(retry, delay)
    }

    private fun attemptReconnect() {
        if (!started || link != null) return
        val target = rememberedTarget()
        if (target == null) {
            // Retrying cannot help once the laptop is unpaired, and a bare "Stopped retrying" would not say why.
            if (screen == Screen.RECONNECTING) goTo(Screen.PAIRING)
            picker.setMessage(getString(R.string.not_paired))
        } else {
            connect(target)
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
            attempt.close(LaptopLink.Closure(LaptopLink.Cause.ENDED))
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
        lastTarget = null
        val current = link
        if (current != null) {
            current.close(LaptopLink.Closure(LaptopLink.Cause.ENDED))
        } else {
            goTo(Screen.SETTINGS)
        }
    }

    private fun connectionInfo(): SettingsScreen.Connection {
        val remembered = settings.laptop ?: return SettingsScreen.Connection(null, "", connected = false)
        val name =
            if (remembered == laptopAddress && laptopName.isNotEmpty()) {
                laptopName
            } else {
                bonded().firstOrNull { it.address == remembered }?.let(::nameOf) ?: remembered
            }
        val median = rtt.median()
        val connected = link?.connected == true
        val detail =
            when {
                connected && !median.isNaN() -> getString(R.string.detail_connected_rtt, median)
                connected -> getString(R.string.detail_connected)
                else -> getString(R.string.detail_remembered)
            }
        return SettingsScreen.Connection(name, detail, connected)
    }

    private companion object {
        val immersive =
            setOf(
                Screen.SURFACE,
                Screen.KEYBOARD,
                Screen.GAMEPAD,
                Screen.CAPTURE,
                Screen.ANNOTATE,
                Screen.MEDIA_LAYOUT,
                Screen.GAMEPAD_LAYOUT,
                Screen.SHAPE_DRAW,
            )

        /** Drawn sideways whatever the phone is doing, because they are laid out for a wide screen and nothing else. */
        val alwaysSideways =
            setOf(
                Screen.KEYBOARD,
                Screen.GAMEPAD,
                Screen.GAMEPAD_LAYOUT,
                // Their settings pages too: a form reached from a sideways screen stays sideways rather than
                // turning the phone upright and back. They show the bars, unlike the screens they came from.
                Screen.KEYBOARD_SETTINGS,
                Screen.GAMEPAD_SETTINGS,
            )

        /** The pages that need the link: with it gone they go back to Devices, as the screens they belong to do. */
        val settingsOfControls = setOf(Screen.KEYBOARD_SETTINGS, Screen.GAMEPAD_SETTINGS, Screen.MACRO_SETTINGS)

        const val REQUEST_BLUETOOTH = 1
        const val REQUEST_IMAGE = 2
        const val REQUEST_LOCAL_NETWORK = 3

        /** How often the Devices list looks again for laptops on the network while it is showing. */
        const val SCAN_EVERY_MS = 10_000L

        /**
         * The screens that show the round trip: Devices beside the connected laptop, live, and Settings on
         * its laptop card. Everywhere else a ping only has to prove the link is still there.
         */
        val showsRtt = setOf(Screen.PAIRING, Screen.SETTINGS)
        const val PING_FAST_MS = 500L
        const val PING_SLOW_MS = 5_000L

        /** How long a link outlives the app leaving the screen: a trip to the picker, a glance at the lock screen. */
        const val LINGER_MS = 30_000L
        const val TICK_MS = 1_000L
        const val RETRY_DELAY_MS = 2_000L

        /** Long enough for the laptop to finish whatever made the first connect miss, short enough to go unnoticed. */
        const val QUICK_RETRY_MS = 1_500L

        /** The pairing code's countdown, the same 90 s the network transport waits for the laptop's answer. */
        const val PAIR_CODE_MS = 90_000L
        const val MAX_ATTEMPTS = 10

        /** A link that lasted less than this did not really come back, so it does not earn a fresh set of attempts. */
        const val STABLE_LINK_MS = 5_000L
        const val MILLIS_PER_SECOND = 1_000L
        const val SECONDS_PER_MINUTE = 60

        /**
         * The largest background image accepted. A phone camera's photo is a few megabytes and a panorama
         * perhaps twenty; past this it is a file that happens to be an image, and it would be decoded, sampled
         * down to the screen and kept, all for a picture the size of the phone.
         */
        const val MAX_IMAGE_MB = 50
        const val BYTES_PER_MB = 1024L * 1024L

        /** The half-copied image, beside the real one until it is whole. */
        const val PART_SUFFIX = ".part"
    }
}
