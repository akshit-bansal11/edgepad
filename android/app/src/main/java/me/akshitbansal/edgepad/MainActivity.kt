package me.akshitbansal.edgepad

import android.Manifest
import android.app.Activity
import android.bluetooth.BluetoothClass
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.content.ActivityNotFoundException
import android.content.Intent
import android.content.pm.ActivityInfo
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.widget.TextView
import android.widget.Toast
import android.window.OnBackInvokedCallback
import android.window.OnBackInvokedDispatcher
import me.akshitbansal.edgepad.gamepad.GamepadStore
import me.akshitbansal.edgepad.link.LaptopLink
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.RttStats
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.ProtocolConstants
import me.akshitbansal.edgepad.protocol.TextKind
import me.akshitbansal.edgepad.screens.AppearanceScreen
import me.akshitbansal.edgepad.screens.CornersScreen
import me.akshitbansal.edgepad.screens.DialFeelScreen
import me.akshitbansal.edgepad.screens.GamepadLayoutScreen
import me.akshitbansal.edgepad.screens.GamepadScreen
import me.akshitbansal.edgepad.screens.GestureScreen
import me.akshitbansal.edgepad.screens.GuideScreen
import me.akshitbansal.edgepad.screens.KeyboardScreen
import me.akshitbansal.edgepad.screens.MacroScreen
import me.akshitbansal.edgepad.screens.MediaLayoutScreen
import me.akshitbansal.edgepad.screens.PickerScreen
import me.akshitbansal.edgepad.screens.ReconnectingScreen
import me.akshitbansal.edgepad.screens.SettingsScreen
import me.akshitbansal.edgepad.screens.ShapeDrawScreen
import me.akshitbansal.edgepad.screens.ShapesScreen
import me.akshitbansal.edgepad.screens.Ui
import me.akshitbansal.edgepad.surface.BackgroundImage
import me.akshitbansal.edgepad.surface.ControlSurface
import me.akshitbansal.edgepad.update.Updates
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import kotlin.concurrent.thread

