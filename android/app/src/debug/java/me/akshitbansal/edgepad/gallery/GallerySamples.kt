package me.akshitbansal.edgepad.gallery

import android.content.Context
import android.graphics.Rect
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import me.akshitbansal.edgepad.LaserSizes
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.gamepad.GamepadStore
import me.akshitbansal.edgepad.link.BatteryHealth
import me.akshitbansal.edgepad.link.ByteRatio
import me.akshitbansal.edgepad.link.GitWire
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.NetBand
import me.akshitbansal.edgepad.link.NetKind
import me.akshitbansal.edgepad.link.PhoneBatteryInfo
import me.akshitbansal.edgepad.link.PhoneNet
import me.akshitbansal.edgepad.link.PhoneSnapshot
import me.akshitbansal.edgepad.link.SignalLevel
import me.akshitbansal.edgepad.link.ThermalLevel
import me.akshitbansal.edgepad.link.xfer.XferPhase
import me.akshitbansal.edgepad.link.xfer.XferSnapshot
import me.akshitbansal.edgepad.protocol.BatteryKind
import me.akshitbansal.edgepad.protocol.DeviceOs
import me.akshitbansal.edgepad.protocol.TextKind
import me.akshitbansal.edgepad.screens.AiScreen
import me.akshitbansal.edgepad.screens.AppearanceScreen
import me.akshitbansal.edgepad.screens.ControlsPlan
import me.akshitbansal.edgepad.screens.ControlsPopup
import me.akshitbansal.edgepad.screens.DataUsesScreen
import me.akshitbansal.edgepad.screens.DeviceChip
import me.akshitbansal.edgepad.screens.FnMode
import me.akshitbansal.edgepad.screens.GamepadLayoutScreen
import me.akshitbansal.edgepad.screens.GitScreen
import me.akshitbansal.edgepad.screens.KeyboardScreen
import me.akshitbansal.edgepad.screens.KeyboardSize
import me.akshitbansal.edgepad.screens.LaserSheet
import me.akshitbansal.edgepad.screens.MediaLayoutScreen
import me.akshitbansal.edgepad.screens.NetworkScreen
import me.akshitbansal.edgepad.screens.PhoneScreen
import me.akshitbansal.edgepad.screens.RingGauge
import me.akshitbansal.edgepad.screens.SettingsScreen
import me.akshitbansal.edgepad.screens.ShapeDrawScreen
import me.akshitbansal.edgepad.screens.StencilIcon
import me.akshitbansal.edgepad.screens.SystemScreen
import me.akshitbansal.edgepad.screens.Ui
import me.akshitbansal.edgepad.screens.XferCardView
import me.akshitbansal.edgepad.screens.hubBar
import me.akshitbansal.edgepad.surface.BatteryGroup
import me.akshitbansal.edgepad.surface.BatteryGroups
import me.akshitbansal.edgepad.surface.BatteryRow
import me.akshitbansal.edgepad.surface.BatterySource
import me.akshitbansal.edgepad.surface.ControlSurface
import me.akshitbansal.edgepad.surface.SurfaceWidget

