package me.akshitbansal.edgepad.screens

import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.InsetDrawable
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.PenColor
import me.akshitbansal.edgepad.surface.PenTool
import me.akshitbansal.edgepad.surface.Strokes

/**
 * Drawing on the laptop's screen from the phone: the pen pad, the six tools, the eight colours, undo and
 * clear, and a Sign switch that turns the same pad into a signature. The laptop owns the ink; every button
 * here sends the one action or control value the protocol names for it, and mirrors it in [Strokes] so the
 * preview agrees. The activity sends ANNOTATE_BEGIN before this is built and ANNOTATE_END when it is left, so
 * a screen that is rebuilt (a rotation) starts a clean session on both sides.
 *
 * Sign mode shows nothing on the laptop and accepts only the pen, so the other tools are hidden rather than
 * left to be dropped. Switching either way begins a new session on the laptop, which throws its strokes away,
 * so the preview clears with it.
 */
class AnnotateScreen(
    private val ui: Ui,
    state: LaptopState,
    private val send: (Frame) -> Unit,
    onBack: () -> Unit,
) {
    private val strokes = Strokes()
    private val canvas = AnnotateCanvas(ui.context, strokes, { state.screenSize }, send)
    private val tools = ArrayList<Pair<PenTool, TextView>>()
    private val swatches = ArrayList<View>()
    private val save =
        ui.button(ui.string(R.string.annotate_save_signature), Ui.Style.OUTLINED) {
            send(ActionId.SIGN_SAVE.frame())
        }

    val view: View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(ui.palette.background)
            addView(
                ui.bar(
                    ui.string(R.string.controls_annotate),
                    onBack,
                    backLabel = ui.string(R.string.annotate_exit),
                ),
            )
            canvas.contentDescription = ui.string(R.string.annotate_description)
            addView(canvas, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
            addView(toolbar())
            // Edge-to-edge is enforced: keep the bar and the toolbar clear of the system bars and the cutout.
            setOnApplyWindowInsetsListener { page, insets ->
                val bars = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
                page.setPadding(bars.left, bars.top, bars.right, bars.bottom)
                insets
            }
        }

    init {
        // A new session starts on the laptop's own colour, so say which one the preview starts on.
        sendColour()
        paint()
    }

    /** Redraws the pad, for a laptop display that changed shape under it. */
    fun refresh() = canvas.refresh()

    private fun toolbar(): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            val side = ui.dp(Space.PAGE)
            setPadding(side, ui.dp(Space.S), side, ui.dp(Space.S))
            addView(scroller(pickers()))
            addView(actions())
            save.visibility = View.GONE
            addView(
                save,
                LinearLayout
                    .LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                    .apply { topMargin = ui.dp(Space.S) },
            )
        }

    private fun scroller(row: View): View =
        HorizontalScrollView(ui.context).apply {
            isHorizontalScrollBarEnabled = false
            addView(row)
        }

    /** The tools, then the colours, in one row that scrolls sideways when the phone is too narrow for all. */
    private fun pickers(): View =
        LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            for (tool in PenTool.entries) {
                val pill = toolPill(tool)
                tools.add(tool to pill)
                addView(pill, spaced())
            }
            for (colour in PenColor.entries) {
                val swatch = swatch(colour)
                swatches.add(swatch)
                addView(swatch, LinearLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH)))
            }
        }

    private fun actions(): View =
        LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            addView(
                ui.chip(ui.string(R.string.annotate_undo)) {
                    strokes.undo()
                    send(ActionId.ANNOTATE_UNDO.frame())
                    canvas.refresh()
                },
                spaced(),
            )
            addView(
                ui.chip(ui.string(R.string.annotate_clear)) {
                    strokes.clear()
                    send(ActionId.ANNOTATE_CLEAR.frame())
                    canvas.refresh()
                },
                spaced(),
            )
            addView(
                ui.toggle(ui.string(R.string.annotate_sign), false, ::sign),
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
            )
        }

    private fun spaced() =
        LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            marginEnd = ui.dp(Space.S)
        }

    private fun toolPill(tool: PenTool): TextView {
        val label = ui.string(TOOL_LABELS.getValue(tool))
        return ui.text(label, Type.LABEL, ui.palette.ink, face = Type.bold).apply {
            gravity = Gravity.CENTER
            minHeight = ui.dp(Space.TOUCH)
            setPadding(ui.dp(Space.L), 0, ui.dp(Space.L), 0)
            contentDescription = ui.string(R.string.annotate_tool_desc, label)
            ui.tappable(this, Space.CARD_RADIUS) {
                if (strokes.select(tool)) {
                    send(tool.action.frame())
                    paint()
                }
            }
        }
    }

    private fun swatch(colour: PenColor): View =
        View(ui.context).apply {
            contentDescription = ui.string(COLOUR_LABELS[colour.index])
            ui.tappable(this, Space.TOUCH / 2) {
                if (strokes.selectColour(colour.index)) {
                    sendColour()
                    paint()
                    canvas.refresh()
                }
            }
        }

    private fun sendColour() = send(Frame.SetValue(ControlId.PEN_COLOR.id, strokes.colour))

    /** Each switch begins a new session on the laptop, which keeps no strokes from the last one. */
    private fun sign(on: Boolean) {
        strokes.clear()
        strokes.signing = on
        send((if (on) ActionId.SIGN_BEGIN else ActionId.ANNOTATE_BEGIN).frame())
        sendColour()
        save.visibility = if (on) View.VISIBLE else View.GONE
        paint()
        canvas.refresh()
    }

    /** Redraws the selection: the chosen tool filled, the chosen colour ringed, no tool but the pen when signing. */
    private fun paint() {
        val selected = ui.string(R.string.annotate_selected)
        for ((tool, pill) in tools) {
            val on = tool == strokes.tool
            pill.background = ui.rounded(if (on) ui.palette.accent else ui.palette.faint, Space.CARD_RADIUS)
            pill.setTextColor(if (on) ui.palette.onAccent else ui.palette.ink)
            pill.stateDescription = if (on) selected else null
            pill.visibility = if (strokes.signing && tool != PenTool.PEN) View.GONE else View.VISIBLE
        }
        val inset = (ui.dp(Space.TOUCH) - ui.dp(SWATCH_DP)) / 2
        swatches.forEachIndexed { index, swatch ->
            val on = index == strokes.colour
            val disc =
                GradientDrawable().apply {
                    shape = GradientDrawable.OVAL
                    setColor(OPAQUE or PenColor.entries[index].rgb)
                    setStroke(ui.dp(if (on) RING_DP else HAIR_DP), if (on) ui.palette.accent else ui.palette.line)
                }
            swatch.background = InsetDrawable(disc, inset)
            swatch.stateDescription = if (on) selected else null
        }
    }

    private companion object {
        const val SWATCH_DP = 32f
        const val RING_DP = 3f
        const val HAIR_DP = 1f
        const val OPAQUE = 0xFF000000.toInt()

        val TOOL_LABELS =
            mapOf(
                PenTool.PEN to R.string.annotate_tool_pen,
                PenTool.HIGHLIGHTER to R.string.annotate_tool_highlighter,
                PenTool.LINE to R.string.annotate_tool_line,
                PenTool.ARROW to R.string.annotate_tool_arrow,
                PenTool.RECTANGLE to R.string.annotate_tool_rectangle,
                PenTool.ELLIPSE to R.string.annotate_tool_ellipse,
            )

        /** Indexed by [PenColor.index], which is also the wire value. */
        val COLOUR_LABELS =
            listOf(
                R.string.annotate_colour_black,
                R.string.annotate_colour_white,
                R.string.annotate_colour_red,
                R.string.annotate_colour_orange,
                R.string.annotate_colour_yellow,
                R.string.annotate_colour_green,
                R.string.annotate_colour_blue,
                R.string.annotate_colour_purple,
            )
    }
}
