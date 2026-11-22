package me.akshitbansal.edgepad.gallery

import android.content.Context
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import me.akshitbansal.edgepad.LaserSizes
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.gamepad.GamepadStore
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.xfer.XferPhase
import me.akshitbansal.edgepad.link.xfer.XferSnapshot
import me.akshitbansal.edgepad.protocol.BatteryKind
import me.akshitbansal.edgepad.protocol.DeviceOs
import me.akshitbansal.edgepad.screens.ControlsPopup
import me.akshitbansal.edgepad.screens.FnMode
import me.akshitbansal.edgepad.screens.GamepadLayoutScreen
import me.akshitbansal.edgepad.screens.KeyboardScreen
import me.akshitbansal.edgepad.screens.KeyboardSize
import me.akshitbansal.edgepad.screens.LaserSheet
import me.akshitbansal.edgepad.screens.MediaLayoutScreen
import me.akshitbansal.edgepad.screens.RingGauge
import me.akshitbansal.edgepad.screens.Ui
import me.akshitbansal.edgepad.screens.XferCardView
import me.akshitbansal.edgepad.surface.BatteryGroup
import me.akshitbansal.edgepad.surface.BatteryGroups
import me.akshitbansal.edgepad.surface.BatteryRow
import me.akshitbansal.edgepad.surface.BatterySource
import me.akshitbansal.edgepad.surface.ControlSurface

/** The sample state behind each gallery screen name. A new screen adds one case to [build]. */
class GallerySamples(
    private val context: Context,
) {
    private val ui = Ui(context)
    private val settings = Settings(context)

    /** The screen called [name], or null when there is none. */
    fun build(name: String): View? =
        when (name) {
            "rings" -> {
                rings()
            }

            "xfer-run", "xfer-paused" -> {
                xferOverSurface(paused = name == "xfer-paused")
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

            // The laser sheet opens over an empty page (see showLaserSheet).
            "presenter-sheet" -> {
                blank()
            }

            // The Controls popup opens over the surface it grows from (see showControls).
            "surface", "controls" -> {
                ControlSurface(context, settings, LaptopState(), {}, {}, {}, batteries = chargingLaptopAndPhone)
            }

            else -> {
                null
            }
        }

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

    /** Opens the layout editor's options panel by tapping its options button inside [root]. */
    fun openOptions(root: View) {
        val found = ArrayList<View>()
        val label = context.getString(R.string.editor_options)
        root.findViewsWithText(found, label, View.FIND_VIEWS_WITH_CONTENT_DESCRIPTION)
        found.firstOrNull()?.performClick()
    }

    /** Opens the Presenter's laser sheet over [owner] with Medium chosen and a 16:9 laptop. */
    fun showLaserSheet(owner: View) {
        settings.laserSize = LaserSizes.MEDIUM
        LaserSheet(ui, settings, { Pair(16, 9) }) {}.show(owner)
    }

    /** Opens the Controls popup over [owner], every tile available. */
    fun showControls(owner: View) {
        ControlsPopup(ui, settings) {}.show(owner, capture = true, annotate = true)
    }
}
