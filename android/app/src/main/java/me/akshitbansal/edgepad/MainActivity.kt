package me.akshitbansal.edgepad

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.bluetooth.BluetoothClass
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.content.ActivityNotFoundException
import android.content.Context
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
import android.view.ViewGroup
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
import me.akshitbansal.edgepad.link.CaptureInbox
import me.akshitbansal.edgepad.link.Discovery
import me.akshitbansal.edgepad.link.Found
import me.akshitbansal.edgepad.link.HostPort
import me.akshitbansal.edgepad.link.Laptop
import me.akshitbansal.edgepad.link.LaptopConnections
import me.akshitbansal.edgepad.link.LaptopLink
import me.akshitbansal.edgepad.link.LaptopRow
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.LinkTransport
import me.akshitbansal.edgepad.link.LocalNetwork
import me.akshitbansal.edgepad.link.MainScheduler
import me.akshitbansal.edgepad.link.PickerHint
import me.akshitbansal.edgepad.link.SavedLaptop
import me.akshitbansal.edgepad.link.Sweep
import me.akshitbansal.edgepad.link.Target
import me.akshitbansal.edgepad.link.TcpTransport
import me.akshitbansal.edgepad.link.TetherProbe
import me.akshitbansal.edgepad.link.Transport
import me.akshitbansal.edgepad.link.decodeSaved
import me.akshitbansal.edgepad.link.encodeSaved
import me.akshitbansal.edgepad.link.forgotten
import me.akshitbansal.edgepad.link.laptopLinks
import me.akshitbansal.edgepad.link.laptopRows
import me.akshitbansal.edgepad.link.mergeFound
import me.akshitbansal.edgepad.link.net.LinkKeys
import me.akshitbansal.edgepad.link.net.Sas
import me.akshitbansal.edgepad.link.parseHostPort
import me.akshitbansal.edgepad.link.pickerHint
import me.akshitbansal.edgepad.link.probeAddress
import me.akshitbansal.edgepad.link.promoted
import me.akshitbansal.edgepad.link.rememberLaptop
import me.akshitbansal.edgepad.link.snapUp
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
 * The links and what each laptop has reported live in [manager], which outlives the activity across a
 * rotation or a theme change, through [onRetainNonConfigurationInstance]. That matters because changing the
 * theme or the orientation setting rebuilds the activity, and a link dropped there would make every settings
 * change look like a disconnection.
 *
 * Several laptops stay connected at once, and every screen draws and sends to [selected]. A laptop's events
 * reach the screens only while it is selected; any other shows on its row in Devices and nowhere else, so
 * losing it never means RECONNECTING and its PIN or pairing code waits there until the row is tapped. A
 * change of selection goes through [onDeselecting] then [onSelected]: the first lets go of everything on
 * the old laptop while frames still reach it, and the second rebuilds the screen on the new one. A view
 * left over from before a change is built on [boundSend], so nothing late can land on the wrong laptop.
 *
 * The links against the activity's lifecycle, which is the whole of it:
 *
 * - Started (onStart to onStop): the links are used; the selected one is pinged every
 *   [LaptopConnections.PING_FAST_MS] on a screen that shows the round trip, and the rest every
 *   [LaptopConnections.PING_SLOW_MS], where the ping only proves the link is still there.
 * - Stopped, and not for a rebuild: whatever a finger holds is let go at once ([releaseHeldInput]); the
 *   links stay up for [LINGER_MS], pinged slowly. The image picker, Bluetooth settings, the documentation
 *   in a browser and the screen going off are all a stop, and until 3.2 every one of them dropped the link.
 * - Started again inside [LINGER_MS]: nothing happened; the links carry on.
 * - [LINGER_MS] passes: every link closes as [LaptopLink.Cause.ENDED], which is what onStop did at once
 *   before 3.2.
 * - Destroyed, and not for a rebuild (backed out of, or finished): closed at once.
 * - Rebuilt: [manager] is handed over in [Retained] and its events swapped in onCreate. It reads them when
 *   each event is delivered on the main thread, so nothing lands on the old activity.
 */
