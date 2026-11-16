package me.akshitbansal.edgepad.screens

import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.surface.Strokes

private const val KEY_PAGE_UP = 0x21
private const val KEY_PAGE_DOWN = 0x22

/** The key a slide step presses: Page Down for a positive [delta], Page Up for a negative one. */
internal fun slideKey(delta: Int): Int = if (delta > 0) KEY_PAGE_DOWN else KEY_PAGE_UP

/**
 * Slides from the phone: two big halves that each press one key, and a laser switch that turns the lower area into
 * the annotate pad on the laser tool. The volume keys call [step] only while this screen is in front (wired by the
 * activity), so nothing here listens for anything on its own.
 */
class PresenterScreen(
    private val ui: Ui,
    private val sendKey: (code: Int, down: Boolean) -> Unit,
    private val send: (Frame) -> Unit,
    private val onLaser: (Boolean) -> Unit,
    private val screenSize: () -> Pair<Int, Int>?,
    onBack: () -> Unit,
) {
    private val canvas = AnnotateCanvas(ui.context, Strokes(), screenSize, send)
    private val pad = FrameLayout(ui.context)

    val view: View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(ui.palette.background)
            ui.padToBars(this)
            addView(
                ui.bar(ui.string(R.string.presenter_title), onBack, backLabel = ui.string(R.string.back_to_surface)),
            )
            val gap = ui.dp(Space.S)
            val halves =
                LinearLayout(ui.context).apply {
                    addView(half(R.string.presenter_previous, -1), halfParams(gap, 0))
                    addView(half(R.string.presenter_next, 1), halfParams(0, gap))
                }
            val side = ui.dp(Space.PAGE)
            val wide = ViewGroup.LayoutParams.MATCH_PARENT
            addView(halves, LinearLayout.LayoutParams(wide, 0, 1f).apply { setMargins(side, gap, side, gap) })
            val wrap = ViewGroup.LayoutParams.WRAP_CONTENT
            val laser = ui.toggle(ui.string(R.string.presenter_laser), false) { on: Boolean -> setLaser(on) }
            addView(laser, LinearLayout.LayoutParams(wide, wrap).apply { setMargins(side, 0, side, 0) })
            val note = ui.secondary(ui.string(R.string.presenter_keys_note))
            addView(note, LinearLayout.LayoutParams(wide, wrap).apply { setMargins(side, 0, side, gap) })
            pad.visibility = View.GONE
            pad.addView(canvas, FrameLayout.LayoutParams(wide, wide))
            addView(pad, LinearLayout.LayoutParams(wide, 0, 1f))
        }

    /** One slide: [delta] above zero is next, below is previous. A key down then a key up. */
    fun step(delta: Int) {
        val code = slideKey(delta)
        sendKey(code, true)
        sendKey(code, false)
    }

    /** Redraws the pad, for a laptop display that changed shape under it. */
    fun refresh() = canvas.refresh()

    private fun setLaser(on: Boolean) {
        pad.visibility = if (on) View.VISIBLE else View.GONE
        if (on) send(ActionId.TOOL_LASER.frame())
        onLaser(on)
    }

    private fun half(
        label: Int,
        delta: Int,
    ) = ui.button(ui.string(label), Ui.Style.OUTLINED) { step(delta) }

    private fun halfParams(
        start: Int,
        end: Int,
    ) = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f).apply {
        marginStart = start
        marginEnd = end
    }
}
