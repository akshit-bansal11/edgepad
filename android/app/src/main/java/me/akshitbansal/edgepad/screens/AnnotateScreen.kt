package me.akshitbansal.edgepad.screens

import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.InsetDrawable
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
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
 * The pad fills the screen. One round handle in the bottom corner opens a menu upward from it, holding every
 * control, and choosing a tool closes it again; the handle wears the current tool in the current colour.
 *
 * Sign mode shows nothing on the laptop and accepts only the pen, so the other tools are hidden rather than
 * left to be dropped. Switching either way begins a new session on the laptop, which throws its strokes away,
 * so the preview clears with it.
 */
class AnnotateScreen(
    private val ui: Ui,
    state: LaptopState,
    private val send: (Frame) -> Unit,
    private val onBack: () -> Unit,
) {
    private val strokes = Strokes()
    private val canvas = AnnotateCanvas(ui.context, strokes, { state.screenSize }, send)
    private val tools = ArrayList<Pair<PenTool, ImageView>>()
    private val swatches = ArrayList<View>()
    private val save =
        ui.button(ui.string(R.string.annotate_save_signature), Ui.Style.OUTLINED) {
            send(ActionId.SIGN_SAVE.frame())
        }
    private val menu = menu()
    private val handleTool = glyph(R.drawable.ic_tool_pen, null)
    private val handleChevron = glyph(R.drawable.ic_chevron_up, null)
    private val handle = handle()
    private lateinit var signButton: ImageView

    val view: View =
        FrameLayout(ui.context).apply {
            setBackgroundColor(ui.palette.background)
            canvas.contentDescription = ui.string(R.string.annotate_description)
            addView(canvas, FrameLayout.LayoutParams(MATCH, MATCH))
            addView(
                LinearLayout(ui.context).apply {
                    orientation = LinearLayout.VERTICAL
                    gravity = Gravity.END
                    addView(menu, spaced())
                    addView(handle, LinearLayout.LayoutParams(WRAP, ui.dp(Space.TOUCH)))
                },
                FrameLayout
                    .LayoutParams(WRAP, WRAP, Gravity.BOTTOM or Gravity.END)
                    .apply { setMargins(ui.dp(Space.M), ui.dp(Space.M), ui.dp(Space.M), ui.dp(Space.M)) },
            )
            // Edge-to-edge is enforced: keep the pad and the menu clear of the system bars and the cutout.
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
        open(false)
    }

    /** Redraws the pad, for a laptop display that changed shape under it. */
    fun refresh() = canvas.refresh()

    /** The menu: the tools, the colours in two rows of four, then undo, clear, Sign and Exit. */
    private fun menu(): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            background = ui.rounded(ui.palette.background, Space.CARD_RADIUS)
            elevation = ui.dp(Space.S).toFloat()
            val pad = ui.dp(Space.S)
            setPadding(pad, pad, pad, pad)
            addView(
                row().apply {
                    for (tool in PenTool.entries) {
                        val label = ui.string(R.string.annotate_tool_desc, ui.string(TOOL_LABELS.getValue(tool)))
                        val button =
                            glyph(TOOL_ICONS.getValue(tool), label) {
                                if (strokes.select(tool)) {
                                    send(tool.action.frame())
                                    paint()
                                }
                                open(false)
                            }
                        tools.add(tool to button)
                        addView(button)
                    }
                },
            )
            for (colours in PenColor.entries.chunked(SWATCHES_PER_ROW)) {
                addView(
                    row().apply {
                        for (colour in colours) {
                            val swatch = swatch(colour)
                            swatches.add(swatch)
                            addView(swatch, LinearLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH)))
                        }
                    },
                )
            }
            addView(actions())
            save.visibility = View.GONE
            addView(
                save,
                LinearLayout
                    .LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                    .apply { topMargin = ui.dp(Space.S) },
            )
        }

    private fun row() =
        LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
        }

    private fun actions(): View =
        row().apply {
            addView(
                glyph(R.drawable.ic_undo, ui.string(R.string.annotate_undo)) {
                    strokes.undo()
                    send(ActionId.ANNOTATE_UNDO.frame())
                    canvas.refresh()
                },
            )
            addView(
                glyph(R.drawable.ic_clear, ui.string(R.string.annotate_clear)) {
                    strokes.clear()
                    send(ActionId.ANNOTATE_CLEAR.frame())
                    canvas.refresh()
                },
            )
            signButton = glyph(R.drawable.ic_sign, ui.string(R.string.annotate_sign)) { sign(!strokes.signing) }
            addView(signButton)
            addView(glyph(R.drawable.ic_close, ui.string(R.string.annotate_exit), onBack))
        }

    /** The round opener: the current tool in the current colour, and a chevron that points the way it will go. */
    private fun handle(): View =
        LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            contentDescription = ui.string(R.string.annotate_menu)
            elevation = ui.dp(Space.S).toFloat()
            handleTool.importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            handleChevron.importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            addView(handleTool)
            addView(handleChevron)
            ui.tappable(this, Space.TOUCH / 2) { open(menu.visibility != View.VISIBLE) }
        }

    private fun open(on: Boolean) {
        menu.visibility = if (on) View.VISIBLE else View.GONE
        val chevron = if (on) R.drawable.ic_chevron_down else R.drawable.ic_chevron_up
        handleChevron.setImageDrawable(ui.context.getDrawable(chevron)?.mutate())
        val state = if (on) R.string.annotate_menu_expanded else R.string.annotate_menu_collapsed
        handle.stateDescription = ui.string(state)
        paintHandle()
    }

    /**
     * A 48 dp icon button in the ink, on its own copy of the drawable so tinting it leaves the shared one alone;
     * [label] is null for the handle's own parts, which the handle speaks for.
     */
    private fun glyph(
        icon: Int,
        label: String?,
        onTap: (() -> Unit)? = null,
    ): ImageView =
        ImageView(ui.context).apply {
            scaleType = ImageView.ScaleType.CENTER
            setImageDrawable(
                ui.context
                    .getDrawable(icon)
                    ?.mutate()
                    ?.apply { setTint(ui.palette.ink) },
            )
            layoutParams = LinearLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH))
            if (label != null) {
                contentDescription = label
                tooltipText = label
            }
            if (onTap != null) ui.tappable(this, Space.CARD_RADIUS, onTap)
        }

    private fun spaced() =
        LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = ui.dp(Space.S)
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
        for ((tool, button) in tools) {
            val on = tool == strokes.tool
            mark(button, on)
            button.stateDescription = if (on) selected else null
            button.visibility = if (strokes.signing && tool != PenTool.PEN) View.GONE else View.VISIBLE
        }
        mark(signButton, strokes.signing)
        signButton.stateDescription = if (strokes.signing) selected else null
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
        paintHandle()
    }

    /** A chosen icon button: filled in the accent with its glyph on it, otherwise the glyph in the ink. */
    private fun mark(
        button: ImageView,
        on: Boolean,
    ) {
        button.background = if (on) ui.rounded(ui.palette.accent, Space.CARD_RADIUS) else null
        button.drawable?.setTint(if (on) ui.palette.onAccent else ui.palette.ink)
    }

    /**
     * The handle on the theme's card, or on the theme's ink where the pen colour would vanish into the card, so
     * every colour shows in both themes. Each image is its own copy, so its tint stays off the shared drawable.
     */
    private fun paintHandle() {
        val ink = OPAQUE or PenColor.entries[strokes.colour].rgb
        val onCard = contrast(ink, ui.palette.card) >= MIN_CONTRAST
        handle.background = ui.rounded(if (onCard) ui.palette.card else ui.palette.ink, Space.TOUCH / 2)
        handleTool.setImageDrawable(ui.context.getDrawable(TOOL_ICONS.getValue(strokes.tool))?.mutate())
        handleTool.drawable?.setTint(ink)
        handleChevron.drawable?.setTint(if (onCard) ui.palette.dim else ui.palette.card)
    }

    /** The WCAG contrast ratio between two opaque colours, from 1 for the same to 21 for black on white. */
    private fun contrast(
        a: Int,
        b: Int,
    ): Float {
        val la = Color.luminance(a)
        val lb = Color.luminance(b)
        return (maxOf(la, lb) + FLARE) / (minOf(la, lb) + FLARE)
    }

    private companion object {
        const val MATCH = ViewGroup.LayoutParams.MATCH_PARENT
        const val WRAP = ViewGroup.LayoutParams.WRAP_CONTENT
        const val SWATCH_DP = 32f
        const val RING_DP = 3f
        const val HAIR_DP = 1f
        const val SWATCHES_PER_ROW = 4
        const val OPAQUE = 0xFF000000.toInt()

        // WCAG's 3:1 for graphics: a pen colour under it on the card gets the ink disc instead. FLARE is the
        // formula's allowance for ambient light.
        const val MIN_CONTRAST = 3f
        const val FLARE = 0.05f

        val TOOL_ICONS =
            mapOf(
                PenTool.PEN to R.drawable.ic_tool_pen,
                PenTool.HIGHLIGHTER to R.drawable.ic_tool_highlighter,
                PenTool.LINE to R.drawable.ic_tool_line,
                PenTool.ARROW to R.drawable.ic_tool_arrow,
                PenTool.RECTANGLE to R.drawable.ic_tool_rectangle,
                PenTool.ELLIPSE to R.drawable.ic_tool_ellipse,
            )

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
