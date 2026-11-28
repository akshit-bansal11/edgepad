package me.akshitbansal.edgepad.screens

import android.content.res.Configuration
import android.util.TypedValue
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.LaserSizes
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.surface.Strokes

private const val KEY_PAGE_UP = 0x21
private const val KEY_PAGE_DOWN = 0x22
private const val SIDE_WEIGHT = 15f
private const val PAD_WEIGHT = 70f
private const val BOTTOM_ROW_DP = 96f
private const val CHEVRON_SP = 40f
private const val FINGER_SCALE = 2.5f
private const val TIP_KEY = "presenter"
private const val MATCH = ViewGroup.LayoutParams.MATCH_PARENT

/** The key a slide step presses: Page Down for a positive [delta], Page Up for a negative one. */
internal fun slideKey(delta: Int): Int = if (delta > 0) KEY_PAGE_DOWN else KEY_PAGE_UP

/** The laser tool the laptop is told to use: the plain dot, or the dot with its trail. */
internal fun laserTool(trail: Boolean): ActionId = if (trail) ActionId.TOOL_LASER_TRAIL else ActionId.TOOL_LASER

/**
 * The phone dot's radius for a laser [size] (the wire's PEN_WIDTH scale) on a pad [boxHeight] tall: the laptop's
 * share of its screen height, a little larger so a fingertip does not hide it.
 */
internal fun laserDotRadius(
    boxHeight: Float,
    size: Int,
): Float = LaserSizes.radius(boxHeight, size) * FINGER_SCALE

/**
 * Slides from the phone. The stage is always the laser pad, with Previous and Next beside it in landscape (narrow
 * columns at either end) or under it in portrait. The laser is on for as long as the screen is open; the activity
 * ends the laptop's overlay when the screen is left. The volume keys call [step] only while this screen is in front
 * (wired by the activity), so nothing here listens for anything on its own.
 */
class PresenterScreen(
    private val ui: Ui,
    private val settings: Settings,
    private val sendKey: (code: Int, down: Boolean) -> Unit,
    private val send: (Frame) -> Unit,
    private val onLaser: (Boolean) -> Unit,
    private val screenSize: () -> Pair<Int, Int>?,
    private val onSettings: () -> Unit,
    onBack: () -> Unit,
) {
    private val canvas = AnnotateCanvas(ui.context, Strokes(), screenSize, send, laser = true)
    private val gap = ui.dp(Space.S)

    private val landscape = ui.context.resources.configuration.orientation == Configuration.ORIENTATION_LANDSCAPE

    private val page =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(ui.palette.background)
            ui.padToBars(this)
            val options = ui.icon(Glyph.Shape.OPTIONS, ui.string(R.string.presenter_settings)) { onSettings() }
            addView(
                ui.bar(
                    ui.string(R.string.presenter_title),
                    onBack,
                    options,
                    backLabel = ui.string(R.string.back_to_surface),
                ),
            )
            val side = ui.dp(Space.PAGE)
            val wide = ViewGroup.LayoutParams.MATCH_PARENT
            val wrap = ViewGroup.LayoutParams.WRAP_CONTENT
            canvas.hint = ui.string(R.string.presenter_point_here)
            canvas.laserSize = settings.laserSize
            canvas.laserTrail = settings.laserTrail
            val stage = LinearLayout.LayoutParams(wide, 0, 1f).apply { setMargins(side, gap, side, gap) }
            val previous = ui.button(ui.string(R.string.presenter_previous), Ui.Style.OUTLINED) { step(-1) }
            val next = ui.button(ui.string(R.string.presenter_next), Ui.Style.FILLED) { step(1) }
            if (landscape) {
                addView(landscapeStage(previous, next), stage)
            } else {
                addView(canvas, stage)
                val row =
                    LinearLayout(ui.context).apply {
                        addView(previous, LinearLayout.LayoutParams(0, wide, 1f).apply { marginEnd = gap })
                        addView(next, LinearLayout.LayoutParams(0, wide, 2f))
                    }
                addView(
                    row,
                    LinearLayout.LayoutParams(wide, ui.dp(BOTTOM_ROW_DP)).apply { setMargins(side, 0, side, gap) },
                )
            }
            // The laser is on for as long as the screen is open: the activity begins the overlay, then calls sendLaser.
            addOnAttachStateChangeListener(
                object : View.OnAttachStateChangeListener {
                    override fun onViewAttachedToWindow(v: View) = onLaser(true)

                    override fun onViewDetachedFromWindow(v: View) = Unit
                },
            )
        }

    val view: View =
        FrameLayout(ui.context).apply {
            addView(page, FrameLayout.LayoutParams(MATCH, MATCH))
            // Above the Previous / Next row in portrait; on the pad in landscape, between its Previous / Next columns.
            val clear = if (landscape) Space.L else BOTTOM_ROW_DP + Space.L + Space.S
            TipCard.show(this, settings, TIP_KEY, StencilIcon.Kind.PRESENTER, R.string.tip_presenter, clear)
        }

    /** Previous column, laser pad, Next column: the side columns are full height and a fraction of the width. */
    private fun landscapeStage(
        previous: TextView,
        next: TextView,
    ): LinearLayout {
        val tall = ViewGroup.LayoutParams.MATCH_PARENT
        for (column in listOf(previous, next)) {
            column.contentDescription = column.text
            column.setTextSize(TypedValue.COMPLEX_UNIT_SP, CHEVRON_SP)
        }
        previous.text = "‹"
        next.text = "›"
        return LinearLayout(ui.context).apply {
            addView(previous, LinearLayout.LayoutParams(0, tall, SIDE_WEIGHT).apply { marginEnd = gap })
            addView(canvas, LinearLayout.LayoutParams(0, tall, PAD_WEIGHT))
            addView(next, LinearLayout.LayoutParams(0, tall, SIDE_WEIGHT).apply { marginStart = gap })
        }
    }

    /** One slide: [delta] above zero is next, below is previous. A key down then a key up. */
    fun step(delta: Int) {
        val code = slideKey(delta)
        sendKey(code, true)
        sendKey(code, false)
    }

    /** Redraws the pad, for a laptop display that changed shape under it. */
    fun refresh() = canvas.refresh()

    /** Tells the laptop the saved laser size, then the tool. Called once the overlay exists and on every change. */
    fun sendLaser() {
        send(ControlId.PEN_WIDTH.set(settings.laserSize))
        send(laserTool(settings.laserTrail).frame())
    }
}
