package me.akshitbansal.edgepad.overlay

import android.content.Context
import android.graphics.PixelFormat
import android.util.Log
import android.view.Gravity
import android.view.HapticFeedbackConstants
import android.view.RoundedCorner
import android.view.WindowManager
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.surface.CornerSlots
import me.akshitbansal.edgepad.surface.DeviceSwitch
import me.akshitbansal.edgepad.surface.Dial
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.DialSpan
import me.akshitbansal.edgepad.surface.Perimeter
import me.akshitbansal.edgepad.surface.RulerGeometry
import me.akshitbansal.edgepad.surface.RulerPainter
import kotlin.math.roundToInt
import android.provider.Settings as SystemSettings

/**
 * The corner dials drawn over other apps, one small window each (see [DialWindow]). Nothing exists until
 * [show] runs and nothing is left after [hide]: no window, no listener, no timer. [show] does nothing without
 * the permission to draw over other apps, which is asked for elsewhere, from a tap.
 */
class FloatingDials(
    context: Context,
    private val settings: Settings,
    private val state: LaptopState,
    private val send: (Frame) -> Unit,
) {
    private val app = context.applicationContext
    private val windowManager = checkNotNull(app.getSystemService(WindowManager::class.java))
    private val density = app.resources.displayMetrics.density
    private val windows = ArrayList<DialWindow>()
    private val dials = ArrayList<Dial>()
    private var shownSpecs: List<Spec> = emptyList()
    private var shownWidth = 0
    private var shownHeight = 0
    private var shownScale = 1f
    private val rebuild = Runnable { show() }
    private val muteText = app.getString(R.string.surface_mute)
    private val unknownText = app.getString(R.string.surface_unknown)

    /** Which dial goes where; two lists of these being equal means the windows already on screen are right. */
    private data class Spec(
        val kind: DialKind,
        val corner: Int,
        val slot: Int,
    )

    val isShown: Boolean get() = windows.isNotEmpty()

    /** Puts the windows up, or lays them out again when the dials or the screen changed since. */
    fun show() {
        if (!SystemSettings.canDrawOverlays(app)) return
        val metrics = windowManager.currentWindowMetrics
        val width = metrics.bounds.width()
        val height = metrics.bounds.height()
        val specs = specs()
        val fontScale = app.resources.configuration.fontScale
        val same = specs == shownSpecs && width == shownWidth && height == shownHeight && fontScale == shownScale
        if (isShown && same) return
        hide()
        if (specs.isEmpty() || width <= 0 || height <= 0) return
        shownSpecs = specs
        shownWidth = width
        shownHeight = height
        shownScale = fontScale
        val insets = metrics.windowInsets
        var corner = 0
        for (position in CORNER_POSITIONS) corner = maxOf(corner, insets.getRoundedCorner(position)?.radius ?: 0)
        val perimeter = Perimeter(width.toFloat(), height.toFloat(), maxOf(corner.toFloat(), MIN_BEND_DP * density))
        val painter = RulerPainter(app.resources.displayMetrics, settings.dialLength, settings.dialHeight)
        for (spec in specs) dials.add(build(spec))
        val half = painter.halfLengthDp * density
        val centres = FloatArray(dials.size)
        for (i in dials.indices) {
            val dial = dials[i]
            centres[i] =
                if (dial.slot == Dial.WHOLE) {
                    perimeter.lengthAt(dial.corner.toFloat())
                } else {
                    perimeter.lengthAtSlot(dial.slot, half / 2f)
                }
        }
        val after = FloatArray(dials.size)
        val before = FloatArray(dials.size)
        DialSpan.compute(centres, half, perimeter.length, FloatArray(0), DIAL_GAP_DP * density, after, before)
        applyState()
        val depth = (RulerGeometry.reach(settings.dialHeight) + labelRoomDp(fontScale)) * density
        val colors = colors()
        for (i in dials.indices) {
            val bounds = DialBounds.of(perimeter, centres[i], before[i], after[i], depth)
            val window = DialWindow(app)
            window.onTurned = rebuild
            window.bind(dials[i], perimeter, painter, centres[i], before[i], after[i], bounds, colors, ::valueText)
            if (!add(window, bounds)) {
                hide()
                return
            }
        }
    }

    /** Takes every window down and lets go of the dials. */
    fun hide() {
        for (window in windows) {
            window.onTurned = null
            try {
                windowManager.removeView(window)
            } catch (e: IllegalArgumentException) {
                Log.w(TAG, "dial window was already gone", e)
            }
        }
        windows.clear()
        dials.clear()
        shownSpecs = emptyList()
    }

    /** The laptop reported something new; [state] already holds it. Draws only while shown. */
    fun stateChanged() {
        if (!isShown) return
        applyState()
        for (window in windows) window.sync(true)
    }

    private fun add(
        window: DialWindow,
        bounds: DialBounds,
    ): Boolean {
        val params =
            WindowManager
                .LayoutParams(
                    bounds.width,
                    bounds.height,
                    WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                        WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL or
                        WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                    PixelFormat.TRANSLUCENT,
                ).apply {
                    gravity = Gravity.TOP or Gravity.START
                    x = bounds.left
                    y = bounds.top
                    layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS
                }
        return try {
            windowManager.addView(window, params)
            windows.add(window)
            true
        } catch (e: RuntimeException) {
            // The permission was withdrawn between the check and the add, or the system refused the window.
            Log.w(TAG, "dial window refused", e)
            false
        }
    }

    private fun specs(): List<Spec> =
        if (settings.splitCorners) {
            (0 until Perimeter.SLOTS).mapNotNull { slot ->
                settings.slot(slot)?.let { Spec(it, CornerSlots.corner(slot), slot) }
            }
        } else {
            (0 until Perimeter.CORNERS).mapNotNull { corner ->
                settings.corner(corner)?.let { Spec(it, corner, Dial.WHOLE) }
            }
        }

    private fun build(spec: Spec): Dial =
        spec.kind.dial(
            spec.corner,
            app.getString(spec.kind.shortRes),
            Dial.BASE_UNITS_PER_DP * settings.sensitivityOf(spec.kind),
            settings.snap,
            send,
            ::haptic,
            spec.slot,
            DeviceSwitch.None,
        )

    private fun haptic() {
        if (settings.haptics) windows.firstOrNull()?.performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
    }

    private fun applyState() {
        for (dial in dials) {
            if (dial.kind == DialKind.REFRESH) {
                dial.maxLevel = (state.refreshRates.size - 1).coerceAtLeast(0).toFloat()
            }
            val control = dial.control ?: continue
            state.level(control)?.let { dial.fromLaptop(it, state.flag(control)) }
        }
    }

    private fun colors(): IntArray {
        val palette = Palette.of(app)
        val ink = settings.controlColor ?: palette.ink
        val accent = settings.controlColor ?: palette.accent
        return intArrayOf(ink, accent, ink and RGB_MASK or DIM_ALPHA)
    }

    private fun valueText(dial: Dial): String =
        when (dial.kind) {
            DialKind.VOLUME -> {
                when {
                    !dial.known -> unknownText
                    dial.flag -> muteText
                    else -> dial.value.toString()
                }
            }

            DialKind.BRIGHTNESS -> {
                if (dial.known) dial.value.toString() else unknownText
            }

            DialKind.REFRESH -> {
                val rate = if (dial.known) state.refreshRates.getOrNull(dial.value) else null
                if (rate != null) app.getString(R.string.dial_refresh_value, rate) else unknownText
            }

            DialKind.MEDIA -> {
                if (dial.known) "${(dial.level / Dial.MAX_LEVEL * PERCENT).roundToInt()}%" else unknownText
            }

            DialKind.ZOOM -> {
                if (dial.steps > 0) "+${dial.steps}" else dial.steps.toString()
            }

            else -> {
                ""
            }
        }

    private companion object {
        const val TAG = "FloatingDials"
        const val MIN_BEND_DP = 24f
        const val DIAL_GAP_DP = 16f
        const val PERCENT = 100

        const val RGB_MASK = 0x00FFFFFF
        const val DIM_ALPHA = 0x8C000000.toInt()
        val CORNER_POSITIONS =
            intArrayOf(
                RoundedCorner.POSITION_TOP_LEFT,
                RoundedCorner.POSITION_TOP_RIGHT,
                RoundedCorner.POSITION_BOTTOM_RIGHT,
                RoundedCorner.POSITION_BOTTOM_LEFT,
            )
    }
}

/** Below the longest mark, the room the label and number need inside the corner, at the default font size. */
private const val LABEL_ROOM_DP = 96f

/** The largest font scale the label room follows; past it the label is cut rather than the window grown. */
private const val MAX_FONT_SCALE = 2f

/** The room under the longest mark for the label and number, grown with the font scale up to [MAX_FONT_SCALE]. */
internal fun labelRoomDp(fontScale: Float): Float = LABEL_ROOM_DP * fontScale.coerceIn(1f, MAX_FONT_SCALE)
