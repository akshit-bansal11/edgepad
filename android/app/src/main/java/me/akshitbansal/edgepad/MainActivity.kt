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
import android.provider.DocumentsContract
import android.provider.Settings.Global
import android.text.InputType
import android.util.Log
import android.view.Gravity
import android.view.HapticFeedbackConstants
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.ViewTreeObserver
import android.view.WindowInsetsController
import android.view.WindowManager
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import android.window.OnBackInvokedCallback
import android.window.OnBackInvokedDispatcher
import me.akshitbansal.edgepad.gamepad.GamepadLayout
import me.akshitbansal.edgepad.gamepad.GamepadStore
import me.akshitbansal.edgepad.link.AddressProblem
import me.akshitbansal.edgepad.link.AppLinks
import me.akshitbansal.edgepad.link.BlobRoutes
import me.akshitbansal.edgepad.link.BluetoothTapHint
import me.akshitbansal.edgepad.link.BluetoothTransport
import me.akshitbansal.edgepad.link.CaptureInbox
import me.akshitbansal.edgepad.link.ClipboardShare
import me.akshitbansal.edgepad.link.DashMask
import me.akshitbansal.edgepad.link.Discovery
import me.akshitbansal.edgepad.link.ForegroundWatchPolicy
import me.akshitbansal.edgepad.link.Found
import me.akshitbansal.edgepad.link.GitHost
import me.akshitbansal.edgepad.link.HostPort
import me.akshitbansal.edgepad.link.HubPages
import me.akshitbansal.edgepad.link.KeptEvents
import me.akshitbansal.edgepad.link.Laptop
import me.akshitbansal.edgepad.link.LaptopConnections
import me.akshitbansal.edgepad.link.LaptopLink
import me.akshitbansal.edgepad.link.LaptopRow
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.LingerAction
import me.akshitbansal.edgepad.link.LinkHooks
import me.akshitbansal.edgepad.link.LinkNotice
import me.akshitbansal.edgepad.link.LinkService
import me.akshitbansal.edgepad.link.LinkSession
import me.akshitbansal.edgepad.link.LinkTransport
import me.akshitbansal.edgepad.link.LocalNetwork
import me.akshitbansal.edgepad.link.MainScheduler
import me.akshitbansal.edgepad.link.NET_KEY
import me.akshitbansal.edgepad.link.OlderHalf
import me.akshitbansal.edgepad.link.PhoneBattery
import me.akshitbansal.edgepad.link.PickerHint
import me.akshitbansal.edgepad.link.SavedLaptop
import me.akshitbansal.edgepad.link.ShareTarget
import me.akshitbansal.edgepad.link.Sweep
import me.akshitbansal.edgepad.link.Target
import me.akshitbansal.edgepad.link.TcpTransport
import me.akshitbansal.edgepad.link.TetherProbe
import me.akshitbansal.edgepad.link.TileStatus
import me.akshitbansal.edgepad.link.Transport
import me.akshitbansal.edgepad.link.addressProblem
import me.akshitbansal.edgepad.link.bluetoothTapHint
import me.akshitbansal.edgepad.link.clipboardShare
import me.akshitbansal.edgepad.link.decodeSaved
import me.akshitbansal.edgepad.link.encodeSaved
import me.akshitbansal.edgepad.link.forgotten
import me.akshitbansal.edgepad.link.laptopLinks
import me.akshitbansal.edgepad.link.laptopRows
import me.akshitbansal.edgepad.link.lingerAction
import me.akshitbansal.edgepad.link.linkNotice
import me.akshitbansal.edgepad.link.mergeFound
import me.akshitbansal.edgepad.link.mergedRows
import me.akshitbansal.edgepad.link.net.LinkKeys
import me.akshitbansal.edgepad.link.net.Sas
import me.akshitbansal.edgepad.link.notify.PermNotifier
import me.akshitbansal.edgepad.link.notify.PhoneNotifier
import me.akshitbansal.edgepad.link.olderHalf
import me.akshitbansal.edgepad.link.onlyAutoConnecting
import me.akshitbansal.edgepad.link.parseHex
import me.akshitbansal.edgepad.link.parseHostPort
import me.akshitbansal.edgepad.link.pickerHint
import me.akshitbansal.edgepad.link.probeAddress
import me.akshitbansal.edgepad.link.promoted
import me.akshitbansal.edgepad.link.rememberLaptop
import me.akshitbansal.edgepad.link.snapUp
import me.akshitbansal.edgepad.link.xfer.Transfers
import me.akshitbansal.edgepad.link.xfer.XferPhase
import me.akshitbansal.edgepad.link.xfer.XferSnapshot
import me.akshitbansal.edgepad.overlay.FloatingDials
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.AppAddStatus
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.CaptureTarget
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.DeviceOs
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.ProtocolConstants
import me.akshitbansal.edgepad.protocol.SignStatus
import me.akshitbansal.edgepad.protocol.TextKind
import me.akshitbansal.edgepad.screens.AiScreen
import me.akshitbansal.edgepad.screens.AnnotateScreen
import me.akshitbansal.edgepad.screens.AppListScreen
import me.akshitbansal.edgepad.screens.AppearanceScreen
import me.akshitbansal.edgepad.screens.BatterySheet
import me.akshitbansal.edgepad.screens.CaptureScreen
import me.akshitbansal.edgepad.screens.CaptureSettingsScreen
import me.akshitbansal.edgepad.screens.Carry
import me.akshitbansal.edgepad.screens.ClipboardSheet
import me.akshitbansal.edgepad.screens.ControlsPopup
import me.akshitbansal.edgepad.screens.ControlsSettingsScreen
import me.akshitbansal.edgepad.screens.CornersScreen
import me.akshitbansal.edgepad.screens.DataUsesScreen
import me.akshitbansal.edgepad.screens.DeveloperHub
import me.akshitbansal.edgepad.screens.DeviceRef
import me.akshitbansal.edgepad.screens.DeviceSource
import me.akshitbansal.edgepad.screens.DialFeelScreen
import me.akshitbansal.edgepad.screens.GamepadLayoutScreen
import me.akshitbansal.edgepad.screens.GamepadScreen
import me.akshitbansal.edgepad.screens.GamepadSettingsScreen
import me.akshitbansal.edgepad.screens.GestureScreen
import me.akshitbansal.edgepad.screens.GitScreen
import me.akshitbansal.edgepad.screens.GuideScreen
import me.akshitbansal.edgepad.screens.KeyboardScreen
import me.akshitbansal.edgepad.screens.KeyboardSettingsScreen
import me.akshitbansal.edgepad.screens.LiveBatteries
import me.akshitbansal.edgepad.screens.MacroScreen
import me.akshitbansal.edgepad.screens.MacroSettingsScreen
import me.akshitbansal.edgepad.screens.MediaLayoutScreen
import me.akshitbansal.edgepad.screens.NetworkScreen
import me.akshitbansal.edgepad.screens.OpeningScreen
import me.akshitbansal.edgepad.screens.PairCodeScreen
import me.akshitbansal.edgepad.screens.PermissionSheet
import me.akshitbansal.edgepad.screens.PickerScreen
import me.akshitbansal.edgepad.screens.PinScreen
import me.akshitbansal.edgepad.screens.PowerBanner
import me.akshitbansal.edgepad.screens.PowerScreen
import me.akshitbansal.edgepad.screens.PresenterScreen
import me.akshitbansal.edgepad.screens.PresenterSettingsScreen
import me.akshitbansal.edgepad.screens.ProfilesScreen
import me.akshitbansal.edgepad.screens.ReconnectingScreen
import me.akshitbansal.edgepad.screens.SettingsScreen
import me.akshitbansal.edgepad.screens.ShapeDrawScreen
import me.akshitbansal.edgepad.screens.ShapesScreen
import me.akshitbansal.edgepad.screens.SharingScreen
import me.akshitbansal.edgepad.screens.SheetRegistry
import me.akshitbansal.edgepad.screens.SurfaceSettingsScreen
import me.akshitbansal.edgepad.screens.SystemHub
import me.akshitbansal.edgepad.screens.SystemScreen
import me.akshitbansal.edgepad.screens.Ui
import me.akshitbansal.edgepad.screens.WidgetsScreen
import me.akshitbansal.edgepad.screens.XferCard
import me.akshitbansal.edgepad.screens.fillCutout
import me.akshitbansal.edgepad.screens.hideBars
import me.akshitbansal.edgepad.screens.onEdited
import me.akshitbansal.edgepad.screens.openOver
import me.akshitbansal.edgepad.screens.showOver
import me.akshitbansal.edgepad.surface.AutoProfiles
import me.akshitbansal.edgepad.surface.BackgroundImage
import me.akshitbansal.edgepad.surface.ConnectedLaptops
import me.akshitbansal.edgepad.surface.ControlSurface
import me.akshitbansal.edgepad.surface.Perimeter
import me.akshitbansal.edgepad.surface.Profile
import me.akshitbansal.edgepad.surface.Profiles
import me.akshitbansal.edgepad.surface.SurfaceWidget
import me.akshitbansal.edgepad.update.Installer
import me.akshitbansal.edgepad.update.UpdateChoice
import me.akshitbansal.edgepad.update.UpdateJob
import me.akshitbansal.edgepad.update.UpdateNotice
import me.akshitbansal.edgepad.update.Updates
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import kotlin.concurrent.thread
import android.provider.Settings as SystemSettings

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
 * - Stopped, and not for a rebuild: whatever a finger holds is let go at once ([releaseHeldInput]). The links
 *   stay up, pinged slowly, in one of two ways. With Stay connected on and a laptop connected, [LinkService] is
 *   running and holds the process, so they stay up for as long as it runs and nothing is scheduled; it is started
 *   only in the foreground ([syncService]), since Android refuses to start one from the background, and it
 *   stops with the last link. Otherwise they stay for [LINGER_MS]. The image picker, Bluetooth settings, the
 *   documentation in a browser and the screen going off are all a stop, and until 3.2 every one of them dropped
 *   the link.
 * - Started again inside [LINGER_MS], or with the service running: nothing happened; the links carry on.
 * - [LINGER_MS] passes: every link closes as [LaptopLink.Cause.ENDED], which is what onStop did at once
 *   before 3.2. A service that stops while the app is away leaves whatever links remain to the same wait.
 * - Destroyed, and not for a rebuild (backed out of, finished, or swiped from the recent apps): closed at
 *   once, and the service stopped.
 * - Rebuilt: [manager] is handed over in [Retained] and its events swapped in onCreate. It reads them when
 *   each event is delivered on the main thread, so nothing lands on the old activity.
 */