class MainActivity :
    Activity(),
    LaptopConnections.Events {
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
        val manager: LaptopConnections,
        val screen: Screen,
        val settingsReturn: Screen,
    )

    private val handler = Handler(Looper.getMainLooper())
    private lateinit var settings: Settings
    private lateinit var ui: Ui
    private lateinit var picker: PickerScreen
    private lateinit var manager: LaptopConnections

    /** What the screens read while no laptop is selected. */
    private val noLaptop = LaptopState()
    private val selected: Laptop? get() = manager.selected
    private val state: LaptopState get() = selected?.state ?: noLaptop
    private val connected: Boolean get() = selected?.connected == true
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

    /** True from MACROS_WATCH until the one MACROS_UNWATCH that answers it, however the grid is left. */
    private var watchingMacros = false

    /** The Controls popup over the surface, while it is open. */
    private var controls: ControlsPopup? = null
    private var screen = Screen.PAIRING
    private var settingsReturn = Screen.PAIRING
    private lateinit var gamepads: GamepadStore
    private var started = false

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

    private val linger = Runnable { manager.closeAll(LaptopLink.Cause.ENDED) }

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
                onTap = ::tapDevice,
                onVia = { device, transport -> connectTo(device.row, transport) },
                onMakeActive = { device -> device.row.laptop?.let { manager.select(it.id) } },
                onDisconnect = ::disconnectDevice,
                onForget = { device -> forgetLaptop(device.row.laptop, device.row.keys) },
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
        manager =
            retained?.manager?.also { it.events = this }
                ?: LaptopConnections(laptopLinks(transports(applicationContext)), MainScheduler(), this)
        if (retained != null) {
            settingsReturn = retained.settingsReturn
            // Carried over, or the readout would stand blank after a rotation and then restart from one sample.
            picker.setRtt(selected?.rtt?.median() ?: Double.NaN)
        }
        goTo(
            when {
                !settings.onboarded -> Screen.ONBOARDING
                retained != null -> retained.screen
                else -> Screen.PAIRING
            },
        )
    }

    override fun onRetainNonConfigurationInstance(): Any = Retained(manager, screen, settingsReturn)

    override fun onStart() {
        super.onStart()
        handler.removeCallbacks(linger)
        started = true
        manager.reconnect = settings.reconnect
        // This also starts a lost laptop's retries at once, which is what the reconnect screen wants.
        manager.foreground = true
        // Rising, so the tick waiting, which may be a slow one from the background, does not hold it up.
        manager.fastPing = screen in showsRtt
        // Back from a detour with the pad still on screen: the laptop has no session, so begin one.
        if (screen == Screen.ANNOTATE && !annotating) goTo(Screen.ANNOTATE)
        // The same for the macro grid: onStop told the laptop to stop looking.
        if (screen == Screen.MACROS) watchMacros()
        if (screen != Screen.ONBOARDING && settings.reconnect) connectSaved()
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
        unwatchMacros()
        manager.foreground = false
        manager.fastPing = false
        handler.removeCallbacks(ticker)
        stopScan()
        // A detour keeps the link for a while, and a rebuild keeps it outright; see the class comment.
        if (!isChangingConfigurations && manager.laptops.any { it.link != null }) {
            handler.postDelayed(linger, LINGER_MS)
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        // A rebuilt activity's waiting ticks go with it; the new one starts its own in onStart.
        handler.removeCallbacksAndMessages(null)
        if (!isChangingConfigurations) manager.shutDown()
    }

    /** Tells the laptop to take its overlay down, once per session, and not at all when there is no link to tell. */
    private fun endAnnotate() {
        if (!annotating) return
        annotating = false
        manager.send(ActionId.ANNOTATE_END.frame())
    }

    /** Asks the laptop to say which macro apps are open, if it can tell. One that never said so is never asked. */
    private fun watchMacros() {
        if (watchingMacros || Feature.RUNNING !in state.features || !connected) return
        watchingMacros = true
        manager.send(ActionId.MACROS_WATCH.frame())
    }

    /** Tells the laptop to stop looking, once per watch, and not at all when there is no link to tell. */
    private fun unwatchMacros() {
        if (!watchingMacros) return
        watchingMacros = false
        manager.send(ActionId.MACROS_UNWATCH.frame())
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
        if (screen == Screen.GAMEPAD && next != Screen.GAMEPAD) manager.send(ActionId.PAD_DETACH.frame())
        if (next == Screen.GAMEPAD && screen != Screen.GAMEPAD) manager.send(ActionId.PAD_ATTACH.frame())
        // Annotating is a session on the laptop: begun on the way in, ended on the way out by any route.
        if (screen == Screen.ANNOTATE && next != Screen.ANNOTATE) endAnnotate()
        if (screen == Screen.MACROS && next != Screen.MACROS) unwatchMacros()
        if (next == Screen.MACROS) watchMacros()
        if (next == Screen.ANNOTATE && !annotating && connected) {
            annotating = true
            manager.send(ActionId.ANNOTATE_BEGIN.frame())
        }
        // Onto a screen that shows the round trip, the pings speed up now rather than at the next slow tick.
        manager.fastPing = started && next in showsRtt
        // Settings writes the setting with nothing listening, and every way out of it comes through here. Only on a
        // change, because setting it starts a lost laptop's retry over.
        if (manager.reconnect != settings.reconnect) manager.reconnect = settings.reconnect
        screen = next
        surface = null
        gamepad = null
        annotate = null
        reconnecting = null
        macros = null
        controls = null
        handler.removeCallbacks(ticker)
        if (next == Screen.PAIRING) startScan() else stopScan()
        val send = boundSend()
        val sendKey = { code: Int, down: Boolean -> send(Frame.Key(code, down)) }
        val view: View =
            when (next) {
                Screen.ONBOARDING -> {
                    GuideScreen.build(ui, settings, intro = true, onFinish = {
                        settings.onboarded = true
                        goTo(Screen.PAIRING)
                        if (settings.reconnect) connectSaved()
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
                            Screen.KEYBOARD_SETTINGS -> R.string.keyboard_settings_title
                            Screen.GAMEPAD_SETTINGS -> R.string.gamepad_settings_title
                            Screen.MACRO_SETTINGS -> R.string.macros_settings
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
                            system = ::openSettings,
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
                    CaptureScreen(ui, settings, state, send) { navigateBack() }.view
                }

                Screen.ANNOTATE -> {
                    AnnotateScreen(ui, state, send) { navigateBack() }
                        .also { annotate = it }
                        .view
                }

                Screen.KEYBOARD -> {
                    KeyboardScreen.build(
                        ui,
                        settings.keyTextScale,
                        settings.keyboardSize,
                        settings.functionKeys,
                        state.os,
                        onKey = sendKey,
                        onAction = { send(it.frame()) },
                        onSettings = { goTo(Screen.KEYBOARD_SETTINGS) },
                    ) { navigateBack() }
                }

                Screen.KEYBOARD_SETTINGS -> {
                    KeyboardSettingsScreen.build(ui, settings, onSystem = ::openSettings) { navigateBack() }
                }

                Screen.GAMEPAD -> {
                    GamepadScreen(
                        ui,
                        gamepads.current,
                        state.padStatus,
                        onKey = sendKey,
                        onPad = send,
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
                        onSystem = ::openSettings,
                    ) { navigateBack() }
                }

                Screen.MACROS -> {
                    MacroScreen(
                        ui,
                        settings,
                        state.macros,
                        icon = state::macroIcon,
                        isOpen = state::isOpen,
                        labels = settings.macroLabels,
                        onRun = ::runMacro,
                        onClose = ::closeMacro,
                        onSettings = { goTo(Screen.MACRO_SETTINGS) },
                        onIconSide = ::requestIcons,
                    ) { navigateBack() }.also { macros = it }.view
                }

                Screen.MACRO_SETTINGS -> {
                    MacroSettingsScreen.build(ui, settings, onSystem = ::openSettings) { navigateBack() }
                }

                Screen.RECONNECTING -> {
                    val lost =
                        ReconnectingScreen(
                            ui,
                            selected?.name.orEmpty(),
                            selected?.address.orEmpty(),
                            onRetry = ::retryNow,
                        ) {
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
                        selected?.pinAttemptsLeft ?: 0,
                        selected?.pinAnswered == true,
                        onAnswer = { pin ->
                            selected?.let {
                                it.pinAnswered = true
                                it.link?.answerPin(pin)
                            }
                        },
                        onCancel = { navigateBack() },
                    ).view
                }

                Screen.PAIR_CODE -> {
                    PairCodeScreen(
                        ui,
                        selected?.pairCode?.display().orEmpty(),
                        selected?.name.orEmpty(),
                        selected?.pairDeadline ?: 0L,
                        selected?.pairAsking == true,
                        onYes = { selected?.link?.answerPairing(true) },
                        onCancel = { navigateBack() },
                    ).view
                }

                Screen.SURFACE -> {
                    ControlSurface(
                        this,
                        settings,
                        state,
                        send,
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
        val sideways = screen in alwaysSideways || (screen == Screen.SETTINGS && settingsReturn in alwaysSideways)
        requestedOrientation =
            when {
                sideways || settings.landscape -> ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
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
                val laptop = selected
                if (laptop?.link != null) {
                    manager.disconnect(laptop.id)
                } else {
                    goTo(Screen.PAIRING)
                }
            }

            // These are opened from the Controls popup over the surface, so back returns to it.
            Screen.KEYBOARD, Screen.GAMEPAD, Screen.MACROS, Screen.CAPTURE, Screen.ANNOTATE -> {
                goTo(if (connected) Screen.SURFACE else Screen.PAIRING)
            }

            // Opened from the surface, which is only there while the link holds.
            Screen.SURFACE_SETTINGS -> {
                goTo(if (connected) Screen.SURFACE else Screen.PAIRING)
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
        if ((settingsReturn == Screen.SURFACE || settingsReturn in settingsOfControls) && !connected) {
            Screen.PAIRING
        } else {
            settingsReturn
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

    /**
     * What a screen built now sends through: to [selected], and only while that is still the laptop it was built for.
     * The screen goes when the selection does, but a tick of a dial or a pad already posted would otherwise land on the
     * new laptop; it is dropped instead.
     */
    private fun boundSend(): (Frame) -> Unit {
        val laptop = selected
        return { frame -> if (selected === laptop) manager.send(frame) }
    }

    /**
     * Runs the laptop's macro in slot [index]. The frame carries that index and nothing else — never a path,
     * a command line or a URL. The laptop alone decides what a slot launches, so a phone that is lost,
     * borrowed or tampered with can only ask for something its owner already set up there. Sending the target
     * would look like a simplification and would hand any phone on the link arbitrary execution.
     */
    private fun runMacro(index: Int) {
        manager.send(Frame.RunAction(ActionId.MACRO_BASE.id + index))
    }

    /**
     * The macro grid draws icons [px] across. Asks the laptop for that size, snapped up, only when it is more than
     * this link already asked for: a larger picture draws sharp when shrunk, and a resend costs the link. A new
     * link starts at 128, so the grid asks again after a reconnect, and a call that changes nothing sends nothing.
     */
    private fun requestIcons(px: Int) {
        val current = selected?.link ?: return
        val size = snapUp(px)
        if (!current.connected || size <= current.iconPx) return
        current.iconPx = size
        current.send(TextKind.WANT_ICONS.frame(size.toString()))
    }

    /** Asks the laptop to close the app of macro slot [index]. As in [runMacro], only the index crosses the link. */
    private fun closeMacro(index: Int) {
        manager.send(Frame.RunAction(ActionId.MACRO_CLOSE_BASE.id + index))
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
        val remembered = saved
        val addresses = remembered.map { it.btAddress }
        val laptops = paired.filter { isComputer(it) || it.address in addresses }
        val shown = if (showAllDevices) paired else laptops
        if (!permissionPending && !askedLocalNetwork && LocalNetwork.required && !LocalNetwork.granted(this)) {
            askLocalNetwork()
        }
        val rows =
            laptopRows(
                remembered,
                manager.laptops,
                shown.map { it.address to nameOf(it) },
                mergeFound(wifiFound + usbFound),
            )
        val hint =
            pickerHint(
                permissionPending = permissionPending,
                bluetoothOn = adapter?.isEnabled ?: true,
                bluetoothPermitted = permitted || adapter == null,
                localNetworkGranted = LocalNetwork.granted(this),
                usbCable = usbCable,
                usbUp = sweep?.let { it.usb != null },
                anyDevices = rows.isNotEmpty(),
            )
        picker.showDevices(
            rows.map { PickerScreen.Device(it, it.laptop != null && it.laptop === selected) },
            connected,
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

    /** The laptops to remember, as Settings holds them. */
    private var saved: List<SavedLaptop>
        get() = decodeSaved(settings.savedLaptops)
        set(value) {
            settings.savedLaptops = encodeSaved(value)
        }

    /**
     * Every remembered laptop that connects on its own and has nothing going on, each over the transport it last used,
     * active one first. Lost ones are left to their retries.
     */
    private fun connectSaved() {
        val paired = bonded()
        for (entry in saved.filter { it.autoConnect }) {
            val live = manager.laptops.firstOrNull { it.id in entry.keys }
            if (live != null && !live.idle) continue
            val device = paired.firstOrNull { it.address == entry.btAddress }?.let { Target.Bluetooth(it) }
            val network = entry.networkTarget()
            val target = if (entry.kind == Transport.BLUETOOTH) device ?: network else network ?: device
            target?.let { connect(it, entry.kind.takeUnless { kind -> kind == Transport.BLUETOOTH }) }
        }
    }

    private fun nameOf(device: BluetoothDevice): String =
        try {
            device.name ?: device.address
        } catch (e: SecurityException) {
            device.address
        }

    private fun targetOf(found: Found) = Target.Network(found.laptopId, found.name, found.hosts, found.port)

    /** A tap on a row: the prompt it is waiting on, the controls of a connected laptop, or else a connect. */
    private fun tapDevice(device: PickerScreen.Device) {
        val row = device.row
        val laptop = row.laptop
        val ask = laptop?.asking
        when {
            laptop == null -> {
                connectTo(row, row.via)
            }

            ask != null -> {
                manager.select(laptop.id)
                goTo(if (ask == Laptop.Ask.PIN) Screen.PIN else Screen.PAIR_CODE)
            }

            laptop.connected -> {
                manager.select(laptop.id)
                goTo(Screen.SURFACE)
            }

            else -> {
                connectTo(row, row.via)
            }
        }
    }

    /** The row's laptop over [via]. Connected already, its link there is replaced by this one. */
    private fun connectTo(
        row: LaptopRow,
        via: Transport?,
    ) {
        // A tap starts a fresh set of attempts and earns the quick retry: the first connect after a long idle often
        // fails once and then works.
        val target =
            when (via) {
                null -> {
                    null
                }

                Transport.BLUETOOTH -> {
                    bonded().firstOrNull { it.address == row.bluetooth }?.let { Target.Bluetooth(it) }
                }

                else -> {
                    row.network.firstOrNull { it.transport == via }?.let(::targetOf) ?: row.saved?.networkTarget()
                }
            }
        val chosen = target ?: row.laptop?.target
        if (chosen == null) {
            picker.setMessage(getString(R.string.not_paired))
        } else {
            connect(chosen, via, tapped = true)
        }
    }

    /** It stays listed and off the automatic connect, until the next time it is connected to. */
    private fun disconnectDevice(device: PickerScreen.Device) {
        val row = device.row
        saved = saved.map { if (it.isIn(row.keys)) it.copy(autoConnect = false) else it }
        row.laptop?.let { manager.disconnect(it.id) }
        refreshDevices()
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
                    connect(targetOf(found), found.transport, tapped = true)
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

    /**
     * Leaves every other laptop alone. A tap with no working laptop on screen takes the selection, so what was tapped
     * is what opens; otherwise the new laptop waits on its row. [via] is how a network laptop was found.
     */
    private fun connect(
        target: Target,
        via: Transport? = null,
        tapped: Boolean = false,
    ) {
        val select = tapped && !connected
        when (target) {
            is Target.Bluetooth -> {
                manager.connect(target, nameOf(target.device), Transport.BLUETOOTH, tapped, select)
            }

            is Target.Network -> {
                manager.connect(target, target.name, via ?: Transport.WIFI, tapped, select)
            }
        }
    }

    /** [laptop] works, so it is remembered, under the key it was reached by; TEXT DEVICE brings its real name here. */
    private fun remember(laptop: Laptop) {
        val target = laptop.target
        val network = target as? Target.Network
        val entry =
            SavedLaptop(
                id = target.key,
                kind = laptop.kind,
                name = laptop.state.laptopName ?: laptop.name,
                btAddress = (target as? Target.Bluetooth)?.device?.address.orEmpty(),
                hosts = network?.hosts?.mapNotNull { it.hostAddress }.orEmpty(),
                port = network?.port ?: 0,
            )
        saved = rememberLaptop(saved, entry, first = laptop === selected)
    }

    /** Devices follows every laptop's state, the selected one's or not. */
    private fun refreshDevices() {
        if (screen == Screen.PAIRING) refreshPicker()
    }

    /**
     * Every attempt, the manager's retries too, starts here. A retry aims at the device it had, so this is where a
     * laptop no longer bonded stops it: retrying cannot help, and a bare "Stopped retrying" would not say why.
     */
    override fun onConnecting(laptop: Laptop) {
        val target = laptop.target
        if (target is Target.Bluetooth && bonded().none { it.address == target.key }) {
            val shown = laptop === selected
            manager.stopRetrying(laptop.id)
            if (shown && screen == Screen.RECONNECTING) goTo(Screen.PAIRING)
            if (shown) picker.setMessage(getString(R.string.not_paired))
            return
        }
        if (laptop === selected) {
            CaptureInbox.owner = laptop.link?.id
            watchingMacros = false
            picker.setRtt(Double.NaN)
            picker.setMessage("")
            updateReconnecting()
        }
        refreshDevices()
    }

    override fun onConnected(laptop: Laptop) {
        remember(laptop)
        if (laptop !== selected) {
            refreshDevices()
            return
        }
        // The trust hint, if it was showing, has been answered.
        picker.setMessage("")
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

    override fun onAwaitingLaptop(laptop: Laptop) {
        refreshDevices()
        if (laptop === selected) picker.setMessage(getString(R.string.status_accept_on_laptop))
    }

    /**
     * From any screen: the laptop waits for this answer and gives up on its own. While the PIN screen is up
     * the attempt's link is set, so no retry can start a second connect behind it. A laptop that is not [selected]
     * interrupts nothing: [Laptop.asking] shows on its row, and tapping that opens this. If it gives up first, that
     * is a failed connect for that laptop alone.
     */
    override fun onPinRequest(laptop: Laptop) {
        if (laptop !== selected) {
            refreshDevices()
            return
        }
        // The trust hint, if it was showing, has been answered.
        picker.setMessage("")
        goTo(Screen.PIN)
    }

    /**
     * A network pairing waits on the laptop's owner typing [code] there; the countdown matches the link's wait. A
     * laptop that is not [selected] waits on its row, as in [onPinRequest].
     */
    override fun onPairingCode(
        laptop: Laptop,
        code: Sas,
    ) {
        if (laptop !== selected) {
            refreshDevices()
            return
        }
        picker.setMessage("")
        goTo(Screen.PAIR_CODE)
    }

    /** The laptop answered the pairing; only the owner's Yes on this screen keeps it ([LaptopLink.answerPairing]). */
    override fun onPairingConfirm(laptop: Laptop) {
        if (laptop === selected) goTo(Screen.PAIR_CODE) else refreshDevices()
    }

    override fun onRoundTrip(
        laptop: Laptop,
        ms: Double,
    ) {
        if (laptop === selected) picker.setRtt(laptop.rtt.median())
    }

    override fun onFrame(
        laptop: Laptop,
        frame: Frame,
    ) {
        if (frame is Frame.Text && frame.kind == TextKind.DEVICE.id) {
            remember(laptop)
            refreshDevices()
        }
        if (laptop !== selected) return
        surface?.stateChanged()
        // Capture is drawn from the features the laptop named, which it sends once after the handshake: one that
        // lands while it is open rebuilds it rather than leaving it saying "none". The Controls popup is redrawn.
        val sized = frame is Frame.Text && (frame.kind == TextKind.FEATURES.id || frame.kind == TextKind.SCREEN_SIZE.id)
        if (screen == Screen.CAPTURE && sized) goTo(screen)
        // The grid can be open before the laptop has named what it can do.
        if (screen == Screen.MACROS && frame is Frame.Text && frame.kind == TextKind.FEATURES.id) watchMacros()
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
        // A new list clears the open states, and a laptop only reports on change, so a watch already running is
        // asked again: every laptop forgets its last report on a WATCH and says what is open afresh.
        if (watchingMacros && frame is Frame.Text && frame.kind == TextKind.MACROS.id) {
            manager.send(ActionId.MACROS_WATCH.frame())
        }
        if (grid != null && frame is Frame.Text) {
            when (frame.kind) {
                TextKind.MACROS.id -> {
                    // The link has already asked for this list's pictures; they arrive as MACRO_ICON frames.
                    grid.setMacros(state.macros)
                }

                TextKind.MACRO_ICON.id -> {
                    grid.iconArrived(state.lastIcon)
                }

                TextKind.MACRO_OPEN.id -> {
                    grid.openChanged()
                }
            }
        }
    }

    /** The manager has already decided what comes next: [Laptop.reconnecting], or a quick retry on its timer. */
    override fun onClosed(
        laptop: Laptop,
        closure: LaptopLink.Closure,
    ) {
        if (laptop !== selected) {
            refreshDevices()
            // Unreachable is a laptop that is off, which is nothing to report at every launch; its row says so.
            if (laptop.failed && laptop.idle && closure.cause != LaptopLink.Cause.UNREACHABLE) {
                picker.setMessage(getString(R.string.status_closed_named, laptop.name, describe(closure)))
            }
            return
        }
        watchingMacros = false
        // Devices is where the reason is shown, so an attempt that ends at the PIN or a pairing code goes there first.
        if (screen == Screen.PIN || screen == Screen.PAIR_CODE) goTo(Screen.PAIRING)
        when {
            closure.cause == LaptopLink.Cause.ENDED -> {
                if (screen == Screen.SURFACE || screen == Screen.RECONNECTING ||
                    screen == Screen.CAPTURE || screen == Screen.ANNOTATE || screen == Screen.KEYBOARD ||
                    screen == Screen.GAMEPAD || screen == Screen.MACROS || screen in settingsOfControls
                ) {
                    goTo(Screen.PAIRING)
                }
                if (screen == Screen.SETTINGS) {
                    if (settingsReturn == Screen.SURFACE || settingsReturn in settingsOfControls) {
                        settingsReturn = Screen.PAIRING
                    }
                    goTo(Screen.SETTINGS)
                }
                refreshPicker()
            }

            // It was working and dropped on its own, or a retry just failed: say so while the manager tries.
            laptop.reconnecting -> {
                if (screen == Screen.RECONNECTING) updateReconnecting() else goTo(Screen.RECONNECTING)
            }

            // The first connect of a tap never reached the laptop: once more before saying anything.
            laptop.retryTimer != null -> {
                refreshPicker()
            }

            else -> {
                refreshPicker()
                picker.setMessage(getString(R.string.status_closed, describe(closure)))
            }
        }
    }

    /**
     * The last moment frames reach [laptop], so everything held on it is let go here: fingers first, then the pad, the
     * overlay and the macro watch, then the screen itself, whose views release what they hold as they detach. A
     * capture still running is left alone: a recording cannot be stopped from here, because Stop is an action and this
     * laptop has stopped being the one actions go to. Its permit stays with its link, and [CaptureInbox.owner] is
     * cleared first so the capture screen closing does not throw away a file on its way in.
     */
    override fun onDeselecting(laptop: Laptop) {
        CaptureInbox.owner = null
        releaseHeldInput()
        controls?.dismiss()
        if (screen == Screen.GAMEPAD) manager.send(ActionId.PAD_DETACH.frame())
        endAnnotate()
        unwatchMacros()
        if (screen in laptopScreens) findViewById<ViewGroup>(android.R.id.content).removeAllViews()
    }

    /** Every screen draws [selected], so the one showing is built again on the new laptop, or left if it cannot be. */
    override fun onSelected(laptop: Laptop?) {
        CaptureInbox.owner = laptop?.link?.id
        picker.setRtt(laptop?.rtt?.median() ?: Double.NaN)
        laptop?.let { saved = promoted(saved, setOf(it.id)) }
        val needsLink = screen in laptopScreens || screen in settingsOfControls || screen == Screen.RECONNECTING
        when {
            screen == Screen.PIN || screen == Screen.PAIR_CODE || (needsLink && !connected) -> {
                goTo(Screen.PAIRING)
            }

            screen in laptopScreens -> {
                goTo(screen)
                if (screen == Screen.GAMEPAD) manager.send(ActionId.PAD_ATTACH.frame())
            }

            screen == Screen.RECONNECTING -> {
                goTo(Screen.SURFACE)
            }

            else -> {
                refreshDevices()
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

    private fun retryNow() {
        selected?.let { manager.retryNow(it.id) }
        updateReconnecting()
    }

    private fun stopRetrying() {
        selected?.let { manager.stopRetrying(it.id) }
    }

    private fun updateReconnecting() {
        val lost = reconnecting ?: return
        val laptop = selected ?: return
        val most = LaptopConnections.MAX_ATTEMPTS
        val status =
            when {
                !settings.reconnect && laptop.link == null -> ReconnectingScreen.State.OFF
                laptop.attempts >= most -> ReconnectingScreen.State.STOPPED
                else -> ReconnectingScreen.State.TRYING
            }
        val seconds = (SystemClock.elapsedRealtime() - laptop.lostAt) / MILLIS_PER_SECOND
        lost.update((laptop.attempts + 1).coerceAtMost(most), most, status, seconds)
    }

    /** Off the remembered list and the manager's. If it was selected, the next connected laptop is, or none. */
    private fun forgetLaptop(
        laptop: Laptop?,
        keys: Set<String>,
    ) {
        saved = forgotten(saved, keys)
        laptop?.let { manager.forget(it.id) }
        if (screen == Screen.SETTINGS) goTo(Screen.SETTINGS) else refreshDevices()
    }

    /** Settings' card: the laptop it would forget. */
    private fun cardLaptop(): SavedLaptop? {
        val laptop = selected
        return if (laptop == null) saved.firstOrNull() else saved.firstOrNull { laptop.id in it.keys }
    }

    /** Settings' Forget. */
    private fun forget() {
        val laptop = selected
        val keys = laptop?.let { setOf(it.id) } ?: cardLaptop()?.keys ?: return
        forgetLaptop(laptop, keys)
    }

    /** The active laptop, or the first remembered one with none active, and how many more are connected. */
    private fun connectionInfo(): SettingsScreen.Connection {
        val laptop = selected
        val entry = cardLaptop()
        if (laptop == null && entry == null) return SettingsScreen.Connection(null, "", connected = false)
        val name =
            laptop?.name?.takeIf { it.isNotEmpty() }
                ?: bonded().firstOrNull { it.address == entry?.btAddress }?.let(::nameOf)
                ?: entry?.name
        val median = laptop?.rtt?.median() ?: Double.NaN
        val detail =
            when {
                connected && !median.isNaN() -> getString(R.string.detail_connected_rtt, median)
                connected -> getString(R.string.detail_connected)
                else -> getString(R.string.detail_remembered)
            }
        val others = manager.laptops.count { it !== laptop && it.connected }
        val more = resources.getQuantityString(R.plurals.detail_more_connected, others, others)
        return SettingsScreen.Connection(name, if (others > 0) "$detail · $more" else detail, connected)
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

        /** The screens built on one laptop's state and sending to it: a change of selection rebuilds them. */
        val laptopScreens =
            setOf(
                Screen.SURFACE,
                Screen.KEYBOARD,
                Screen.GAMEPAD,
                Screen.MACROS,
                Screen.CAPTURE,
                Screen.ANNOTATE,
            )

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

        /** How long a link outlives the app leaving the screen: a trip to the picker, a glance at the lock screen. */
        const val LINGER_MS = 30_000L
        const val TICK_MS = 1_000L

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

        /**
         * Builds each connect's transport. The manager keeps this past a rebuild, so [app] is the application context
         * and nothing here may hold an activity. The link id and pairing keys open the first time a network laptop
         * is tried.
         */
        private fun transports(app: Context): (Target) -> LinkTransport {
            val keys by lazy { LinkKeys.open(app) }
            return { target ->
                when (target) {
                    is Target.Bluetooth -> {
                        BluetoothTransport(target.device)
                    }

                    is Target.Network -> {
                        TcpTransport(target.hosts, target.port, target.laptopId, keys, phoneName(app), target.pairAgain)
                    }
                }
            }
        }

        /** What the laptop's pairing prompt calls this phone: the name set in the phone's settings, else its model. */
        private fun phoneName(context: Context): String =
            Global.getString(context.contentResolver, Global.DEVICE_NAME) ?: Build.MODEL
    }
}