/** The sample state behind each gallery screen name. A new screen adds one case to [build]. */
class GallerySamples(
    private val context: Context,
) {
    private val ui = Ui(context)
    private val settings = Settings(context).also { it.landscape = ui.landscape }

    /** The screen called [name], or null when there is none. */
    fun build(name: String): View? =
        when (name) {
            "rings" -> {
                rings()
            }

            "xfer-run", "xfer-paused" -> {
                xferOverSurface(paused = name == "xfer-paused")
            }

            "stats-system" -> {
                SystemScreen(ui, { sampleLaptop }, sysBar()).view
            }

            "stats-system-phone" -> {
                val phone = PhoneScreen(ui).apply { show(samplePhone) }
                SystemScreen(ui, { sampleLaptop }, sysBar(onPhone = true), phone).apply { showPhone(true) }.view
            }

            "stats-network" -> {
                NetworkScreen(ui, sysBar(), { sampleLaptop }, { SAMPLE_RTT_MS }, {}).view
            }

            "stats-network-phone" -> {
                NetworkScreen(ui, sysBar(onPhone = true), { sampleLaptop }, { SAMPLE_RTT_MS }, {})
                    .apply {
                        phoneChanged(samplePhone)
                        showPhone(true)
                    }.view
            }

            "ai-two", "ai-four" -> {
                val laptop = aiLaptop(four = name == "ai-four")
                AiScreen(ui, { laptop }, devBar(R.string.dev_ai)).view
            }

            "git" -> {
                GitScreen(ui, { gitLaptop }, devBar(R.string.dev_git)) {}.view
            }

            // The app's Settings, top to bottom; "settings-capture" is the same page scrolled to Capture & Annotate.
            "settings", "settings-capture" -> {
                SettingsScreen.build(
                    ui,
                    settings,
                    "Edgepad 5.0.0 · protocol 6",
                    ui.string(R.string.pairing_title),
                    SettingsScreen.Update(ui.string(R.string.updates_title)) {},
                    SettingsScreen.Routes(
                        devices = {},
                        guide = {},
                        documentation = {},
                        update = {},
                        licences = {},
                        uses = {},
                        sideways = {},
                        clipboard = {},
                        stayConnected = {},
                        back = {},
                    ),
                )
            }

            "data-uses" -> {
                DataUsesScreen.build(ui, ui.string(R.string.settings_title)) {}
            }

            "look" -> {
                AppearanceScreen.build(ui, settings, {}, {})
            }

            "shape-pick" -> {
                // A stroke already drawn, so the screen opens on the list of what it can run.
                val stroke = floatArrayOf(-0.3f, -0.3f, 0f, 0.3f, 0.3f, -0.3f)
                ShapeDrawScreen.build(ui, settings, emptyList(), {}, {}, stroke)
            }

            "keyboard" -> {
                KeyboardScreen.build(
                    ui,
                    settings.keyTextScale,
                    KeyboardSize.FULL,
                    FnMode.WITH_FN,
                    DeviceOs.WINDOWS,
                    onKey = { _, _ -> },
                    onAction = {},
                    onSettings = {},
                    send = {},
                    onBack = {},
                )
            }

            "gamepad-layout" -> {
                GamepadLayoutScreen.build(ui, GamepadStore(context)) {}
            }

            "media-layout" -> {
                MediaLayoutScreen.build(ui, settings) {}
            }

            "widget-layout" -> {
                // The editor offers only the widgets that are switched on.
                for (widget in listOf(SurfaceWidget.CLOCK, SurfaceWidget.WEATHER, SurfaceWidget.SYSTEM)) {
                    settings.setWidgetOn(widget, true)
                }
                MediaLayoutScreen.build(ui, settings, MediaLayoutScreen.Scope.WIDGETS) {}
            }

            // The laser sheet opens over an empty page (see showLaserSheet).
            "presenter-sheet" -> {
                blank()
            }

            // The Controls popup opens over the surface it grows from (see showControls): 4 or 12 to a page.
            "surface", "controls", "controls-4", "controls-12" -> {
                // Set each time: preferences outlive a run, and a shot must not depend on the last.
                val perPage = name.substringAfter("controls-", "").toIntOrNull()
                settings.controlsPerPage = perPage ?: ControlsPlan.DEFAULT_PER_PAGE
                settings.controlsIconPercent = ControlsPlan.DEFAULT_ICON_PERCENT
                ControlSurface(context, settings, LaptopState(), {}, {}, {}, batteries = chargingLaptopAndPhone)
            }

            // The thirteen Controls icons, large, as the popup draws them on its own background.
            "stencil-icons" -> {
                stencils()
            }

            else -> {
                null
            }
        }

    private fun sysBar(onPhone: Boolean = false): View {
        val name = if (onPhone) ui.string(R.string.battery_this_phone) else SAMPLE_LAPTOP
        return ui.hubBar(ui.string(R.string.sys_title), DeviceChip(ui, name, onPhone) {}, {})
    }

    private fun devBar(title: Int): View = ui.hubBar(ui.string(title), DeviceChip(ui, SAMPLE_LAPTOP, false) {}, {})

    /** A laptop that said who it is and what it can, with a minute of numbers, two disks, and a network and a test. */
    private val sampleLaptop: LaptopState by lazy {
        LaptopState().apply {
            take(TextKind.DEVICE.frame("0123456789abcdef0123456789abcdef/windows/$SAMPLE_LAPTOP"))
            take(TextKind.FEATURES.frame("stats/netinfo"))
            take(TextKind.STATS_INFO.frame("Ryzen 7 7840HS|32768|GeForce RTX 4060|8192|Wi-Fi"))
            repeat(SAMPLE_SECONDS) { second ->
                val cpu = SAMPLE_BASE + second % SAMPLE_SWING
                take(TextKind.STATS.frame("$cpu/52/${cpu / 2}/20/${SAMPLE_BASE * second}/40/3/0/58/61/45"))
            }
            take(TextKind.DISKS.frame("C:62:953:361|D:35:1863:1210"))
            take(
                TextKind.BATTERIES.frame(
                    "laptop:97:1:94:34:$SAMPLE_LAPTOP|mouse:64:0:-:-:MX Master 3|headphones:18:0:-:-:WH-1000",
                ),
            )
            take(TextKind.NET_INFO.frame("wifi|5|1200|85|192.168.1.23|192.168.1.1"))
            take(TextKind.SPEED_RESULT.frame("done|94200|38100|9|2|$SAMPLE_TEST_MINUTE|96000000"))
        }
    }

    /** A laptop signed in to GitHub and GitLab, with GitHub's page: three repositories and a few items. */
    private val gitLaptop: LaptopState by lazy {
        val now = System.currentTimeMillis() / MS_PER_MIN
        val rows =
            listOf(
                "H\tgithub\takshit-bansal11\tAkshit Bansal\t24\t12\t1",
                "P\takshit-bansal11/edgepad\t\tKotlin\t31\t3\t1",
                "P\takshit-bansal11/plimsoll\tp\tTypeScript\t4\t0\t0",
                "P\takshit-bansal11/spotatui\t\tRust\t12\t2\t12",
                "M\takshit-bansal11/edgepad\t128\tFix the Git page badge\td\tfail\topen\t${now - 12}",
                "M\takshit-bansal11/edgepad\t131\tDashboard sessions pill\t\tok\topen\t${now - 190}",
                "M\takshit-bansal11/plimsoll\t7\tRelease notes for 2.1\t\tnone\topen\t${now - 2_900}",
                "R\takshit-bansal11/spotatui\t88\tQueue view keeps its place\t\tpending\topen\t${now - 45}",
                "A\takshit-bansal11/edgepad\t120\tTray menu crashes on Wayland\ti\tnone\topen\t${now - 700}",
            )
        LaptopState().apply {
            take(TextKind.DEVICE.frame("0123456789abcdef0123456789abcdef/windows/$SAMPLE_LAPTOP"))
            take(TextKind.FEATURES.frame("github/gitlab"))
            setGit(GitWire.parse(rows.joinToString("\n").toByteArray()))
        }
    }

    /** A laptop with Claude and Codex, and with Gemini CLI and OpenCode too when [four]: limits, spend and sessions. */
    private fun aiLaptop(four: Boolean): LaptopState {
        val now = System.currentTimeMillis() / MS_PER_MIN
        val measured = now - 1
        return LaptopState().apply {
            take(TextKind.DEVICE.frame("0123456789abcdef0123456789abcdef/windows/$SAMPLE_LAPTOP"))
            take(TextKind.FEATURES.frame(if (four) "claude/codex/gemini/opencode" else "claude/codex"))
            val limits =
                listOf(
                    "claude:300:34:${now + 180}:$measured",
                    "claude:10080:62:${now + 4_320}:$measured",
                    "claude:~seven_day_fable:81:${now + 5_760}:$measured",
                    "claude:~spend_limit:23:${now + 20_000}:$measured",
                    "codex:300:12:${now + 240}:$measured",
                    "codex:10080:93:${now + 1_440}:$measured",
                )
            take(TextKind.LIMITS.frame(limits.joinToString("|")))
            val spend =
                listOf(
                    "claude/$measured/1200000/210/9400000/1480/30000000/5200/0",
                    "codex/$measured/260000/40/3100000/620/11000000/2100/0",
                    "gemini/$measured/182000/0/940000/0/3000000/0/0",
                    "opencode/$measured/64000/0/410000/0/1900000/0/0",
                )
            take(TextKind.SPEND.frame(spend.take(if (four) 4 else 2).joinToString("|")))
            val sessions =
                listOf(
                    "claude:busy:42:edgepad",
                    "claude:idle:81:plimsoll",
                    "codex:wait:18:spotatui",
                    "gemini:idle:-1:site",
                    "opencode:busy:-1:notes",
                )
            take(TextKind.SESSIONS.frame(sessions.joinToString("|")))
        }
    }

    private val samplePhone =
        PhoneSnapshot(
            battery = PhoneBatteryInfo(52, true, 310, BatteryHealth.GOOD),
            memory = ByteRatio(3_300_000_000L, 6_400_000_000L),
            storage = ByteRatio(78_000_000_000L, 128_000_000_000L),
            net = PhoneNet(NetKind.WIFI, NetBand.GHZ_5, 433, SignalLevel.GOOD, "192.168.1.41"),
            thermal = ThermalLevel.NONE,
        )

    /** A charging laptop at 97% and this phone at 52%, for the surface's battery strip. */
    private val chargingLaptopAndPhone =
        BatterySource {
            val laptop = BatteryRow("Akshit's M42", BatteryKind.LAPTOP, false, 97, true, "Akshit's M42")
            BatteryGroups(
                BatteryGroup("Akshit's M42", listOf(laptop)),
                emptyList(),
                BatteryRow("Pixel", null, true, 52, false, "Pixel"),
            )
        }

    /** Four usage rings at 34, 62, 81 and 93 percent, one per level colour, on a card. */
    private fun rings(): View {
        val gap = ui.dp(Space.L)
        val row =
            LinearLayout(context).apply {
                setBackgroundColor(ui.palette.card)
                setPadding(gap, gap, gap, gap)
            }
        intArrayOf(34, 62, 81, 93).forEach { pct ->
            val ring = RingGauge(context).apply { set(pct, "$pct%") }
            val params = LinearLayout.LayoutParams(ui.dp(88f), ViewGroup.LayoutParams.WRAP_CONTENT)
            params.marginEnd = gap
            row.addView(ring, params)
        }
        return FrameLayout(context).apply {
            setBackgroundColor(ui.palette.background)
            val wrap = ViewGroup.LayoutParams.WRAP_CONTENT
            addView(row, FrameLayout.LayoutParams(wrap, wrap))
        }
    }

    /** The transfer card, 12.4 of 48 MB of a file moving or held, in the bottom corner of the surface. */
    private fun xferOverSurface(paused: Boolean): View {
        val surface = ControlSurface(context, settings, LaptopState(), {}, {}, {}, batteries = chargingLaptopAndPhone)
        val moving =
            XferSnapshot(
                phase = XferPhase.RUNNING,
                sending = true,
                index = 1,
                total = 1,
                name = "report-final.pdf",
                sent = 12_400_000L,
                size = 48_000_000L,
                bytesPerSecond = 1_200_000L,
                done = 0,
                failed = emptyList(),
                cancelling = false,
                paused = paused,
            )
        val card = XferCardView(context).apply { bind(moving) }
        return FrameLayout(context).apply {
            val match = ViewGroup.LayoutParams.MATCH_PARENT
            addView(surface, FrameLayout.LayoutParams(match, match))
            val gap = ui.dp(Space.PAGE)
            val corner = Gravity.BOTTOM or Gravity.END
            val params = FrameLayout.LayoutParams(ui.dp(320f), ViewGroup.LayoutParams.WRAP_CONTENT, corner)
            params.setMargins(gap, gap, gap, gap)
            addView(card, params)
        }
    }

    /** An empty page in the app's background: the answer to an unknown name. */
    fun blank(): View = FrameLayout(context).apply { setBackgroundColor(ui.palette.background) }

    /** Every [StencilIcon.Kind] large with its name, on the popup's own background, for the owner to approve. */
    private fun stencils(): View {
        val side = ui.dp(STENCIL_DP)
        val pad = ui.dp(Space.M)
        val perRow = if (ui.landscape) STENCIL_ROW_SIDEWAYS else STENCIL_ROW_UPRIGHT
        return LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setBackgroundColor(ui.palette.popover)
            for (kinds in StencilIcon.Kind.entries.chunked(perRow)) {
                addView(
                    LinearLayout(context).apply {
                        gravity = Gravity.CENTER
                        for (kind in kinds) {
                            addView(
                                LinearLayout(context).apply {
                                    orientation = LinearLayout.VERTICAL
                                    gravity = Gravity.CENTER_HORIZONTAL
                                    setPadding(pad, pad, pad, pad)
                                    val picture = StencilIcon(kind, ui.palette.ink, ui.palette.accent)
                                    addView(ImageView(context).apply { setImageDrawable(picture) }, side, side)
                                    addView(ui.text(kind.name.lowercase(), Type.SMALL, ui.palette.dim))
                                },
                            )
                        }
                    },
                )
            }
        }
    }

    /** Opens the layout editor's options panel by tapping its options button inside [root]. */
    fun openOptions(root: View) {
        val found = ArrayList<View>()
        val label = context.getString(R.string.editor_options)
        root.findViewsWithText(found, label, View.FIND_VIEWS_WITH_CONTENT_DESCRIPTION)
        found.firstOrNull()?.performClick()
    }

    /** Scrolls the Settings page in [root] to its Capture & Annotate section, so the pen rows are in the shot. */
    fun showCaptureSection(root: View) {
        val found = ArrayList<View>()
        root.findViewsWithText(found, context.getString(R.string.settings_capture), View.FIND_VIEWS_WITH_TEXT)
        val heading = found.firstOrNull() ?: return
        heading.requestRectangleOnScreen(Rect(0, 0, heading.width, root.height / 2), true)
    }

    /** Opens the Presenter's laser sheet over [owner] with Medium chosen and a 16:9 laptop. */
    fun showLaserSheet(owner: View) {
        settings.laserSize = LaserSizes.MEDIUM
        LaserSheet(ui, settings, { Pair(16, 9) }) {}.show(owner)
    }

    /** Opens the Controls popup over [owner], every tile available. */
    fun showControls(owner: View) {
        ControlsPopup(ui, settings, developer = { true }) {}.show(owner, capture = true, annotate = true)
    }

    private companion object {
        const val STENCIL_DP = 72f
        const val STENCIL_ROW_SIDEWAYS = 7
        const val STENCIL_ROW_UPRIGHT = 4
        const val SAMPLE_LAPTOP = "Akshit's M42"
        const val SAMPLE_RTT_MS = 6
        const val MS_PER_MIN = 60_000L
        const val SAMPLE_SECONDS = 60
        const val SAMPLE_BASE = 30
        const val SAMPLE_SWING = 20

        /** 10:42 on a day in November 2026, in UTC minutes since the Unix epoch. */
        const val SAMPLE_TEST_MINUTE = 29_800_000L
    }
}