class MainActivity :
    Activity(),
    LaptopConnections.Events {
    private enum class Screen {
        OPENING,
        ONBOARDING,
        GUIDE,
        USES,
        PAIRING,
        SHARING,
        SETTINGS,
        SURFACE_SETTINGS,
        CORNERS,
        GESTURES,
        SHAPES,
        SHAPE_DRAW,
        DIAL_FEEL,
        APPEARANCE,
        PROFILES,
        MEDIA_LAYOUT,
        WIDGETS,
        WIDGET_LAYOUT,
        GAMEPAD_LAYOUT,
        KEYBOARD_SETTINGS,
        GAMEPAD_SETTINGS,
        MACRO_SETTINGS,
        CONTROLS_SETTINGS,
        PRESENTER_SETTINGS,
        CAPTURE_SETTINGS,
        CAPTURE,
        ANNOTATE,
        PRESENTER,
        SYSTEM,
        DEVELOPER,
        POWER,
        KEYBOARD,
        GAMEPAD,
        MACROS,
        APPS,
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
        val clipboard: ClipboardShare,
        val apps: AppLinks,
        val screen: Screen,
        val settingsReturn: Screen,
        val resumeScreen: Screen?,
        val childFrom: Screen?,
    )

    private val handler = Handler(Looper.getMainLooper())
    private lateinit var settings: Settings
    private lateinit var ui: Ui
    private lateinit var picker: PickerScreen
    private lateinit var manager: LaptopConnections

    /** The shared clipboard, kept with [manager] since the links it is wired to outlive the activity. */
    private lateinit var clipboard: ClipboardShare

    /** Every laptop's app list, kept with [manager] for the same reason: it is wired to the links' blob routes. */
    private lateinit var apps: AppLinks

    /** What the screens read while no laptop is selected. */
    private val noLaptop = LaptopState()
    private val selected: Laptop? get() = manager.selected
    private val state: LaptopState get() = selected?.state ?: noLaptop
    private val connected: Boolean get() = selected?.connected == true
    private var surface: ControlSurface? = null

    /** The dials over other apps; built on first use, and shown only while the app is away. */
    private var floating: FloatingDials? = null

    /**
     * What the Devices dial moves through: a step selects another connected laptop, which runs [onDeselecting] and
     * rebuilds the surface like any change of selection, so a slide in flight is cancelled by that and not here.
     */
    private val deviceSwitch by lazy { ConnectedLaptops(manager) { goTo(Screen.PAIRING) } }

    /** What the Stats and Developer hubs' device dropdown lists and selects: the same switch path as the dial. */
    private val hubDevices by lazy {
        DeviceSource(
            laptops = {
                manager.laptops
                    .filter { it.connected }
                    .map { DeviceRef(it.id, it.state.laptopName?.takeIf { name -> name.isNotEmpty() } ?: it.name) }
            },
            selectedId = { manager.selected?.id },
            select = manager::select,
        )
    }

    /** The gamepad while it is the screen, so the laptop's answer about its controller can reach it. */
    private var gamepad: GamepadScreen? = null

    /** The layout the gamepad on screen is playing, which differs from the chosen one in keyboard mode. */
    private var playedLayout = ""

    /** The control screen that was up when the link dropped, to return to on reconnect; null otherwise. */
    private var resumeScreen: Screen? = null

    /** The annotate screen while it is the screen, so a changed laptop display can redraw its pad. */
    private var annotate: AnnotateScreen? = null

    /** True from ANNOTATE_BEGIN until the one ANNOTATE_END that answers it, however the screen is left. */
    private var annotating = false

    /** The Presenter screen while it is the screen: the volume keys step its slides, and only while it is. */
    private var presenter: PresenterScreen? = null

    /** The System and Developer pages while they are the screen, so a new sample or list can be handed to them. */
    private var systemHub: SystemHub? = null
    private var systemScreen: SystemScreen? = null
    private var networkScreen: NetworkScreen? = null
    private var aiScreen: AiScreen? = null
    private var gitScreen: GitScreen? = null

    /** Redraws the Power page; set while that page shows. */
    private var powerRedraw: (() -> Unit)? = null

    /** The surface's Hide dials switch, kept here because the surface is rebuilt and the switch is on Dials. */
    private var dialsHidden = false

    /** The page of the System or Developer hub that shows. The laptop watches only that page's data. */
    private var statsPage = 0

    /** Whether Stats looks at this phone, which needs nothing from the laptop. */
    private var statsPhone = false

    /** What DASH_WATCH last told the selected laptop (0: nothing). The laptop forgets it at session end. */
    private var dashMask = 0

    /** The AI approval sheet, over any screen, and the notice that stands in for it while the app is away. */
    private lateinit var permSheet: PermissionSheet
    private lateinit var permNotifier: PermNotifier
    private var reconnecting: ReconnectingScreen? = null

    /** The macro grid while it is the screen, so a new list or a finished icon can be handed to it. */
    private var macros: MacroScreen? = null

    /** True from MACROS_WATCH until the one MACROS_UNWATCH that answers it, however the grid is left. */
    private var watchingMacros = false

    /** True from FOREGROUND_WATCH 1 until the 0 that answers it; the laptop forgets it when the session ends. */
    private var watchingForeground = false

    /** The app in front when the Profiles screen was last built, so a different one rebuilds it. */
    private var profilesBuiltFor: String? = null

    /** The laptop's app list while it is the screen, so a new list or a finished icon can be handed to it. */
    private var appList: AppListScreen? = null
    private var lastSurfaceBack = 0L

    /** True from APPS_LIST until the one APPS_CANCEL that answers it, however the list is left. */
    private var listingApps = false

    /** The Controls popup over the surface, while it is open. */
    private var controls: ControlsPopup? = null
    private val phoneBattery by lazy { PhoneBattery(this) }
    private val liveBatteries by lazy { LiveBatteries(manager, phoneBattery, getString(R.string.battery_this_phone)) }
    private var batterySheet: BatterySheet? = null
    private var clipboardSheet: ClipboardSheet? = null

    /** Everything open that is bound to the selected laptop; a switch of laptop closes all of it. */
    private val sheets = SheetRegistry()
    private var phoneWatch: AutoCloseable? = null
    private var screen = Screen.PAIRING
    private var settingsReturn = Screen.PAIRING

    /** Set to Settings while a page reached from its rows is open, so back returns there; null otherwise. */
    private var childFrom: Screen? = null

    /** The floating transfer card; rebuilt from [Transfers.progress] on every start, never retained. */
    private var xferCard: XferCard? = null

    // This activity's own listener: added in onStart, removed in onStop, leaving the share screen's alone.
    private val cardListener: (XferSnapshot) -> Unit = { showCard(it) }

    /** Holds the screen on show and, for the length of a carry, the one leaving. */
    private val stage by lazy { FrameLayout(this) }

    /** The armed restart or shut down, on every screen the phone shows while connected. */
    private val powerBanner by lazy { PowerBanner(ui) { manager.send(ActionId.POWER_CANCEL.frame()) } }

    /** The stage with the power banner over it; the banner is outside the stage so a screen change never removes it. */
    private val root by lazy {
        FrameLayout(this).apply {
            addView(stage)
            val params =
                FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    Gravity.TOP,
                )
            params.marginStart = ui.dp(Space.PAGE)
            params.marginEnd = ui.dp(Space.PAGE)
            addView(powerBanner.view, params)
        }
    }

    /** True while [navigateBack] runs its synchronous [goTo], so the change plays in reverse. */
    private var backing = false
    private lateinit var gamepads: GamepadStore

    // Volatile: the update check's worker reads it to tell a stopped app from a shown one.
    @Volatile private var started = false

    /** [LinkService] is running, as far as this activity knows; a rebuilt one starts again and finds out in onStart. */
    private var serviceUp = false

    /** What the service's notification says now, so it is only rewritten when it changes. */
    private var shownNotice: LinkNotice? = null

    private var backCallback: Any? = null

    /** Paired devices of every kind in the list, not only the ones that say they are computers. */
    private var showAllDevices = false

    /** Nearby devices has been asked for once by this activity; after that only a tap asks again. */
    private var askedPermission = false

    /** A check with GitHub is in flight, or the last one got no answer. Neither outlives the activity. */
    private var checkingUpdate = false

    /** Skip and Later for the update notice; also read by the daily check's worker. */
    private val updateChoice by lazy { UpdateChoice(applicationContext) }
    private var updateCheckFailed = false

    /** The Updates row's summary while Settings is showing, rewritten in place as a check finishes. */
    private var updateSummaryView: TextView? = null

    /** The system's permission prompt is up, so the Devices list offers no way round it underneath. */
    private var permissionPending = false

    /** Why the prompt that is up was asked, said on the list behind it. */
    private var permissionReason = R.string.permission_reason

    /** The local-network permission has been asked for once, so the list offers the way back and does not nag. */
    private var askedLocalNetwork = false

    /** Notifications have been asked for once; a refusal is fine, as the service runs and Android still lists it. */
    private var askedNotifications = false

    /** Laptops found by NsdManager and by the tether probe, and by the last address typed; the picker merges them. */
    private var wifiFound: List<Found> = emptyList()
    private var usbFound: List<Found> = emptyList()

    /** What the last sweep saw and whether a USB cable is in; the Devices list says both under the list. */
    private var sweep: Sweep? = null

    /** The Wi-Fi has been swept once since the Devices list opened or Refresh was pressed. */
    private var swept = false
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

    // A share still going over the link holds it until the transfer ends; the transfer is paced and has an end.
    private val linger =
        object : Runnable {
            override fun run() {
                when (lingerAction(LinkHooks.sharing.get())) {
                    LingerAction.WAIT -> handler.postDelayed(this, SHARE_RECHECK_MS)
                    LingerAction.CLOSE -> manager.closeAll(LaptopLink.Cause.ENDED)
                }
            }
        }

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
        if (settings.seenVersion.isEmpty()) settings.seenVersion = getString(R.string.app_version)
        gamepads = GamepadStore(this)
        Type.load(this)
        ui = Ui(this)
        permSheet = PermissionSheet(ui) { _, text -> manager.send(TextKind.PERM_ANSWER.frame(text)) }
        sheets.add("permission") { permSheet.dismiss() }
        permNotifier = PermNotifier(applicationContext)
        picker =
            PickerScreen(
                ui,
                onTap = ::tapDevice,
                onVia = { device, transport -> connectTo(device.row, transport) },
                onMakeActive = { device -> device.row.laptop?.let { manager.select(it.id) } },
                onDisconnect = { device -> disconnectRow(device.row) },
                onForget = { device -> forgetLaptop(device.row.laptops, device.row.keys) },
                onPairAgain = { device -> connectTo(device.row, device.row.via, pairAgain = true) },
                onOpenControls = { goTo(Screen.SURFACE) },
                onBluetoothSettings = ::openBluetoothSettings,
                onSettings = ::openSettings,
                onRefresh = {
                    // The one thing that asks the Wi-Fi to be searched again; see [probeTether].
                    swept = false
                    refreshPicker()
                    startScan()
                },
                onShowAll = { all ->
                    showAllDevices = all
                    refreshPicker()
                },
                onAddAddress = ::askAddress,
                onUsbHelp = ::openTetherSettings,
                onDisconnectAll = ::disconnectAll,
                onUpdate = ::openUpdate,
                onSkip = {
                    updateChoice.skip(settings.latestVersion)
                    updateChanged()
                },
                onLater = {
                    updateChoice.later(System.currentTimeMillis())
                    updateChanged()
                },
            )
        discovery =
            Discovery(this) { list ->
                handler.post {
                    wifiFound = list
                    if (screen == Screen.PAIRING) refreshPicker()
                }
            }
        val retained = lastNonConfigurationInstance as? Retained
        // Or the links an activity Android destroyed by itself left behind, with Stay connected on; see onDestroy.
        val kept = retained?.let { LinkSession.Kept(it.manager, it.clipboard, it.apps) } ?: LinkSession.kept
        LinkSession.kept = null
        manager =
            kept?.manager?.also { it.events = this }
                ?: LaptopConnections(laptopLinks(transports(applicationContext)), MainScheduler(), this)
        // The toggle is read through a Settings of its own, so the retained clipboard never holds this activity.
        val owner = Settings(applicationContext)
        // The service ends every link through this and holds nothing of the activity: only the manager, which survives
        // a rebuild, so the new activity sets the same thing again.
        val links = manager
        LinkHooks.disconnectAll = {
            // The service is ending, so links kept for a later activity are not held either.
            LinkSession.kept = null
            links.disconnectAll()
            // As Disconnect on a row: nothing reconnects by itself until the laptop is tapped.
            owner.savedLaptops = encodeSaved(decodeSaved(owner.savedLaptops).map { it.copy(autoConnect = false) })
        }
        if (kept != null) {
            clipboard = kept.clipboard
            apps = kept.apps
        } else {
            // One set of routes, so a link's blobs reach the clipboard or the app list by their kind.
            val routes = BlobRoutes()
            clipboard = clipboardShare(applicationContext, routes) { owner.clipboardSync }
            apps =
                AppLinks(
                    routes,
                    notifier = PhoneNotifier(applicationContext),
                    notifyEnabled = { owner.notifyForward },
                )
        }
        // A laptop's media icon arrived: the surface redraws its Now playing. Cleared in onDestroy with the events.
        apps.onMediaIcon = { laptop -> if (laptop === selected) surface?.stateChanged() }
        apps.onGit = { laptop -> if (laptop === selected) gitScreen?.refresh() }
        apps.onPermAsk = { laptop -> if (laptop === selected) syncPerm() }
        // As above, over the retained pieces alone: what another app shares goes to the selected, connected laptop.
        val shares = clipboard.links
        LinkHooks.shareTarget = {
            links.selected?.takeIf { it.connected }?.let { laptop ->
                val file = Transfers.link(laptop)
                ShareTarget(
                    laptop.name,
                    shares.activeSync(laptop)?.let { it::sendNow },
                    file?.let { link -> { uris -> link.sendAll(uris).allSent } },
                )
            }
        }
        // The tile and the notification's actions, over the manager alone: the selected laptop if it is connected.
        LinkHooks.sendToSelected = { frame ->
            val link = links.selected?.takeIf { it.connected }?.link
            link?.send(frame)
            link != null
        }
        LinkHooks.tileStatus = { links.selected?.takeIf { it.connected }?.let { TileStatus(it.name) } }
        apps.events =
            AppLinks.Events { laptop, token ->
                if (laptop === selected) {
                    if (token == null) appList?.listChanged() else appList?.iconArrived(token)
                }
            }
        if (retained != null) {
            settingsReturn = retained.settingsReturn
            // Or a rotation on a page opened from Settings sends back to the feature screen, which needs the link.
            childFrom = retained.childFrom
            // Or a rotation on Reconnecting forgets the screen to go back to, and returns to the surface instead.
            resumeScreen = retained.resumeScreen
            // Carried over, or the readout would stand blank after a rotation and then restart from one sample.
            picker.setRtt(selected?.rtt?.median() ?: Double.NaN)
        }
        val opening = OpeningScreen.due(retained != null, settings.openingVersion, getString(R.string.app_version))
        if (opening && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) holdSplash()
        goTo(
            when {
                opening -> Screen.OPENING
                !settings.onboarded -> Screen.ONBOARDING
                retained != null -> retained.screen
                else -> Screen.PAIRING
            },
        )
    }

    /** Keeps the Android 12 launch screen up for its whole pop (760 ms, themes.xml), only on the opening's open. */
    private fun holdSplash() {
        val decor = window.decorView
        val until = SystemClock.uptimeMillis() + SPLASH_MS
        decor.viewTreeObserver.addOnPreDrawListener(
            object : ViewTreeObserver.OnPreDrawListener {
                override fun onPreDraw(): Boolean {
                    if (SystemClock.uptimeMillis() < until) return false
                    decor.viewTreeObserver.removeOnPreDrawListener(this)
                    return true
                }
            },
        )
        decor.postDelayed({ decor.invalidate() }, SPLASH_MS)
    }

    override fun onRetainNonConfigurationInstance(): Any =
        Retained(manager, clipboard, apps, screen, settingsReturn, resumeScreen, childFrom)

    override fun onStart() {
        super.onStart()
        handler.removeCallbacks(linger)
        started = true
        refreshPowerBanner()
        clipboard.bridge.start()
        manager.reconnect = settings.reconnect
        // This also starts a lost laptop's retries at once, which is what the reconnect screen wants.
        manager.foreground = true
        syncService()
        phoneWatch?.close()
        phoneWatch = phoneBattery.watch(::refreshBatteries)
        // Rising, so the tick waiting, which may be a slow one from the background, does not hold it up.
        manager.fastPing = screen in showsRtt
        // Back from a detour with the pad still on screen: the laptop has no session, so begin one.
        if (screen == Screen.ANNOTATE && !annotating) goTo(Screen.ANNOTATE)
        // The Presenter's laser ended with it, so it comes back with the switch off.
        if (screen == Screen.PRESENTER) goTo(Screen.PRESENTER)
        // The same for the macro grid: onStop told the laptop to stop looking.
        if (screen == Screen.MACROS) watchMacros()
        systemHub?.setActive(true)
        syncDashWatch()
        // Its "updated" times and expired windows moved while the app was away, with no frame to redraw them.
        aiScreen?.refresh()
        window.decorView.post { syncPerm() }
        // And the app list: onStop told the laptop to stop sending, so it is asked afresh and the old rows go.
        if (screen == Screen.APPS) {
            listApps()
            appList?.listChanged()
        }
        if (!inIntro() && settings.reconnect) connectSaved()
        if (screen == Screen.RECONNECTING) handler.post(ticker)
        // A download begun by an activity a rotation replaced reports here now.
        UpdateJob.onChange = ::updateChanged
        settleUpdateFailure()
        refreshPicker()
        startScan()
        checkForUpdateIfDue()
        Transfers.progress.addListener(cardListener)
        val snapshot = Transfers.progress.snapshot
        if (snapshot.phase != XferPhase.IDLE) showCard(snapshot)
    }

    /** Not over the guide: a first-time user has not been told yet that the app ever goes online. */
    private fun checkForUpdateIfDue() {
        val sinceCheck = System.currentTimeMillis() - settings.lastUpdateCheck
        val due = settings.autoUpdateCheck && sinceCheck >= Updates.CHECK_EVERY_MS
        if (!inIntro() && due) checkForUpdate()
    }

    override fun onStop() {
        super.onStop()
        started = false
        Transfers.progress.removeListener(cardListener)
        xferCard?.hide()
        // Nothing ticks while the app is out of sight; onStart draws it again.
        powerBanner.update(null, "")
        UpdateJob.onChange = null
        clipboard.bridge.stop()
        releaseHeldInput()
        // The laptop drops its overlay when told; a rotation rebuild begins a fresh session in onCreate.
        endAnnotate()
        unwatchMacros()
        cancelApps()
        systemHub?.setActive(false)
        syncDashWatch()
        permSheet.dismiss()
        permNotifier.sync(permSheet.waiting, started)
        manager.foreground = false
        manager.fastPing = false
        phoneWatch?.close()
        phoneWatch = null
        batterySheet?.dismiss()
        clipboardSheet?.dismiss()
        handler.removeCallbacks(ticker)
        stopScan()
        // The service keeps the links outright, a detour for a while and a rebuild too; see the class comment.
        if (!serviceUp) lingerLinks()
        syncFloating()
    }

    /** Gives the links [LINGER_MS] and then ends them, unless a rebuild or a start comes first. */
    private fun lingerLinks() {
        handler.removeCallbacks(linger)
        if (!isChangingConfigurations && manager.laptops.any { it.link != null }) {
            handler.postDelayed(linger, LINGER_MS)
        }
    }

    /**
     * Brings [LinkService] in line with the links: running, with the selected laptop named, while one is connected and
     * Stay connected is on; stopped otherwise. It is only ever started while the activity is on screen, because
     * Android 12 and later refuse a foreground service start from the background, so a laptop that connects while
     * minimized waits for the next onStart. Stopping is allowed from anywhere, and when it happens with the app away
     * the links that remain get the usual wait.
     */
    private fun syncService() {
        syncLinkService()
        syncFloating()
    }

    /**
     * The floating dials are up only while the app is away, the service holds the process, a laptop is connected
     * and the permission is held; any other state takes them down. Nothing is built while the switch is off.
     */
    private fun syncFloating() {
        val want = settings.floatingDials && !started && serviceUp && connected && SystemSettings.canDrawOverlays(this)
        if (!want) {
            floating?.hide()
            return
        }
        val dials = floating ?: FloatingDials(this, settings, state, { frame -> selected?.link?.send(frame) })
        floating = dials
        dials.show()
    }

    private fun syncLinkService() {
        val notice =
            if (settings.stayConnected) linkNotice(manager.laptops, manager.selected, settings.clipboardSync) else null
        if (notice == null) {
            if (!serviceUp) return
            serviceUp = false
            shownNotice = null
            LinkService.stop(applicationContext)
            manager.keepAlive = false
            if (!started) lingerLinks()
            return
        }
        if (!started && !serviceUp) return
        if (serviceUp && notice == shownNotice) return
        // Asked for when the service is first needed. A denial costs only the notification: the service runs anyway.
        if (!serviceUp) askNotifications()
        // Refused, the links get the usual linger and the next onStart tries again.
        if (!LinkService.show(applicationContext, notice, serviceUp)) return
        serviceUp = true
        shownNotice = notice
        // The process is held, so a laptop that drops in the background is still won back; see LaptopConnections.
        manager.keepAlive = true
    }

    /**
     * Turning the laptop-notifications switch on or off: the laptop is told at once, and turning it on is what asks
     * Android 13 and later for the permission. Nothing is ever posted without it, so a refusal only leaves it silent.
     */
    private fun notifyChanged(on: Boolean) {
        for (laptop in manager.laptops) {
            if (laptop.connected && Feature.NOTIFY in laptop.state.features) {
                laptop.link?.send(ControlId.NOTIFY.set(if (on) 1 else 0))
            }
        }
        if (on) askNotifications(forced = true)
    }

    /** Developer mode as the selected laptop reports it, or as the last one reported it while none is connected. */
    private fun developerOn(): Boolean = if (connected) Feature.DEVELOPER in state.features else settings.developerMode

    /**
     * The Developer mode switch: the laptop keeps the value, so it is only told, and its FEATURES that follow are the
     * confirmation. With no laptop connected the switch is disabled and this never runs.
     */
    private fun developerChanged(on: Boolean) {
        if (connected) manager.send(ControlId.DEVELOPER.set(if (on) 1 else 0))
    }

    /** The selected laptop's FEATURES: the cache follows them, and a page showing developer mode is redrawn. */
    private fun developerReported() {
        val on = Feature.DEVELOPER in state.features
        if (on == settings.developerMode) return
        settings.developerMode = on
        if (screen == Screen.SETTINGS || screen == Screen.WIDGETS) goTo(screen)
    }

    // [forced] is the user's own tap, which asks again even when the service already did once.
    private fun askNotifications(forced: Boolean = false) {
        // Below Android 13 notifications need no permission, and the constant does not exist there.
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return
        val granted = checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED
        // One system prompt at a time: while another is up this waits for the next time the service is needed.
        // Not over the guide; onFinish asks once it is over.
        if (granted || (askedNotifications && !forced) || permissionPending || inIntro()) return
        askedNotifications = true
        requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), REQUEST_NOTIFICATIONS)
    }

    // A copy made in another app is readable only once this window has focus.
    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) {
            window.hideBars()
            clipboard.bridge.focused()
        }
    }

    /**
     * Points every link's clipboard at the selected laptop alone; see
     * [me.akshitbansal.edgepad.link.ClipboardLinks].
     */
    private fun shareClipboard() = clipboard.links.refresh(manager.laptops, manager.selected)

    /** Everything a link's blobs feed, brought in line with the laptops: the clipboard and the app lists. */
    private fun refreshBlobLinks() {
        shareClipboard()
        apps.refresh(manager.laptops)
        Transfers.attach(applicationContext, manager.laptops)
    }

    override fun onDestroy() {
        super.onDestroy()
        // A rebuilt activity's waiting ticks go with it; the new one starts its own in onStart.
        handler.removeCallbacksAndMessages(null)
        apps.events = null
        apps.onMediaIcon = null
        apps.onGit = null
        apps.onPermAsk = null
        permSheet.dismiss()
        floating?.hide()
        xferCard?.hide()
        xferCard = null
        if (!isChangingConfigurations && !isFinishing && serviceUp) {
            // Android destroyed this activity by itself while Stay connected holds the process: the links stay, for
            // the next activity to adopt, and the hooks stay too, since they hold only the manager.
            // Locals only, so nothing kept holds this activity.
            val app = applicationContext
            val links = manager
            links.events =
                KeptEvents(links) {
                    links.keepAlive = false
                    LinkSession.kept = null
                    LinkService.stop(app)
                }
            LinkSession.kept = LinkSession.Kept(links, clipboard, apps)
        } else if (!isChangingConfigurations) {
            LinkHooks.disconnectAll = null
            LinkHooks.shareTarget = null
            LinkHooks.sendToSelected = null
            LinkHooks.tileStatus = null
            manager.shutDown()
            LinkService.stop(applicationContext)
        }
    }

    /** Tells the laptop to take its overlay down, once per session, and not at all when there is no link to tell. */
    private fun endAnnotate() {
        if (!annotating) return
        annotating = false
        manager.send(ActionId.ANNOTATE_END.frame())
    }

    /** The Presenter's laser, on while its screen is open: it needs the laptop's overlay, so that is begun first. */
    private fun presenterLaser(on: Boolean) {
        if (!on) return endAnnotate()
        if (!annotating && connected) {
            annotating = true
            manager.send(ActionId.ANNOTATE_BEGIN.frame())
        }
        // The overlay exists now: the saved size, then the tool (plain or with a trail), in that order.
        presenter?.sendLaser()
    }

    /** Asks the laptop to say which macro apps are open, if it can tell. One that never said so is never asked. */
    private fun watchMacros() {
        if (watchingMacros || Feature.RUNNING !in state.features || !connected) return
        watchingMacros = true
        manager.send(ActionId.MACROS_WATCH.frame())
    }

    /**
     * Brings the laptop's watch of the app in front in line with the wish: on only while profiles are on, the laptop
     * offers it and there is a profile to switch to. Sent when that changes, never on a timer.
     */
    private fun syncForegroundWatch() {
        val want =
            ForegroundWatchPolicy.wanted(
                connected = connected,
                offered = Feature.FOREGROUND in state.features,
                profilesOn = settings.profilesOn,
                hasProfiles = Profiles(settings.profiles).all.isNotEmpty(),
                onProfilesScreen = screen == Screen.PROFILES,
                automatic = automaticProfiles(),
            )
        if (want == watchingForeground) return
        watchingForeground = want
        manager.send(ControlId.FOREGROUND_WATCH.set(if (want) 1 else 0))
    }

    /**
     * Tells the laptop what the phone draws, as [DashMask] works it out, and only when that changed: the widgets that
     * are on while the surface shows, and the System, AI or GitHub screen. 0 while the app is away or the link is down.
     */
    private fun syncDashWatch() {
        val dash =
            when (screen) {
                Screen.SYSTEM -> {
                    HubPages.system(statsPage, statsPhone)
                }

                Screen.DEVELOPER -> {
                    HubPages.developer(statsPage)
                }

                else -> {
                    null
                }
            }
        val mask =
            DashMask.of(
                active = connected && started && state.features.any { it in DASH_FEATURES },
                onSurface = screen == Screen.SURFACE,
                statsWidget = settings.widgetOn(SurfaceWidget.SYSTEM) || settings.widgetOn(SurfaceWidget.NETWORK),
                weatherWidget = settings.widgetOn(SurfaceWidget.WEATHER),
                aiWidget = settings.widgetOn(SurfaceWidget.AI) && developerOn(),
                screen = dash,
            )
        if (mask == dashMask) return
        dashMask = mask
        manager.send(ControlId.DASH_WATCH.set(mask))
    }

    /** The name the hubs' chip shows: the laptop's own, else the one it was paired under. */
    private fun hubLaptopName(): String = state.laptopName?.takeIf { it.isNotEmpty() } ?: selected?.name.orEmpty()

    /** Shows the armed countdown over any screen but the Power page, which has its own, and only while connected. */
    private fun refreshPowerBanner() {
        val armed = state.power.takeIf { started && connected && screen != Screen.POWER }
        powerBanner.update(armed, hubLaptopName())
    }

    /** A hub page came to rest: the laptop is told to send that page's data and no other. */
    private fun hubPage(page: Int) {
        statsPage = page
        syncDashWatch()
    }

    /** Stats switched to this phone, or back to a laptop. */
    private fun hubDevice(phone: Boolean) {
        statsPhone = phone
        syncDashWatch()
    }

    /**
     * Keeps the approval sheet and its notice in line with the selected laptop's open asks. Called when an ask comes
     * or goes, the link or the selection changes and the screen does, so the sheet is over whatever is showing, upright
     * or sideways. While the app is away the sheet has no owner and the notice speaks for it.
     */
    private fun syncPerm() {
        val asks = if (connected) state.permAsks else emptyList()
        permSheet.update(if (started) window.decorView else null, asks)
        permNotifier.sync(permSheet.waiting, started)
    }

    /** Whether the dials may follow the kind of app in front: profiles on, automatic on, and the laptop names it. */
    private fun automaticProfiles(): Boolean =
        settings.profilesOn && settings.autoProfiles && Feature.APP_KIND in state.features

    /** The profile that applies to the app in front, or null for the default dials. */
    private fun frontProfile(): Profile? =
        AutoProfiles.resolve(
            custom = state.foreground?.let { Profiles(settings.profiles).forApp(it.token) },
            profilesOn = settings.profilesOn,
            autoOn = automaticProfiles(),
            kind = state.foregroundKind,
            raw = settings.autoProfilesRaw,
        )

    /** The app in front changed: the surface is rebuilt only when that gives another profile than the one drawn. */
    private fun followForeground() {
        // The Profiles screen names the app in front, so it is drawn again when that changes.
        if (screen == Screen.PROFILES && state.foreground?.token != profilesBuiltFor) goTo(Screen.PROFILES)
        val next = frontProfile()
        if (next == settings.activeProfile) return
        settings.activeProfile = next
        if (screen == Screen.SURFACE) goTo(Screen.SURFACE)
    }

    /** A frame of the selected laptop: the dashboard screens redraw from the kinds they show, and a gone ask leaves. */
    private fun refreshDashboard(frame: Frame) {
        if (frame !is Frame.Text) return
        if (frame.kind == TextKind.PERM_GONE.id) syncPerm()
        val kind = TextKind.of(frame.kind)
        if (kind == TextKind.FEATURES || kind == TextKind.UNAVAILABLE) {
            systemScreen?.refresh()
            networkScreen?.refresh()
            aiScreen?.refresh()
            gitScreen?.refresh()
            return
        }
        when (kind) {
            TextKind.STATS -> {
                systemScreen?.refresh()
                // The Network page shows the gateway ping and loss that ride on STATS.
                if (statsPage == HubPages.NETWORK) networkScreen?.refresh()
            }

            TextKind.STATS_INFO, TextKind.DISKS, TextKind.BATTERIES -> {
                systemScreen?.refresh()
            }

            TextKind.NET_INFO, TextKind.SPEED_RESULT -> {
                networkScreen?.refresh()
            }

            TextKind.SESSIONS, TextKind.LIMITS, TextKind.SPEND -> {
                aiScreen?.refresh()
            }

            else -> {
                Unit
            }
        }
    }

    /** Takes the watch back from the laptop being left, whatever it was told, before the selection moves on. */
    private fun unwatchDash() {
        if (dashMask == 0) return
        dashMask = 0
        manager.send(ControlId.DASH_WATCH.set(0))
    }

    private fun unwatchMacros() {
        if (!watchingMacros) return
        watchingMacros = false
        manager.send(ActionId.MACROS_UNWATCH.frame())
    }

    /**
     * Asks the laptop for the apps it could add as macros, if it can say. One that never named `apps` is
     * never asked.
     */
    private fun listApps() {
        if (listingApps || Feature.APPS !in state.features || !connected) return
        // The old list's tokens mean nothing to the new epoch, so none is kept to be tapped before the new one lands.
        state.apps.clear()
        listingApps = true
        manager.send(ActionId.APPS_LIST.frame())
    }

    /** Tells the laptop to stop sending, once per list, and not at all when there is no link to tell. */
    private fun cancelApps() {
        if (!listingApps) return
        listingApps = false
        manager.send(ActionId.APPS_CANCEL.frame())
    }

    /** The refresh button: the list in flight is stopped and another asked for, and the rows show it is awaited. */
    private fun refreshApps() {
        cancelApps()
        listApps()
        appList?.listChanged()
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
        // The volume keys turn slides on the Presenter screen alone; everywhere else they keep the system's job.
        val volume = keyCode == KeyEvent.KEYCODE_VOLUME_UP || keyCode == KeyEvent.KEYCODE_VOLUME_DOWN
        if (screen == Screen.PRESENTER && volume) {
            if (event?.repeatCount == 0) presenter?.step(if (keyCode == KeyEvent.KEYCODE_VOLUME_DOWN) 1 else -1)
            return true
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(
        keyCode: Int,
        event: KeyEvent?,
    ): Boolean {
        val volume = keyCode == KeyEvent.KEYCODE_VOLUME_UP || keyCode == KeyEvent.KEYCODE_VOLUME_DOWN
        return (screen == Screen.PRESENTER && volume) || super.onKeyUp(keyCode, event)
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
        swept = false
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
        // The Wi-Fi is swept address by address once a visit, not every ten seconds: on a shared network that is a
        // knock on every neighbour. USB is always swept, as its subnet is only the laptop's. Refresh sweeps again.
        val spared = wifiFound.isNotEmpty() || swept
        val earlier = usbFound.filter { it.transport == Transport.WIFI }
        thread(name = "edgepad-probe") {
            val result = pass.run(mdnsFound = spared)
            handler.post {
                if (probe === pass) {
                    probe = null
                    if (started && screen == Screen.PAIRING) {
                        swept = true
                        usbFound = mergeFound((if (spared) earlier else emptyList()) + result.found)
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
        // The Presenter's laser is the same session, begun by its switch and ended here by any route out.
        if (screen == Screen.PRESENTER && next != Screen.PRESENTER) endAnnotate()
        if (screen == Screen.MACROS && next != Screen.MACROS) unwatchMacros()
        if (next == Screen.MACROS) watchMacros()
        // The app list is a session on the laptop too: asked for on the way in, cancelled on the way out by any route.
        if (screen == Screen.APPS && next != Screen.APPS) cancelApps()
        if (next == Screen.APPS) listApps()
        if (next == Screen.ANNOTATE && !annotating && connected) {
            annotating = true
            manager.send(ActionId.ANNOTATE_BEGIN.frame())
        }
        // Onto a screen that shows the round trip, the pings speed up now rather than at the next slow tick.
        manager.fastPing = started && next in showsRtt
        if (screen == Screen.SETTINGS && next != Screen.SETTINGS && justUpdated()) {
            settings.seenVersion = getString(R.string.app_version)
        }
        // Settings writes the setting with nothing listening, and every way out of it comes through here. Only on a
        // change, because setting it starts a lost laptop's retry over.
        if (manager.reconnect != settings.reconnect) manager.reconnect = settings.reconnect
        val previous = screen
        // A page opened from Settings goes back there; clearing it at the pages that start a trip of their own.
        if (next in tripStarts) childFrom = null
        screen = next
        // The laptop reports the app in front while the Profiles screen is open, even before there is a profile.
        if ((previous == Screen.PROFILES) != (next == Screen.PROFILES)) syncForegroundWatch()
        // The bars on a page opened from a row of Settings say so; deeper pages keep their own labels.
        val fromSettings = childFrom == Screen.SETTINGS && next in settingsPages
        ui.backOverride = if (fromSettings) getString(R.string.settings_title) else null
        statsPage = 0
        statsPhone = false
        syncDashWatch()
        // Only the surface follows the app in front: every settings page edits the defaults.
        settings.activeProfile = if (next == Screen.SURFACE) frontProfile() else null
        surface = null
        gamepad = null
        annotate = null
        presenter = null
        reconnecting = null
        macros = null
        appList = null
        // Nothing of the phone's own is read once the page is left.
        systemHub?.setActive(false)
        systemHub = null
        systemScreen = null
        networkScreen = null
        aiScreen = null
        gitScreen = null
        powerRedraw = null
        controls = null
        handler.removeCallbacks(ticker)
        if (next == Screen.PAIRING) startScan() else stopScan()
        val send = boundSend()
        val sendKey = { code: Int, down: Boolean -> send(Frame.Key(code, down)) }
        val view: View =
            when (next) {
                Screen.OPENING -> {
                    OpeningScreen.build(
                        ui,
                        getString(R.string.app_version),
                        onGithub = { open(GITHUB_URL) },
                        onContinue = { finishOpening() },
                    )
                }

                Screen.ONBOARDING -> {
                    GuideScreen.build(ui, settings, intro = true, onFinish = {
                        settings.onboarded = true
                        goTo(Screen.PAIRING)
                        if (settings.reconnect) connectSaved()
                        // Both waited for the guide to end: run now, not at the next start.
                        syncService()
                        if (serviceUp) askNotifications()
                        checkForUpdateIfDue()
                    }, onBack = { navigateBack() }, onUses = { goTo(Screen.USES) })
                }

                Screen.GUIDE -> {
                    GuideScreen.build(ui, settings, intro = false, onFinish = {}) { navigateBack() }
                }

                Screen.USES -> {
                    val from = if (settings.onboarded) R.string.settings_title else R.string.app_name
                    DataUsesScreen.build(ui, getString(from)) { navigateBack() }
                }

                Screen.PAIRING -> {
                    picker.view.also { refreshPicker() }
                }

                Screen.SHARING -> {
                    SharingScreen.build(
                        ui,
                        settings,
                        onChanged = {
                            shareClipboard()
                            syncService()
                        },
                        onContinue = {
                            settings.sharingAsked = true
                            goTo(if (connected) Screen.SURFACE else Screen.PAIRING)
                        },
                    )
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
                        versionLine(),
                        getString(backLabel),
                        SettingsScreen.Update(updateSummary()) { view ->
                            // The summary changes with nobody touching it, so a screen reader is told when it does.
                            view.accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
                            updateSummaryView = view
                        },
                        SettingsScreen.Routes(
                            devices = { goTo(Screen.PAIRING) },
                            guide = { goTo(Screen.GUIDE) },
                            uses = { goTo(Screen.USES) },
                            documentation = { open(getString(R.string.documentation_url)) },
                            update = ::openUpdate,
                            licences = ::showLicences,
                            clipboard = {
                                shareClipboard()
                                syncService()
                            },
                            stayConnected = ::syncService,
                            notify = ::notifyChanged,
                            developer = ::developerChanged,
                            surface = { openFrom(Screen.SETTINGS, Screen.SURFACE_SETTINGS) },
                            controls = { openFrom(Screen.SETTINGS, Screen.CONTROLS_SETTINGS) },
                            keyboard = { openFrom(Screen.SETTINGS, Screen.KEYBOARD_SETTINGS) },
                            gamepad = { openFrom(Screen.SETTINGS, Screen.GAMEPAD_SETTINGS) },
                            macros = { openFrom(Screen.SETTINGS, Screen.MACRO_SETTINGS) },
                            presenter = { openFrom(Screen.SETTINGS, Screen.PRESENTER_SETTINGS) },
                            capture = { openFrom(Screen.SETTINGS, Screen.CAPTURE_SETTINGS) },
                            sideways = { on ->
                                settings.landscape = on
                                applyOrientation()
                            },
                            back = { navigateBack() },
                        ),
                        laptops = laptopsSummary(),
                        developer = developerOn(),
                        developerEditable = connected,
                    )
                }

                Screen.SURFACE_SETTINGS -> {
                    SurfaceSettingsScreen.build(
                        ui,
                        settings,
                        getString(R.string.settings_surface),
                        SurfaceSettingsScreen.Routes(
                            rows = surfaceRows(),
                            system = if (childFrom == Screen.SETTINGS) null else ::openSettings,
                            back = { navigateBack() },
                            dialsHidden = dialsHidden,
                            onHideDials = { on ->
                                dialsHidden = on
                                surface?.setDialsHidden(on)
                            },
                            laptopConnected = { connected },
                            onFloatingChanged = ::syncFloating,
                        ),
                    )
                }

                Screen.WIDGETS -> {
                    WidgetsScreen.build(
                        ui,
                        settings,
                        developerOn(),
                        WidgetsScreen.Routes(layout = { goTo(Screen.WIDGET_LAYOUT) }, back = { navigateBack() }),
                    )
                }

                Screen.WIDGET_LAYOUT -> {
                    MediaLayoutScreen.build(ui, settings, MediaLayoutScreen.Scope.WIDGETS, developerOn()) {
                        navigateBack()
                    }
                }

                Screen.PROFILES -> {
                    profilesBuiltFor = state.foreground?.token
                    ProfilesScreen.build(
                        ui,
                        on = settings.profilesOn,
                        setOn = { on ->
                            settings.profilesOn = on
                            syncForegroundWatch()
                            followForeground()
                        },
                        raw = settings.profiles,
                        setRaw = { text ->
                            settings.profiles = text
                            syncForegroundWatch()
                        },
                        current = state.foreground,
                        supported = Feature.FOREGROUND in state.features,
                        autoOn = settings.autoProfiles,
                        setAutoOn = { auto ->
                            settings.autoProfiles = auto
                            syncForegroundWatch()
                            followForeground()
                        },
                        autoRaw = settings.autoProfilesRaw,
                        setAutoRaw = { text ->
                            settings.autoProfilesRaw = text
                            followForeground()
                        },
                        autoSupported = Feature.APP_KIND in state.features,
                        defaults = (0 until Perimeter.CORNERS).map { settings.corner(it) },
                        onBack = { navigateBack() },
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
                    AnnotateScreen(ui, settings, state, send) { navigateBack() }
                        .also { annotate = it }
                        .view
                }

                Screen.PRESENTER -> {
                    PresenterScreen(
                        ui,
                        settings,
                        sendKey,
                        send,
                        onLaser = ::presenterLaser,
                        screenSize = { state.screenSize },
                    ) { navigateBack() }
                        .also { presenter = it }
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
                        send = send,
                    ) { navigateBack() }
                }

                Screen.KEYBOARD_SETTINGS -> {
                    KeyboardSettingsScreen.build(ui, settings) { navigateBack() }
                }

                Screen.GAMEPAD -> {
                    val played = GamepadLayout.forStatus(gamepads.current, state.padStatus)
                    playedLayout = played.name
                    GamepadScreen(
                        ui,
                        played,
                        state.padStatus,
                        state.os,
                        gamepads.all.map { it.name },
                        onPick = { name ->
                            gamepads.choose(name)
                            goTo(Screen.GAMEPAD)
                        },
                        onEdit = { goTo(Screen.GAMEPAD_LAYOUT) },
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
                    ) { navigateBack() }
                }

                Screen.MACROS -> {
                    MacroScreen(
                        ui,
                        settings,
                        state.macros,
                        state.macrosHeard,
                        state.os,
                        icon = state::macroIcon,
                        isOpen = state::isOpen,
                        labels = settings.macroLabels,
                        onRun = ::runMacro,
                        onClose = ::closeMacro,
                        onSettings = { goTo(Screen.MACRO_SETTINGS) },
                        onIconSide = ::requestIcons,
                        onAddFromLaptop = addFromLaptop(),
                    ) { navigateBack() }.also { macros = it }.view
                }

                Screen.CONTROLS_SETTINGS -> {
                    ControlsSettingsScreen.build(ui, settings) { navigateBack() }
                }

                Screen.PRESENTER_SETTINGS -> {
                    PresenterSettingsScreen.build(ui, settings) { navigateBack() }
                }

                Screen.CAPTURE_SETTINGS -> {
                    CaptureSettingsScreen.build(
                        ui,
                        settings,
                        onToPhone = { toPhone ->
                            val target = if (toPhone) CaptureTarget.PHONE else CaptureTarget.LAPTOP
                            CaptureInbox.install(contentResolver, target)
                            if (connected) manager.send(target.frame())
                        },
                    ) { navigateBack() }
                }

                Screen.MACRO_SETTINGS -> {
                    MacroSettingsScreen.build(ui, settings, onAddFromLaptop = addFromLaptop()) { navigateBack() }
                }

                Screen.APPS -> {
                    AppListScreen(
                        ui,
                        selected?.name.orEmpty(),
                        state.apps,
                        windows = state.os == DeviceOs.WINDOWS,
                        macroNames = { state.macros },
                        macroCap = { state.macroCap },
                        // Only the token goes: the laptop looks up what it stands for in the list it made.
                        onAdd = { app -> send(TextKind.APP_ADD.frame(app.token)) },
                        onRefresh = ::refreshApps,
                    ) { navigateBack() }.also { appList = it }.view
                }

                Screen.POWER -> {
                    PowerScreen.build(
                        ui,
                        features = { state.features },
                        power = { state.power },
                        laptopName = ::hubLaptopName,
                        send = { action -> manager.send(action.frame()) },
                        onBack = { navigateBack() },
                        onRedraw = { redraw -> powerRedraw = redraw },
                    )
                }

                Screen.SYSTEM -> {
                    val hub =
                        SystemHub(
                            ui,
                            { state },
                            laptopName = ::hubLaptopName,
                            devices = hubDevices,
                            onDevice = ::hubDevice,
                            rtt = {
                                selected
                                    ?.rtt
                                    ?.median()
                                    ?.takeIf { !it.isNaN() }
                                    ?.toInt() ?: -1
                            },
                            onSpeed = { on -> manager.send(ControlId.SPEED_TEST.set(if (on) 1 else 0)) },
                            onPage = ::hubPage,
                        ) { navigateBack() }
                    systemHub = hub
                    systemScreen = hub.laptop
                    networkScreen = hub.network
                    hub.view
                }

                Screen.DEVELOPER -> {
                    val hub =
                        DeveloperHub(
                            ui,
                            { state },
                            laptopName = ::hubLaptopName,
                            devices = hubDevices,
                            onPage = ::hubPage,
                            onGitOpen = { text -> manager.send(TextKind.GIT_OPEN.frame(text)) },
                        ) { navigateBack() }
                    aiScreen = hub.ai
                    gitScreen = hub.git
                    hub.view
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
                    val attemptsLeft = selected?.pinAttemptsLeft ?: 0
                    PinScreen(
                        ui,
                        attemptsLeft,
                        PinScreen.showsWrong(attemptsLeft, selected?.pinSentOnLink == true),
                        selected?.pinAnswered == true,
                        onAnswer = { pin ->
                            selected?.let {
                                it.pinAnswered = true
                                it.pinSentOnLink = true
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
                        onNewCode = {
                            selected?.let { laptop ->
                                // A new code pairs again over the same transport: never a resume with the old key.
                                val target = (laptop.target as? Target.Network)?.again() ?: laptop.target
                                navigateBack()
                                connect(target, laptop.kind, tapped = true)
                            }
                        },
                    ).view
                }

                Screen.SURFACE -> {
                    ControlSurface(
                        this,
                        settings,
                        state,
                        send,
                        {
                            childFrom = null
                            goTo(Screen.SURFACE_SETTINGS)
                        },
                        onOpenControls = ::openControls,
                        devices = deviceSwitch,
                        batteries = liveBatteries,
                        onOpenBatteries = ::openBatteries,
                    ).also {
                        surface = it
                        if (dialsHidden) it.setDialsHidden(true)
                    }
                }
            }
        // No screenshot, screen recording or recents thumbnail of the PIN or pairing code, and only of those screens.
        // Leaving one of them is an instant swap (showScreen), so the secret is off the stage before the flag clears.
        if (next in secure) {
            window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        } else {
            window.clearFlags(WindowManager.LayoutParams.FLAG_SECURE)
        }
        // These are physical: edge rulers, trackpad, swipes, key rows and layout editors say left and right of the
        // phone, so Arabic mirrors the settings pages around them but never them.
        if (next in immersive) view.layoutDirection = View.LAYOUT_DIRECTION_LTR
        showScreen(view, previous, next)
        applyOrientation()
        syncPerm()
        // The bars are see-through over the page, whose own background is the ground, so they take its colour.
        // Left to the default, three-button navigation would lay a translucent scrim over the ground instead.
        window.isNavigationBarContrastEnforced = false
        window.insetsController?.let { bars ->
            val light =
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or
                    WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS
            bars.setSystemBarsAppearance(if (ui.palette.dark) 0 else light, light)
        }
        // Every screen hides the bars; an edge swipe shows them for a moment (see hideBars).
        window.fillCutout()
        window.hideBars()
        updateBack()
    }

    /**
     * Puts [view] on the stage. A rebuild of the same screen is instant. A control screen on either side of the change
     * only cross-fades, so its touch grid is exact from the first frame (UX-28); so does a change nothing tapped, and
     * Back always has the origin its forward change stored.
     */
    private fun showScreen(
        view: View,
        previous: Screen,
        next: Screen,
    ) {
        Carry.cancel()
        if (root.parent == null) setContentView(root)
        refreshPowerBanner()
        // A secret screen never animates in or out: a carry would keep the PIN or code drawn while FLAG_SECURE flips.
        if (next == previous || previous in secure || next in secure) {
            stage.removeAllViews()
            stage.addView(view)
            return
        }
        Carry.swap(
            stage,
            view,
            stage.getChildAt(stage.childCount - 1),
            forward = !backing,
            crossFadeOnly = next in immersive || previous in immersive || (!backing && !Carry.hasFreshMark()),
        )
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
                sideways || (settings.landscape && screen !in followsPhone) -> {
                    ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
                }

                screen in followsPhone -> {
                    ActivityInfo.SCREEN_ORIENTATION_FULL_SENSOR
                }

                else -> {
                    ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
                }
            }
    }

    /**
     * From Android 16 back arrives only through a callback; it is registered while there is a screen to go
     * back from.
     */
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
        backing = true
        try {
            return stepBack()
        } finally {
            backing = false
        }
    }

    private fun stepBack(): Boolean {
        when (screen) {
            // AND-A-18: a stray edge swipe while sliding a dial must not drop the user on Devices;
            // the second back does.
            Screen.SURFACE -> {
                val now = SystemClock.uptimeMillis()
                if (now - lastSurfaceBack <= BACK_AGAIN_MS) {
                    lastSurfaceBack = 0L
                    goTo(Screen.PAIRING)
                } else {
                    lastSurfaceBack = now
                    Toast.makeText(this, R.string.surface_back_again, Toast.LENGTH_SHORT).show()
                }
            }

            Screen.OPENING -> {
                finishOpening()
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
            Screen.SHAPES, Screen.PROFILES, Screen.WIDGETS,
            -> {
                goTo(Screen.SURFACE_SETTINGS)
            }

            Screen.WIDGET_LAYOUT -> {
                goTo(Screen.WIDGETS)
            }

            Screen.GUIDE -> {
                goTo(Screen.SETTINGS)
            }

            // Reached from the first run's guide, or from Settings once that is over.
            Screen.USES -> {
                goTo(if (settings.onboarded) Screen.SETTINGS else Screen.ONBOARDING)
            }

            Screen.SHAPE_DRAW -> {
                goTo(Screen.SHAPES)
            }

            Screen.GAMEPAD_LAYOUT -> {
                goTo(Screen.GAMEPAD_SETTINGS)
            }

            Screen.KEYBOARD_SETTINGS -> {
                goTo(childFrom ?: Screen.KEYBOARD)
            }

            Screen.GAMEPAD_SETTINGS -> {
                goTo(childFrom ?: Screen.GAMEPAD)
            }

            Screen.MACRO_SETTINGS -> {
                goTo(childFrom ?: Screen.MACROS)
            }

            // Opened from the Settings page's Screens rows only.
            Screen.PRESENTER_SETTINGS, Screen.CAPTURE_SETTINGS -> {
                goTo(childFrom ?: Screen.SETTINGS)
            }

            // From the Controls popup's gear it returns there; from Settings, to Settings.
            Screen.CONTROLS_SETTINGS -> {
                val from = childFrom
                if (from != null) {
                    goTo(from)
                } else {
                    goTo(if (connected) Screen.SURFACE else Screen.PAIRING)
                    // AND-A-32: back lands where the user came from, so the popup opens again over the surface.
                    if (connected) surface?.post { openControls() }
                }
            }

            // Opened from the macros, which are only there while the link holds.
            Screen.APPS -> {
                goTo(if (connected) Screen.MACROS else Screen.PAIRING)
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
            Screen.KEYBOARD, Screen.GAMEPAD, Screen.MACROS, Screen.CAPTURE, Screen.ANNOTATE, Screen.PRESENTER,
            Screen.SYSTEM, Screen.DEVELOPER, Screen.POWER,
            -> {
                goTo(if (connected) Screen.SURFACE else Screen.PAIRING)
                // AND-A-32: back lands where the user came from, so the popup opens again over the surface.
                if (connected) surface?.post { openControls() }
            }

            // From the Settings page's Surface row it goes back there; from the surface's gear, to the surface, which
            // is only there while the link holds.
            Screen.SURFACE_SETTINGS -> {
                goTo(childFrom ?: if (connected) Screen.SURFACE else Screen.PAIRING)
            }

            // Left unanswered, so the question comes again at the next connection.
            Screen.SHARING -> {
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

    /** Asks the system's picker for any file to send to the laptop. Nothing is read until the user has chosen one. */
    private fun pickFile() {
        val intent =
            Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
                addCategory(Intent.CATEGORY_OPENABLE)
                type = "*/*"
                putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true)
            }
        @Suppress("DEPRECATION")
        startActivityForResult(intent, REQUEST_FILE)
    }

    /** The documents the picker returned: every item of a multiple pick, else the single one. */
    private fun pickedFiles(data: Intent?): List<Uri>? {
        val clip = data?.clipData
        if (clip != null && clip.itemCount > 0) return List(clip.itemCount) { clip.getItemAt(it).uri }
        return data?.data?.let(::listOf)
    }

    /**
     * Queues the documents the user chose for the selected laptop; a batch already going out takes them on its end.
     * The user's pick in the system picker is the confirmation. Counted as a share while they go (by the queue), so the
     * app leaving does not end the link under it; the progress and the outcome are the card's and the notification's.
     */
    private fun sendPicked(picked: List<Uri>) {
        val link = selected?.takeIf { it.connected }?.let(Transfers::link)
        if (link == null) {
            Toast.makeText(this, R.string.xfer_no_laptop, Toast.LENGTH_SHORT).show()
            return
        }
        if (picked.size > MAX_FILES) {
            val tooMany = resources.getQuantityString(R.plurals.xfer_too_many, MAX_FILES, MAX_FILES)
            Toast.makeText(this, tooMany, Toast.LENGTH_SHORT).show()
        }
        link.enqueue(picked.take(MAX_FILES))
    }

    /** Shows the transfer card for [s] over the whole screen, making it on first use. */
    private fun showCard(s: XferSnapshot) {
        val card =
            xferCard
                ?: XferCard(
                    ui,
                    stage,
                    onCancel = Transfers::cancel,
                    onPause = { Transfers.pause() },
                    onResume = { Transfers.resume() },
                    onRetry = {
                        if (!Transfers.retryLast()) {
                            Toast.makeText(this, R.string.xfer_no_laptop, Toast.LENGTH_SHORT).show()
                        }
                    },
                    onDone = { Transfers.progress.dismiss() },
                ).also { xferCard = it }
        card.show(s)
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
        if (resultCode != RESULT_OK) return
        if (requestCode == REQUEST_FILE) {
            pickedFiles(data)?.let(::sendPicked)
            return
        }
        if (requestCode != REQUEST_IMAGE) return
        val uri = data?.data
        // Only a document the system picker vouches for: a file:// or this app's own provider would let whatever
        // answered the intent point the copy at Edgepad's private files.
        if (uri == null || !DocumentsContract.isDocumentUri(this, uri)) {
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
        if (refusal == 0) {
            settings.backgroundImageDark = BackgroundImage.peek()?.dark ?: false
            settings.background = Settings.Background.IMAGE
        } else {
            part.delete()
        }
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

    /** The laptop's answer to APP_ADD, as the toast the person is waiting for. */
    private fun added(status: AppAddStatus?) {
        val text =
            when (status) {
                AppAddStatus.ADDED -> R.string.apps_added
                AppAddStatus.FULL -> R.string.apps_full
                AppAddStatus.DUPLICATE -> R.string.apps_duplicate
                AppAddStatus.STALE -> R.string.apps_stale
                else -> R.string.apps_failed
            }
        if (status == AppAddStatus.ADDED) appList?.markAdded()
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

    /** The way into the app list, or null when the selected laptop never said it can list its apps. */
    private fun addFromLaptop(): (() -> Unit)? = if (Feature.APPS in state.features) ::openApps else null

    private fun openApps() = goTo(Screen.APPS)

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
            ControlsPopup(
                ui,
                settings,
                reasons = { state.unavailable },
                features = { state.features },
                developer = ::developerOn,
                laptopConnected = { connected },
                onSendFile = ::pickFile,
                onSettings = { goTo(Screen.CONTROLS_SETTINGS) },
            ) { target ->
                when (target) {
                    ControlsPopup.Target.KEYBOARD -> goTo(Screen.KEYBOARD)
                    ControlsPopup.Target.GAMEPAD -> goTo(Screen.GAMEPAD)
                    ControlsPopup.Target.MACROS -> openMacros()
                    ControlsPopup.Target.CAPTURE -> goTo(Screen.CAPTURE)
                    ControlsPopup.Target.ANNOTATE -> goTo(Screen.ANNOTATE)
                    ControlsPopup.Target.CLIPBOARD -> openClipboardHistory()
                    ControlsPopup.Target.PRESENTER -> goTo(Screen.PRESENTER)
                    ControlsPopup.Target.SYSTEM -> goTo(Screen.SYSTEM)
                    ControlsPopup.Target.DEVELOPER -> goTo(Screen.DEVELOPER)
                    ControlsPopup.Target.POWER -> goTo(Screen.POWER)
                }
            }.also { it.show(owner, controlsCapture(), controlsAnnotate()) }
        sheets.add("controls") { controls?.dismiss() }
    }

    /** The laptop's recent copies over the surface; a tap on one sends it to the laptop again. */
    private fun openClipboardHistory() {
        val owner = surface ?: return
        clipboardSheet?.dismiss()
        clipboardSheet = ClipboardSheet(ui, clipboard.history, onSend = ::sendAgain).also { it.show(owner) }
        sheets.add("clipboard") { clipboardSheet?.dismiss() }
    }

    /** Writes [text] to the selected laptop's clipboard. The transfer blocks, so it runs off the main thread. */
    private fun sendAgain(text: String) {
        val laptop = selected?.takeIf { it.connected }
        val sync = laptop?.let { clipboard.links.activeSync(it) }
        val name = laptop?.name.orEmpty()
        thread(name = "edgepad-clip-resend") {
            val sent = sync?.sendNow(BlobKind.CLIP_TEXT, text.toByteArray()) == true
            handler.post {
                val line =
                    if (sent) getString(R.string.clip_history_sent, name) else getString(R.string.clip_history_failed)
                Toast.makeText(this, line, Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun openBatteries() {
        val owner = surface ?: return
        batterySheet?.dismiss()
        batterySheet = BatterySheet(ui, liveBatteries).also { it.show(owner) }
        sheets.add("battery") { batterySheet?.dismiss() }
    }

    /** A battery, the laptop in use or the set of connected laptops changed: the strip and an open sheet remake. */
    private fun refreshBatteries() {
        surface?.refreshBatteries()
        batterySheet?.refresh()
    }

    /** Opens the macro grid. Its pictures are already cached: the link asks for them as each macro list arrives. */
    private fun openMacros() {
        goTo(Screen.MACROS)
    }

    private fun versionLine(): String =
        getString(R.string.settings_version, getString(R.string.app_version), ProtocolConstants.VERSION)

    /** The Laptops row's summary: the one laptop that is connected, by name, how many when more, or that none is. */
    private fun laptopsSummary(): String {
        val up = manager.laptops.filter { it.connected }
        return when (up.size) {
            0 -> getString(R.string.settings_laptops_none)
            1 -> up[0].name
            else -> resources.getQuantityString(R.plurals.settings_laptops_summary, up.size, up.size)
        }
    }

    /** The surface's pages: back from any of them returns to the surface's settings, which they are opened from. */
    private fun surfaceRows() =
        SurfaceSettingsScreen.Rows(
            corners = { goTo(Screen.CORNERS) },
            gestures = { goTo(Screen.GESTURES) },
            shapes = { goTo(Screen.SHAPES) },
            dialFeel = { goTo(Screen.DIAL_FEEL) },
            mediaLayout = { goTo(Screen.MEDIA_LAYOUT) },
            appearance = { goTo(Screen.APPEARANCE) },
            profiles = { goTo(Screen.PROFILES) },
            widgets = { goTo(Screen.WIDGETS) },
        )

    private fun openFrom(
        from: Screen?,
        page: Screen,
    ) {
        childFrom = from
        goTo(page)
    }

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
        updateChanged()
        val app = applicationContext
        thread(name = "edgepad-update") {
            val latest = Updates.latest()
            if (latest != null) {
                settings.latestVersion = latest
                settings.lastUpdateCheck = System.currentTimeMillis()
                // D-ANDNOTE: only when the check ended with the app out of sight; the notice posts once a version.
                val newer = Updates.isNewer(latest, app.getString(R.string.app_version))
                if (!started && newer && updateChoice.shouldShow(latest, System.currentTimeMillis())) {
                    UpdateNotice.post(app, latest)
                }
            }
            runOnUiThread {
                checkingUpdate = false
                updateCheckFailed = latest == null
                settleUpdateFailure()
                updateChanged()
            }
        }
    }

    /** The Updates row follows the state, and so does the notice on Devices: both say the same thing. */
    private fun updateChanged() {
        updateSummaryView?.text = updateSummary()
        if (screen == Screen.PAIRING) refreshPicker()
    }

    /** A newer release the user has not skipped or put off: what the Devices notice and the Updates row announce. */
    private fun updateAvailable(): Boolean =
        Updates.isNewer(settings.latestVersion, getString(R.string.app_version)) &&
            updateChoice.shouldShow(settings.latestVersion, System.currentTimeMillis())

    /** Ends the opening screen: noted as shown for this version, then on to the guide on a first run or the app. */
    private fun finishOpening() {
        settings.openingVersion = getString(R.string.app_version)
        if (!settings.onboarded) {
            goTo(Screen.ONBOARDING)
            return
        }
        goTo(Screen.PAIRING)
        if (settings.reconnect) connectSaved()
        syncService()
        if (serviceUp) askNotifications()
        checkForUpdateIfDue()
    }

    /** The app is a different version from the one last noted; cleared on leaving Settings, see [goTo]. */
    private fun justUpdated(): Boolean = settings.seenVersion != getString(R.string.app_version)

    /** What the Updates row says. Empty until GitHub has answered once, because nothing is known before that. */
    private fun updateSummary(): String {
        val progress = UpdateJob.progress
        return when {
            progress != null -> getString(R.string.updates_downloading, progress)
            checkingUpdate -> getString(R.string.updates_checking)
            settings.updateFailure != 0 -> getString(R.string.updates_problem)
            updateCheckFailed -> getString(R.string.updates_failed)
            updateAvailable() -> getString(R.string.updates_available, settings.latestVersion)
            justUpdated() -> getString(R.string.updates_updated, getString(R.string.app_version))
            settings.lastUpdateCheck == 0L -> ""
            else -> getString(R.string.updates_current)
        }
    }

    /** A kept failure with no newer release left to install, as after an update by hand, is cleared. */
    private fun settleUpdateFailure() {
        val failure = settings.updateFailure
        if (failure != 0 && !Updates.failureStands(settings.latestVersion, getString(R.string.app_version), failure)) {
            settings.updateFailure = 0
        }
    }

    /**
     * The Updates row, and the notice on Devices: a problem with the last update is explained, a newer release is
     * confirmed before anything is downloaded, and otherwise GitHub is asked afresh.
     */
    private fun openUpdate() {
        when {
            UpdateJob.progress != null -> Unit
            settings.updateFailure != 0 -> explainUpdateFailure()
            updateAvailable() -> confirmUpdate(settings.latestVersion)
            else -> checkForUpdate()
        }
    }

    /** What an update does to the app before it does it: it restarts, which drops every link. */
    private fun confirmUpdate(version: String) {
        val body = getString(R.string.updates_confirm_body)
        val message =
            if (Updates.majorDiffers(version, getString(R.string.app_version))) {
                getString(R.string.sentences_joined, body, getString(R.string.updates_confirm_major))
            } else {
                body
            }
        AlertDialog
            .Builder(this)
            .setTitle(getString(R.string.updates_confirm_title, version))
            .setMessage(message)
            .setPositiveButton(R.string.updates_confirm_action) { _, _ -> installUpdate(version) }
            .setNeutralButton(R.string.updates_release_notes) { _, _ -> open(Updates.releaseUrl(version)) }
            .setNegativeButton(android.R.string.cancel, null)
            .showOver(findViewById<View>(android.R.id.content))
    }

    /** Why the last update did not go through, and the step that fits: Android's own switch, or another try. */
    private fun explainUpdateFailure() {
        val code = settings.updateFailure
        val page = Updates.releaseUrl(settings.latestVersion)
        val dialog =
            AlertDialog
                .Builder(this)
                .setMessage(Installer.failureText(code))
                .setNegativeButton(R.string.updates_release_page) { _, _ -> open(page) }
        if (Installer.isBlocked(code)) {
            dialog.setPositiveButton(R.string.updates_open_settings) { _, _ -> openInstallSettings() }
        } else {
            dialog.setPositiveButton(R.string.updates_try_again) { _, _ ->
                settings.updateFailure = 0
                updateChanged()
                openUpdate()
            }
        }
        dialog.showOver(findViewById<View>(android.R.id.content))
    }

    /** Android's switch for installing from this app, where an update that was blocked can be allowed. */
    private fun openInstallSettings() {
        val page = Uri.fromParts("package", packageName, null)
        try {
            startActivity(Intent(android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, page))
        } catch (e: ActivityNotFoundException) {
            Log.w("Edgepad", "No activity for the install permission", e)
            openAppSettings()
        }
    }

    private fun showLicences() {
        AlertDialog
            .Builder(this)
            .setTitle(R.string.licences_title)
            .setMessage(R.string.licences_body)
            .setPositiveButton(R.string.editor_done, null)
            .showOver(findViewById<View>(android.R.id.content))
    }

    /**
     * Downloads [version] and hands it to the system. Once that is done the row goes back to offering the
     * update: what happens next is the system's question or the app being replaced, and a refusal is
     * reported by [Installer.StatusReceiver], so there is nothing more for the row to wait on.
     */
    private fun installUpdate(version: String) {
        // One download per process: a second tap, or the same tap after a rotation, finds this one running.
        if (!UpdateJob.begin()) return
        settings.updateFailure = 0
        updateChanged()
        // Nothing below holds this activity: a rotation mid-download leaves it to whichever one is on screen.
        val app = applicationContext
        val main = Handler(Looper.getMainLooper())
        thread(name = "edgepad-update") {
            val handed =
                Installer.install(app, version) { percent ->
                    UpdateJob.progress = percent
                    main.post { UpdateJob.onChange?.invoke() }
                }
            // Kept on the row after the Toast is gone: a refusal from the system is kept the same way.
            if (!handed) Settings(app).updateFailure = Installer.DOWNLOAD_FAILED
            UpdateJob.finish()
            main.post {
                UpdateJob.onChange?.invoke()
                if (!handed) Toast.makeText(app, R.string.updates_download_failed, Toast.LENGTH_LONG).show()
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

    /** The first run's guide, or the list of what Edgepad uses opened from it: no permission question yet. */
    private fun inIntro(): Boolean =
        screen == Screen.OPENING || screen == Screen.ONBOARDING || (screen == Screen.USES && !settings.onboarded)

    private fun refreshPicker() {
        // The guide comes first; the permission prompts wait for the list they are asked for.
        if (inIntro()) return
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
            mergedRows(
                laptopRows(
                    remembered,
                    manager.laptops,
                    shown.map { it.address to nameOf(it) },
                    mergeFound(wifiFound + usbFound),
                ),
                selected,
            )
        picker.setScanning(rows.isEmpty() && probe != null)
        val hint =
            pickerHint(
                permissionPending = permissionPending,
                bluetoothOn = adapter?.isEnabled ?: true,
                bluetoothPermitted = permitted || adapter == null,
                // A Bluetooth-only user who said Not now is not nagged on every visit.
                localNetworkGranted = LocalNetwork.granted(this) || settings.localNetworkHintDismissed,
                usbCable = usbCable,
                usbUp = sweep?.let { it.usb != null },
                anyDevices = rows.isNotEmpty(),
            )
        picker.showDevices(
            rows.map { row -> PickerScreen.Device(row, row.laptops.any { it === selected }) },
            connected,
            settings.reconnect,
            hidden = paired.size - laptops.size,
            showingAll = showAllDevices,
            staysConnected = settings.stayConnected,
        )
        when (hint) {
            // First, because Wi-Fi and USB laptops cannot be found without it.
            PickerHint.LOCAL_NETWORK -> {
                val dismiss = {
                    settings.localNetworkHintDismissed = true
                    refreshPicker()
                }
                if (shouldShowRequestPermissionRationale(LocalNetwork.PERMISSION)) {
                    val allow = getString(R.string.allow_local_network)
                    picker.setMessage(getString(R.string.local_network_needed), allow, dismiss) { askLocalNetwork() }
                } else {
                    val settingsLabel = getString(R.string.open_app_settings)
                    picker.setMessage(getString(R.string.local_network_needed), settingsLabel, dismiss) {
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
                // Nothing to fix; but while the system's question is up, the reason for it stays on the list.
                val blocked = selected?.takeIf { it.connected && it.state.inputBlocked }
                picker.setMessage(
                    when {
                        blocked != null -> {
                            val name = blocked.state.laptopName?.takeIf { it.isNotEmpty() } ?: blocked.name
                            getString(R.string.input_blocked, name)
                        }

                        permissionPending -> {
                            getString(permissionReason)
                        }

                        else -> {
                            ""
                        }
                    },
                )
            }
        }
        picker.setStatus(if (rows.isEmpty()) sweepStatus() else "")
        picker.setUpdate(if (updateAvailable()) settings.latestVersion else null)
    }

    /**
     * Where the last sweep looked and found nothing, in plain words. Shown only when no laptop is listed: a list with
     * laptops in it needs no explanation of the network, and the address stays in the log for a bug report.
     */
    private fun sweepStatus(): String {
        val last = sweep ?: return ""
        Log.d("Edgepad", "Sweep: wifi=${last.wifi} usb=${last.usb}")
        val wifi = if (last.wifi != null) R.string.status_searched_wifi else R.string.status_no_wifi
        val usb = if (last.usb != null) R.string.status_searched_usb else null
        val said = getString(wifi)
        return if (usb == null) said else getString(R.string.sentences_joined, said, getString(usb))
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
        // The reason stays on the list behind the system's question, and goes when the answer refreshes it.
        permissionReason = R.string.permission_reason
        picker.setMessage(getString(permissionReason))
        requestPermissions(arrayOf(Manifest.permission.BLUETOOTH_CONNECT), REQUEST_BLUETOOTH)
    }

    /** Some builds have no Bluetooth page to open; the app's own page is the nearest thing. */
    private fun openBluetoothSettings() {
        try {
            startActivity(Intent(android.provider.Settings.ACTION_BLUETOOTH_SETTINGS))
        } catch (e: ActivityNotFoundException) {
            Log.w("Edgepad", "No activity for the Bluetooth settings", e)
            openAppSettings()
        }
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

    /** Bluetooth is on and this app may use it: only then does an empty [bonded] list mean "not paired". */
    private fun bluetoothReady(): Boolean {
        val adapter = getSystemService(BluetoothManager::class.java)?.adapter ?: return false
        return adapter.isEnabled &&
            checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED
    }

    /**
     * Every paired device, of every kind. The Devices list narrows it to computers for itself; everything
     * that connects looks through all of them, so a laptop reached through "show all" once is still found
     * by the reconnect that follows, however its Bluetooth class reads.
     */
    private fun bonded(): List<BluetoothDevice> {
        val adapter = getSystemService(BluetoothManager::class.java)?.adapter ?: return emptyList()
        // Checked here and not through bluetoothReady(), so lint sees the permission check next to its use.
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
            // A laptop that said no is not asked again at every return to the app; a tap still connects it.
            if (live != null && (!live.idle || !LaptopConnections.retryable(live.lastCause))) continue
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

    /** Pairing keys are kept per laptop id, so a network laptop forgotten here is paired from scratch next time. */
    private fun dropPairingKeys(keys: Set<String>) {
        val ids = keys.filter { it.startsWith(NET_KEY) }.mapNotNull { parseHex(it.removePrefix(NET_KEY)) }
        if (ids.isEmpty()) return
        val store = LinkKeys.open(applicationContext)
        thread(name = "edgepad-forget") {
            try {
                ids.forEach(store::forget)
            } catch (e: IOException) {
                Log.w("Edgepad", "Could not delete a pairing key", e)
            }
        }
    }

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

    /**
     * The row's laptop over [via]. Connected already, its link there is replaced by this one. [pairAgain] is the
     * user's own "Pair again" for a network laptop that forgot this phone: this one attempt pairs with a new code.
     */
    private fun connectTo(
        row: LaptopRow,
        via: Transport?,
        pairAgain: Boolean = false,
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
            if (via == Transport.BLUETOOTH) {
                tappedUnpairedBluetooth()
            } else {
                picker.setMessage(getString(R.string.not_paired))
            }
        } else {
            // The row's other record, over the other transport, hands over to this one once it connects, and only
            // this one connects by itself at the next launch.
            val others = row.laptops.filter { it.id != chosen.key }
            saved = onlyAutoConnecting(saved, row.keys, chosen.key)
            val next = if (pairAgain && chosen is Target.Network) chosen.again() else chosen
            connect(next, via, tapped = true, switching = others)
        }
    }

    /** With Bluetooth off or not allowed, the empty bonded list says nothing about the pairing, so that is said. */
    private fun tappedUnpairedBluetooth() {
        val adapterOn = getSystemService(BluetoothManager::class.java)?.adapter?.isEnabled == true
        val permitted =
            checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED
        when (bluetoothTapHint(adapterOn, permitted)) {
            BluetoothTapHint.NOT_READY_OFF -> {
                val settingsLabel = getString(R.string.open_bluetooth_settings)
                picker.setMessage(getString(R.string.bluetooth_tap_off), settingsLabel) { openBluetoothSettings() }
            }

            // The usual question, which already offers the app's settings after a refusal for good.
            BluetoothTapHint.NOT_READY_PERMISSION -> {
                if (!permissionPending) askPermission()
            }

            BluetoothTapHint.READY -> {
                picker.setMessage(getString(R.string.not_paired))
            }
        }
    }

    /** It stays listed and off the automatic connect, until the next time it is connected to. */
    private fun disconnectRow(row: LaptopRow) {
        saved = saved.map { if (it.isIn(row.keys)) it.copy(autoConnect = false) else it }
        row.laptops.forEach { manager.disconnect(it.id) }
        syncService()
        refreshDevices()
    }

    /** Every link ends and none reconnects by itself until it is tapped, as Disconnect on a row does. */
    private fun disconnectAll() {
        saved = saved.map { it.copy(autoConnect = false) }
        manager.disconnectAll()
        syncService()
        refreshDevices()
    }

    private fun askLocalNetwork() {
        askedLocalNetwork = true
        permissionPending = true
        permissionReason = R.string.local_network_reason
        picker.setMessage(getString(permissionReason))
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
        // UX-17: what is wrong is said under the field, in words, and goes as soon as the text is changed.
        val problem =
            ui.text("", Type.SMALL, ui.palette.danger).apply {
                visibility = View.GONE
                val pad = ui.dp(Space.L)
                setPadding(pad, 0, pad, 0)
            }
        val body =
            LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                addView(field)
                addView(problem)
            }

        fun check(): Boolean {
            val typed = field.text.toString().trim()
            val found = if (typed.isEmpty()) AddressProblem.NOT_AN_ADDRESS else addressProblem(typed)
            problem.text =
                when (found) {
                    null -> ""
                    AddressProblem.PORT_OUT_OF_RANGE -> getString(R.string.address_bad_port)
                    AddressProblem.NOT_AN_ADDRESS -> getString(R.string.address_invalid)
                }
            problem.visibility = if (found == null) View.GONE else View.VISIBLE
            return found == null
        }
        field.onEdited { problem.visibility = View.GONE }
        // Leaving the field with something typed in it is the moment to say it is wrong; an empty field is not yet.
        field.setOnFocusChangeListener { _, focused ->
            if (!focused && field.text.isNotBlank()) check()
        }
        val dialog =
            AlertDialog
                .Builder(this)
                .setTitle(R.string.add_by_address)
                .setView(body)
                .setPositiveButton(R.string.address_connect, null)
                .setNegativeButton(android.R.string.cancel, null)
                .openOver(picker.view)
        // Set after the dialog is up, so that an address that is not one leaves it open with the text still in it.
        dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener {
            if (check()) {
                val typed = field.text.toString().trim()
                dialog.dismiss()
                probeManual(parseHostPort(typed) ?: return@setOnClickListener, typed)
            }
        }
    }

    private fun probeManual(
        address: HostPort,
        typed: String,
    ) {
        picker.setMessage(getString(R.string.address_looking, typed))
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
        switching: List<Laptop> = emptyList(),
    ) {
        // A switch keeps the working link selected until the new one is up; the manager hands over then.
        val select = tapped && !connected
        val replacing = switching.map { it.id }
        when (target) {
            is Target.Bluetooth -> {
                manager.connect(target, nameOf(target.device), Transport.BLUETOOTH, tapped, select, replacing)
            }

            is Target.Network -> {
                manager.connect(target, target.name, via ?: Transport.WIFI, tapped, select, replacing)
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
        // Bluetooth off is not "not paired": that attempt fails as unreachable and retries.
        if (target is Target.Bluetooth && bluetoothReady() && bonded().none { it.address == target.key }) {
            val shown = laptop === selected
            manager.stopRetrying(laptop.id)
            if (shown && screen == Screen.RECONNECTING) goTo(Screen.PAIRING)
            if (shown) picker.setMessage(getString(R.string.not_paired))
            return
        }
        if (laptop === selected) {
            CaptureInbox.owner = laptop.link?.id
            watchingMacros = false
            watchingForeground = false
            dashMask = 0
            listingApps = false
            picker.setRtt(Double.NaN)
            picker.setMessage("")
            updateReconnecting()
        }
        refreshDevices()
    }

    override fun onConnected(laptop: Laptop) {
        val first = saved.none { it.id == laptop.target.key }
        remember(laptop)
        syncService()
        if (laptop !== selected) {
            refreshDevices()
            return
        }
        // The trust hint, if it was showing, has been answered.
        picker.setMessage("")
        if (first) {
            window.decorView.performHapticFeedback(HapticFeedbackConstants.CONFIRM)
            Toast.makeText(this, getString(R.string.connected_to, laptop.name), Toast.LENGTH_SHORT).show()
        }
        when (screen) {
            Screen.PAIRING, Screen.RECONNECTING, Screen.PIN, Screen.PAIR_CODE -> {
                // The first time, one short question comes before the controls; see [SharingScreen].
                val back = resumeScreen.takeIf { screen == Screen.RECONNECTING } ?: Screen.SURFACE
                resumeScreen = null
                goTo(if (settings.sharingAsked) back else Screen.SHARING)
            }

            // Opened from Devices, back now leads to the surface the connect would have gone to.
            Screen.SETTINGS -> {
                if (settingsReturn == Screen.PAIRING) settingsReturn = Screen.SURFACE
                refreshDevices()
            }

            else -> {}
        }
    }

    // The row says it is waiting; a second copy under the list would only repeat it.
    override fun onAwaitingLaptop(laptop: Laptop) {
        refreshDevices()
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
        // Said once a connection, for any laptop: the one that offers the clipboard may be the one selected.
        if (frame is Frame.Text && frame.kind == TextKind.FEATURES.id) {
            refreshBlobLinks()
            // The laptop forgets this at the end of a session, so each connection is told again.
            if (Feature.NOTIFY in laptop.state.features) {
                laptop.link?.send(ControlId.NOTIFY.set(if (settings.notifyForward) 1 else 0))
            }
            // The notification only mentions the clipboard once the laptop has said it offers one.
            syncService()
        }
        // TEXT 18, input state: the banner lives on the Devices list, which is the connection status.
        if (frame is Frame.Text && frame.kind == TextKind.INPUT_STATE.id) refreshDevices()
        if (frame is Frame.Text && frame.kind == TextKind.DEVICE.id) {
            remember(laptop)
            refreshDevices()
        }
        if (frame is Frame.Text &&
            (
                frame.kind == TextKind.BATTERIES.id ||
                    frame.kind == TextKind.OUTPUT.id ||
                    frame.kind == TextKind.DEVICE.id
            )
        ) {
            refreshBatteries()
        }
        if (laptop !== selected) return
        if (frame is Frame.Text && frame.kind == TextKind.FEATURES.id) syncForegroundWatch()
        if (frame is Frame.Text && frame.kind == TextKind.FEATURES.id) developerReported()
        if (frame is Frame.Text && frame.kind == TextKind.FEATURES.id) syncDashWatch()
        refreshDashboard(frame)
        val frontId = TextKind.FOREGROUND_APP.id
        val kindId = TextKind.FOREGROUND_KIND.id
        if (frame is Frame.Text && (frame.kind == frontId || frame.kind == kindId)) followForeground()
        surface?.stateChanged()
        floating?.takeIf { it.isShown }?.stateChanged()
        // Capture is drawn from the features the laptop named, which it sends once after the handshake: one that
        // lands while it is open rebuilds it rather than leaving it saying "none". The Controls popup is redrawn.
        val sized = frame is Frame.Text && (frame.kind == TextKind.FEATURES.id || frame.kind == TextKind.SCREEN_SIZE.id)
        if (screen == Screen.CAPTURE && sized) goTo(screen)
        // The grid can be open before the laptop has named what it can do.
        if (screen == Screen.MACROS && frame is Frame.Text && frame.kind == TextKind.FEATURES.id) {
            watchMacros()
            // Built before the laptop said it lists apps, so the way into the list was left out.
            goTo(Screen.MACROS)
        }
        if (screen == Screen.APPS && frame is Frame.Text && frame.kind == TextKind.FEATURES.id) listApps()
        if (frame is Frame.Text && frame.kind == TextKind.APP_ADD_STATUS.id) added(state.apps.lastStatus)
        if (sized) controls?.refresh(controlsCapture(), controlsAnnotate())
        if (frame is Frame.Text &&
            (
                frame.kind == TextKind.POWER_STATE.id ||
                    frame.kind == TextKind.UNAVAILABLE.id ||
                    frame.kind == TextKind.FEATURES.id
            )
        ) {
            powerRedraw?.invoke()
            refreshPowerBanner()
        }
        if (frame is Frame.Text && frame.kind == TextKind.SCREEN_SIZE.id) {
            annotate?.refresh()
            presenter?.refresh()
        }
        if (frame is Frame.Text && frame.kind == TextKind.SIGN_STATUS.id) signed(state.signStatus)
        // The pad decides which of its two modes it is in from this answer, which usually lands a few
        // milliseconds after PAD_ATTACH went out — by then the screen is already up, so it is handed
        // over rather than rebuilt: rebuilding would drop whatever fingers are on the glass.
        if (frame is Frame.Text && frame.kind == TextKind.PAD_STATUS.id) {
            gamepad?.setStatus(state.padStatus)
            // A laptop with no controller driver gets the keyboard preset; the screen is rebuilt once, not each time.
            val wanted = GamepadLayout.forStatus(gamepads.current, state.padStatus).name
            if (screen == Screen.GAMEPAD && wanted != playedLayout) goTo(Screen.GAMEPAD)
        }
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
                    grid.setMacros(state.macros, state.macrosHeard)
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
        refreshBlobLinks()
        syncService()
        refreshBatteries()
        if (laptop === selected && closure.cause == LaptopLink.Cause.REFUSED) {
            window.decorView.performHapticFeedback(HapticFeedbackConstants.REJECT)
        }
        if (laptop !== selected) {
            refreshDevices()
            // Unreachable is a laptop that is off, which is nothing to report at every launch; its row says so.
            if (laptop.failed && laptop.idle && closure.cause != LaptopLink.Cause.UNREACHABLE) {
                picker.setMessage(getString(R.string.status_closed_named, laptop.name, describe(closure, laptop)))
            }
            return
        }
        watchingMacros = false
        watchingForeground = false
        dashMask = 0
        listingApps = false
        syncPerm()
        val answering = screen
        // Devices is where the reason is shown, so an attempt that ends at the PIN or a pairing code goes there first.
        if (screen == Screen.PIN || screen == Screen.PAIR_CODE) goTo(Screen.PAIRING)
        when {
            closure.cause == LaptopLink.Cause.ENDED -> {
                if (screen == Screen.SURFACE || screen == Screen.RECONNECTING || screen == Screen.SHARING ||
                    screen == Screen.CAPTURE || screen == Screen.ANNOTATE || screen == Screen.KEYBOARD ||
                    screen == Screen.GAMEPAD || screen == Screen.MACROS || screen == Screen.APPS ||
                    screen == Screen.PRESENTER || screen in settingsOfControls
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
                // Cancelled by the user; the row is the way back in, and the screen they left says so.
                when (answering) {
                    Screen.PAIR_CODE -> {
                        picker.setMessage(getString(R.string.pair_cancelled))
                    }

                    Screen.PIN -> {
                        picker.setMessage(getString(R.string.pin_cancelled))
                    }

                    else -> {}
                }
            }

            // It was working and dropped on its own, or a retry just failed: say so while the manager tries.
            laptop.reconnecting -> {
                if (screen == Screen.RECONNECTING) {
                    updateReconnecting()
                } else {
                    // AND-A-2: remember where the user was, so coming back puts them there and not on the surface.
                    resumeScreen = screen.takeIf { it in RESUMABLE }
                    goTo(Screen.RECONNECTING)
                }
            }

            // The first connect of a tap never reached the laptop: once more before saying anything.
            laptop.retryTimer != null -> {
                refreshPicker()
            }

            else -> {
                // A retry that stopped for good leaves the Reconnecting screen instead of counting on.
                if (screen == Screen.RECONNECTING) goTo(Screen.PAIRING)
                refreshPicker()
                picker.setMessage(getString(R.string.status_closed, describe(closure, laptop)))
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
        // Controls, battery, clipboard and approvals described the laptop being left; syncPerm brings the new one's.
        sheets.dismissAll()
        sheets.add("permission") { permSheet.dismiss() }
        if (screen == Screen.GAMEPAD) manager.send(ActionId.PAD_DETACH.frame())
        endAnnotate()
        unwatchMacros()
        cancelApps()
        unwatchDash()
        if (screen in laptopScreens) findViewById<ViewGroup>(android.R.id.content).removeAllViews()
    }

    /** Every screen draws [selected], so the one showing is built again on the new laptop, or left if it cannot be. */
    override fun onSelected(laptop: Laptop?) {
        // The watch belonged to the laptop before; the new one is asked afresh once it has named its features.
        watchingForeground = false
        dashMask = 0
        syncForegroundWatch()
        syncDashWatch()
        syncPerm()
        refreshBlobLinks()
        // The dials were built on the old laptop's state; dropped here, syncService builds them on the new one.
        floating?.hide()
        floating = null
        syncService()
        CaptureInbox.owner = laptop?.link?.id
        refreshBatteries()
        picker.setRtt(laptop?.rtt?.median() ?: Double.NaN)
        laptop?.let { saved = promoted(saved, setOf(it.id)) }
        val needsLink = screen in laptopScreens || screen in settingsOfControls || screen == Screen.RECONNECTING
        when {
            screen == Screen.PIN || screen == Screen.PAIR_CODE || (needsLink && !connected) -> {
                goTo(Screen.PAIRING)
            }

            // The list belongs to the laptop it was asked of, so it closes rather than showing another laptop's apps.
            screen == Screen.APPS -> {
                goTo(if (connected) Screen.MACROS else Screen.PAIRING)
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
    private fun describe(
        closure: LaptopLink.Closure,
        laptop: Laptop,
    ): String =
        when (closure.cause) {
            LaptopLink.Cause.ENDED, LaptopLink.Cause.LOST -> getString(R.string.link_lost)
            LaptopLink.Cause.UNREACHABLE -> unreachable(laptop)
            LaptopLink.Cause.REFUSED -> getString(R.string.link_refused)
            LaptopLink.Cause.MISMATCH -> mismatch(closure.detail, laptop)
            LaptopLink.Cause.UNEXPECTED -> unexpected(closure.detail)
            LaptopLink.Cause.NO_PERMISSION -> getString(R.string.link_no_permission)
            LaptopLink.Cause.NO_ANSWER -> getString(R.string.link_no_answer)
            LaptopLink.Cause.PIN_LOCKED -> lockedFor(closure.detail.toIntOrNull() ?: 0)
            LaptopLink.Cause.NOT_PAIRED -> getString(R.string.link_not_paired)
            LaptopLink.Cause.NOT_THIS_LAPTOP -> getString(R.string.link_not_this_laptop)
            LaptopLink.Cause.PAIRING_FAILED -> getString(R.string.link_pairing_failed)
            LaptopLink.Cause.PAIRING_LOCKED -> getString(R.string.link_pairing_locked)
        }

    /** Bluetooth and the network fail for different reasons, and so ask for different things. */
    private fun unreachable(laptop: Laptop): String =
        getString(
            if (laptop.target is Target.Bluetooth) {
                R.string.link_unreachable_bluetooth
            } else {
                R.string.link_unreachable_network
            },
        )

    /** What came back is for the log; to the user it is only "not Edgepad". */
    private fun unexpected(detail: String): String {
        Log.w("Edgepad", "Unexpected handshake reply: $detail")
        return getString(R.string.link_unexpected)
    }

    /** Names the older half when the two protocol numbers say which it is: the higher number is the newer release. */
    private fun mismatch(
        theirs: String,
        laptop: Laptop,
    ): String {
        val name = laptop.state.laptopName?.takeIf { it.isNotEmpty() } ?: laptop.name
        val text =
            when (olderHalf(theirs, ProtocolConstants.VERSION)) {
                OlderHalf.LAPTOP -> R.string.link_mismatch_laptop_older
                OlderHalf.PHONE -> R.string.link_mismatch_phone_older
                OlderHalf.UNKNOWN -> R.string.link_mismatch
            }
        return getString(text, name)
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

    /**
     * Off the remembered list and the manager's, and a network laptop's pairing key with it: the next tap pairs afresh,
     * which is also the way out for a laptop that no longer knows this phone. Android's own Bluetooth pairing is not
     * ours to remove. If it was selected, the next connected laptop is, or none.
     */
    private fun forgetLaptop(
        laptops: List<Laptop>,
        keys: Set<String>,
    ) {
        saved = forgotten(saved, keys)
        laptops.forEach { manager.forget(it.id) }
        dropPairingKeys(keys)
        syncService()
        refreshDevices()
    }

    private companion object {
        const val GITHUB_URL = "https://github.com/akshit-bansal11/edgepad"

        /** The launch screen's pop, android:windowSplashScreenAnimationDuration in themes.xml. */
        const val SPLASH_MS = 760L

        /** Screens that show a secret (the PIN, a pairing code): FLAG_SECURE, and never carried. */
        val secure = setOf(Screen.PIN, Screen.PAIR_CODE)

        val immersive =
            setOf(
                Screen.SURFACE,
                Screen.KEYBOARD,
                Screen.GAMEPAD,
                Screen.CAPTURE,
                Screen.ANNOTATE,
                Screen.PRESENTER,
                Screen.MEDIA_LAYOUT,
                Screen.WIDGET_LAYOUT,
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

        /** Pages that only read: they turn with the phone, whatever the Orientation setting says. */
        val followsPhone =
            setOf(
                Screen.OPENING,
                Screen.ONBOARDING,
                Screen.GUIDE,
                Screen.USES,
                Screen.PAIRING,
                Screen.SHARING,
                Screen.RECONNECTING,
                Screen.PIN,
                Screen.PAIR_CODE,
            )

        /** The pages that need the link: with it gone they go back to Devices, as the screens they belong to do. */
        val settingsOfControls = setOf(Screen.KEYBOARD_SETTINGS, Screen.GAMEPAD_SETTINGS, Screen.MACRO_SETTINGS)

        /** The pages a row of Settings opens; the pages they open in turn are not here. */
        val settingsPages =
            setOf(
                Screen.SURFACE_SETTINGS,
                Screen.CONTROLS_SETTINGS,
                Screen.KEYBOARD_SETTINGS,
                Screen.GAMEPAD_SETTINGS,
                Screen.MACRO_SETTINGS,
                Screen.PRESENTER_SETTINGS,
                Screen.CAPTURE_SETTINGS,
            )

        /**
         * Where a trip out of Settings ends: arriving at any of these forgets which page it began from. The surface's
         * settings are not one: opened from Settings they keep the origin for the pages they open in turn, and the
         * surface's gear clears it itself.
         */
        val tripStarts =
            setOf(
                Screen.SETTINGS,
                Screen.SURFACE,
                Screen.KEYBOARD,
                Screen.GAMEPAD,
                Screen.MACROS,
                Screen.PAIRING,
            )

        /** The control screens a dropped link returns to; Annotate is not here because the laptop ends that session. */
        val RESUMABLE = setOf(Screen.KEYBOARD, Screen.GAMEPAD, Screen.MACROS, Screen.CAPTURE)

        /** The screens built on one laptop's state and sending to it: a change of selection rebuilds them. */
        val laptopScreens =
            setOf(
                Screen.SURFACE,
                Screen.KEYBOARD,
                Screen.GAMEPAD,
                Screen.MACROS,
                Screen.CAPTURE,
                Screen.ANNOTATE,
                Screen.PRESENTER,
                Screen.SYSTEM,
                Screen.DEVELOPER,
                Screen.POWER,
            )

        /** What a laptop names when it can answer DASH_WATCH: one of these, or the phone never sends it. */
        val DASH_FEATURES =
            setOf(Feature.STATS, Feature.NETINFO, Feature.WEATHER, Feature.CLAUDE, Feature.CODEX) +
                GitHost.entries.map { it.feature }

        const val REQUEST_BLUETOOTH = 1
        const val REQUEST_IMAGE = 2
        const val REQUEST_LOCAL_NETWORK = 3
        const val REQUEST_NOTIFICATIONS = 4
        const val REQUEST_FILE = 5

        /** Most files one pick queues. */
        const val MAX_FILES = 100

        /** How often the Devices list looks again for laptops on the network while it is showing. */
        const val SCAN_EVERY_MS = 10_000L

        /**
         * The screens that show the round trip: Devices, beside the connected laptop, live. Everywhere else a ping
         * only has to prove the link is still there.
         */
        val showsRtt = setOf(Screen.PAIRING)

        /** How long a link outlives the app leaving the screen: a trip to the picker, a glance at the lock screen. */
        const val LINGER_MS = 30_000L

        /** How often a linger that found a share still going looks again. */
        const val SHARE_RECHECK_MS = 5_000L
        const val TICK_MS = 1_000L

        const val MILLIS_PER_SECOND = 1_000L

        /** AND-A-18: the window in which a second back on the surface leaves it for Devices. */
        const val BACK_AGAIN_MS = 2_000L

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
                        // Both read on the link thread, not here on the main one: see TcpTransport.handshake.
                        TcpTransport(
                            target.hosts,
                            target.port,
                            target.laptopId,
                            { keys },
                            { phoneName(app) },
                            target.pairAgain,
                        )
                    }
                }
            }
        }

        /** What the laptop's pairing prompt calls this phone: the name set in the phone's settings, else its model. */
        private fun phoneName(context: Context): String =
            Global.getString(context.contentResolver, Global.DEVICE_NAME) ?: Build.MODEL
    }
}