/**
 * One activity for every screen: the guide on the first run and from Settings, the Devices list, the
 * control surface, the settings pages, the keyboard, the gamepad, the macros, the two layout editors, and the
 * connection-lost screen that retries after a link drops on its own. [Screen] is the whole set and
 * [goTo] the only way between them — there are no fragments and no back stack, so [navigateBack] is
 * where every screen's way out is written down.
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
        CORNERS,
        GESTURES,
        SHAPES,
        SHAPE_DRAW,
        DIAL_FEEL,
        APPEARANCE,
        MEDIA_LAYOUT,
        GAMEPAD_LAYOUT,
        KEYBOARD,
        GAMEPAD,
        MACROS,
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
        val rtt: RttStats,
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
    private var reconnecting: ReconnectingScreen? = null

    /** The macro grid while it is the screen, so a new list or a finished icon can be handed to it. */
    private var macros: MacroScreen? = null
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

    /** Paired devices of every kind in the list, not only the ones that say they are computers. */
    private var showAllDevices = false

    /** Nearby devices has been asked for once by this activity; after that only a tap asks again. */
    private var askedPermission = false

    /** A check with GitHub is in flight, or the last one got no answer. Neither outlives the activity. */
    private var checkingUpdate = false
    private var updateCheckFailed = false

    /** The Updates row's summary while Settings is showing, rewritten in place as a check finishes. */
    private var updateSummaryView: TextView? = null

    /** The system's permission prompt is up, so the Devices list offers no way round it underneath. */
    private var permissionPending = false

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
                onRefresh = ::refreshPicker,
                onShowAll = { all ->
                    showAllDevices = all
                    refreshPicker()
                },
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
            // Carried over, or the readout would stand blank after a rotation and then restart from one sample.
            rtt = retained.rtt
            picker.setRtt(rtt.median())
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
        Retained(link, state, screen, settingsReturn, layoutReturn, laptopName, laptopAddress, attempts, lostAt, rtt)

    override fun onStart() {
        super.onStart()
        handler.removeCallbacks(linger)
        started = true
        val current = link
        when {
            // Restarted rather than left running: the tick waiting may be a slow one from the background.
            current != null -> if (current.connected) startPinger()

            screen == Screen.RECONNECTING -> scheduleRetry(0L)

            screen != Screen.ONBOARDING && settings.reconnect -> rememberedDevice()?.let(::connect)
        }
        if (screen == Screen.RECONNECTING) handler.post(ticker)
        refreshPicker()
        val sinceCheck = System.currentTimeMillis() - settings.lastUpdateCheck
        if (settings.autoUpdateCheck && sinceCheck >= Updates.CHECK_EVERY_MS) checkForUpdate()
    }

    override fun onStop() {
        super.onStop()
        started = false
        releaseHeldInput()
        handler.removeCallbacks(retry)
        handler.removeCallbacks(ticker)
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
        if (requestCode != REQUEST_BLUETOOTH) return
        permissionPending = false
        // Refused as well as granted: a refusal changes what the list offers, from nothing to a way back.
        refreshPicker()
    }

    private fun goTo(next: Screen) {
        // The virtual controller is plugged in for exactly as long as the gamepad is on screen. Asked for
        // here rather than inside the screen because leaving it is also a way of arriving somewhere else,
        // and because a preset change rebuilds the same screen: that is not a trip out and back.
        if (screen == Screen.GAMEPAD && next != Screen.GAMEPAD) link?.send(ActionId.PAD_DETACH.frame())
        if (next == Screen.GAMEPAD && screen != Screen.GAMEPAD) link?.send(ActionId.PAD_ATTACH.frame())
        // Onto a screen that shows the round trip, the pings speed up now rather than at the next slow tick.
        if (next in showsRtt && screen !in showsRtt && link?.connected == true) startPinger()
        screen = next
        surface = null
        gamepad = null
        reconnecting = null
        macros = null
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
                    val backTo = settingsBack()
                    val backLabel = if (backTo == Screen.SURFACE) R.string.settings_surface else R.string.pairing_title
                    SettingsScreen.build(
                        ui,
                        settings,
                        connectionInfo(),
                        gamepads.current.name,
                        versionLine(),
                        getString(backLabel),
                        SettingsScreen.Update(updateSummary()) { view ->
                            // The summary changes with nobody touching it, so a screen reader is told when it does.
                            view.accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
                            updateSummaryView = view
                        },
                        SettingsScreen.Routes(
                            forget = ::forget,
                            corners = { goTo(Screen.CORNERS) },
                            gestures = { goTo(Screen.GESTURES) },
                            shapes = { goTo(Screen.SHAPES) },
                            dialFeel = { goTo(Screen.DIAL_FEEL) },
                            gamepadLayout = {
                                layoutReturn = Screen.SETTINGS
                                goTo(Screen.GAMEPAD_LAYOUT)
                            },
                            mediaLayout = { goTo(Screen.MEDIA_LAYOUT) },
                            appearance = { goTo(Screen.APPEARANCE) },
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

                Screen.CORNERS -> {
                    CornersScreen.build(ui, settings) { navigateBack() }
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

                Screen.KEYBOARD -> {
                    KeyboardScreen.build(ui, settings.keyTextScale, onKey = ::key) { navigateBack() }
                }

                Screen.GAMEPAD -> {
                    GamepadScreen(
                        ui,
                        gamepads.current,
                        gamepads.all.map { it.name },
                        state.padStatus,
                        onKey = ::key,
                        onPad = { pad -> link?.send(pad) },
                        onBack = { navigateBack() },
                        onChoose = { name ->
                            gamepads.choose(name)
                            goTo(Screen.GAMEPAD)
                        },
                        onEdit = {
                            layoutReturn = Screen.GAMEPAD
                            goTo(Screen.GAMEPAD_LAYOUT)
                        },
                    ).also { gamepad = it }.view
                }

                Screen.MACROS -> {
                    MacroScreen(
                        ui,
                        state.macros,
                        icon = state::macroIcon,
                        labels = settings.macroLabels,
                        onRun = ::runMacro,
                    ) { navigateBack() }.also { macros = it }.view
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
                        onOpenMacros = ::openMacros,
                    ).also { surface = it }
                }
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
     * Three answers, not two. The keyboard and the gamepad are drawn sideways and only sideways. The control
     * surface and the two canvases that stand in for it are held the way the Orientation setting says — the
     * media layout editor because a layout is stored per orientation since 2.3.0, so a phone turned mid-edit
     * would quietly start changing the other one, and the shape canvas because a stroke is drawn at the pad's
     * own proportions. Everything else follows the phone, which is what the setting used to override for the
     * whole app. A change rebuilds the activity; the link survives it.
     */
    private fun applyOrientation() {
        requestedOrientation =
            when {
                screen in alwaysSideways -> ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
                screen !in pinned -> ActivityInfo.SCREEN_ORIENTATION_USER
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

            Screen.CORNERS, Screen.GESTURES, Screen.DIAL_FEEL, Screen.APPEARANCE, Screen.GUIDE, Screen.MEDIA_LAYOUT,
            Screen.SHAPES,
            -> {
                goTo(Screen.SETTINGS)
            }

            Screen.SHAPE_DRAW -> {
                goTo(Screen.SHAPES)
            }

            Screen.GAMEPAD_LAYOUT -> {
                goTo(layoutReturn)
            }

            Screen.KEYBOARD, Screen.GAMEPAD, Screen.MACROS -> {
                goTo(if (link?.connected == true) Screen.SURFACE else Screen.PAIRING)
            }

            else -> {
                return false
            }
        }
        return true
    }

    /**
     * Where back from Settings leads, which its back link is also named for: the surface it was opened from
     * while the link still holds, and the Devices list otherwise.
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

    /**
     * Opens the macro grid, asking the laptop for the pictures on the way in. Asked for rather than pushed
     * because an icon is thousands of times the size of an input frame: requesting them here spends the link
     * on a screen that has no trackpad on it, and a laptop too old to know the request drops it and counts
     * it, leaving a grid of names.
     */
    private fun openMacros() {
        link?.send(TextKind.WANT_ICONS.frame(""))
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
            checkingUpdate -> getString(R.string.updates_checking)
            updateCheckFailed -> getString(R.string.updates_failed)
            updateAvailable() -> getString(R.string.updates_available, settings.latestVersion)
            settings.lastUpdateCheck == 0L -> ""
            else -> getString(R.string.updates_current)
        }

    /** The Updates row: the newest release's page when there is one to get, and otherwise a fresh check. */
    private fun openUpdate() {
        if (updateAvailable()) open(Updates.LATEST_URL) else checkForUpdate()
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
        val devices = shown.map { PickerScreen.Device(it.address, nameOf(it)) }.sortedBy { it.name }
        picker.showDevices(
            devices,
            settings.laptop,
            if (link?.connected == true) laptopAddress else null,
            settings.reconnect,
            hidden = paired.size - laptops.size,
            showingAll = showAllDevices,
        )
        when {
            adapter == null || !adapter.isEnabled -> {
                picker.setMessage(getString(R.string.bluetooth_off), getString(R.string.open_bluetooth_settings)) {
                    openBluetoothSettings()
                }
            }

            !permitted && permissionPending -> {
                picker.setMessage(getString(R.string.permission_needed))
            }

            !permitted && shouldShowRequestPermissionRationale(Manifest.permission.BLUETOOTH_CONNECT) -> {
                picker.setMessage(getString(R.string.permission_needed), getString(R.string.allow_permission)) {
                    askPermission()
                }
            }

            !permitted -> {
                picker.setMessage(getString(R.string.permission_needed), getString(R.string.open_app_settings)) {
                    openAppSettings()
                }
            }

            paired.isEmpty() -> {
                picker.setMessage(getString(R.string.no_paired), getString(R.string.open_bluetooth_settings)) {
                    openBluetoothSettings()
                }
            }

            // Paired devices, none of them claiming to be a computer: the list's own button shows them all.
            devices.isEmpty() -> {
                picker.setMessage(getString(R.string.no_laptop_paired))
            }

            else -> {
                picker.setMessage("")
            }
        }
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
        handler.removeCallbacks(pinger)
        val old = link
        link = null
        old?.close(LaptopLink.Closure(LaptopLink.Cause.ENDED))
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

    override fun onConnected(link: LaptopLink) {
        if (link != this.link) return
        attempts = 0
        picker.setConnecting(null)
        // The trust hint, if it was showing, has been answered.
        picker.setMessage("")
        startPinger()
        when (screen) {
            Screen.PAIRING, Screen.RECONNECTING -> goTo(Screen.SURFACE)
            Screen.SETTINGS -> settingsReturn = Screen.SURFACE
            else -> Unit
        }
    }

    override fun onAwaitingLaptop(link: LaptopLink) {
        if (link == this.link) picker.setMessage(getString(R.string.status_accept_on_laptop))
    }

    override fun onRoundTrip(ms: Double) {
        rtt.add(ms)
        picker.setRtt(rtt.median())
    }

    override fun onFrame(frame: Frame) {
        if (!state.take(frame)) return
        surface?.stateChanged()
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
                    grid.setMacros(state.macros)
                    // A new list retires the icons with it, since a slot number now means a different macro.
                    // Nothing else asks for them again, so this is where a grid left open gets its pictures back.
                    link?.send(TextKind.WANT_ICONS.frame(""))
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
        when {
            closure.cause == LaptopLink.Cause.ENDED -> {
                if (screen == Screen.SURFACE || screen == Screen.RECONNECTING || screen == Screen.KEYBOARD ||
                    screen == Screen.GAMEPAD || screen == Screen.MACROS
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
                picker.setMessage(getString(R.string.status_closed, describe(closure)))
            }
        }
    }

    /** Why an attempt ended, in the user's language. The link names the cause; the words are the app's. */
    private fun describe(closure: LaptopLink.Closure): String =
        when (closure.cause) {
            LaptopLink.Cause.ENDED, LaptopLink.Cause.LOST -> closure.detail.ifEmpty { getString(R.string.link_lost) }
            LaptopLink.Cause.REFUSED -> getString(R.string.link_refused)
            LaptopLink.Cause.MISMATCH -> getString(R.string.link_mismatch, closure.detail, ProtocolConstants.VERSION)
            LaptopLink.Cause.UNEXPECTED -> getString(R.string.link_unexpected, closure.detail)
            LaptopLink.Cause.NO_PERMISSION -> getString(R.string.link_no_permission)
            LaptopLink.Cause.NO_ANSWER -> getString(R.string.link_no_answer)
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
                Screen.MEDIA_LAYOUT,
                Screen.GAMEPAD_LAYOUT,
                Screen.SHAPE_DRAW,
            )

        /** Drawn sideways whatever the phone is doing, because they are laid out for a wide screen and nothing else. */
        val alwaysSideways = setOf(Screen.KEYBOARD, Screen.GAMEPAD, Screen.GAMEPAD_LAYOUT)

        /**
         * Held the way the Orientation setting says. Everything outside both sets follows the phone.
         *
         * The shape canvas is here for a reason of its own: it is held the way the pad is because a shape
         * is drawn at the pad's own proportions, and because a phone that rotated mid-stroke would turn
         * half a drawing into a saved shape nobody could reproduce.
         */
        val pinned = setOf(Screen.SURFACE, Screen.MEDIA_LAYOUT, Screen.SHAPE_DRAW)
        const val REQUEST_BLUETOOTH = 1
        const val REQUEST_IMAGE = 2

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
        const val MAX_ATTEMPTS = 10
        const val MILLIS_PER_SECOND = 1_000L

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
