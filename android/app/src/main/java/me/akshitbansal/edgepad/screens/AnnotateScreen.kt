package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.content.Context
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.InsetDrawable
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.PenColor
import me.akshitbansal.edgepad.surface.PenTool
import me.akshitbansal.edgepad.surface.Strokes

/**
 * Drawing on the laptop's screen from the phone: the pen pad, the six tools, the eight colours and a custom one,
 * a width and an opacity, undo and clear, and a Sign switch that turns the same pad into a signature. The laptop
 * owns the ink; every button here sends the one action or control value the protocol names for it, and mirrors it
 * in [Strokes] so the preview agrees. The colour, width and opacity are kept in [settings] and sent again each time
 * the pad opens, because the laptop forgets them when its session ends. The activity sends ANNOTATE_BEGIN before
 * this is built and ANNOTATE_END when it is left, so a screen that is rebuilt (a rotation) starts a clean session on
 * both sides.
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
    private val settings: Settings,
    state: LaptopState,
    private val send: (Frame) -> Unit,
    private val onBack: () -> Unit,
) {
    private val strokes = Strokes(settings.penRgb, settings.penWidth, settings.penOpacity)
    private val canvas = AnnotateCanvas(ui.context, strokes, { state.screenSize }, send)
    private val tools = ArrayList<Pair<PenTool, ImageView>>()
    private val swatches = ArrayList<View>()
    private lateinit var customSwatch: View
    private val save =
        ui.button(ui.string(R.string.annotate_save_signature), Ui.Style.OUTLINED) {
            send(ActionId.SIGN_SAVE.frame())
        }
    private val menu = menu()
    private val menuScroll = scrolling(menu)
    private val handleTool = glyph(R.drawable.ic_tool_pen, null)
    private val handleChevron = glyph(R.drawable.ic_chevron_up, null)
    private val handle = handle()
    private lateinit var signButton: ImageView

    val view: View =
        FrameLayout(ui.context).apply {
            setBackgroundColor(ui.palette.background)
            canvas.contentDescription = ui.string(R.string.annotate_description)
            addView(canvas, FrameLayout.LayoutParams(MATCH, MATCH))
            EditorFrame.addBack(this, ui, onBack)
            addView(
                LinearLayout(ui.context).apply {
                    orientation = LinearLayout.VERTICAL
                    gravity = Gravity.END
                    addView(menuScroll, spaced())
                    addView(handle, LinearLayout.LayoutParams(WRAP, ui.dp(Space.TOUCH)))
                },
                FrameLayout
                    .LayoutParams(WRAP, WRAP, Gravity.BOTTOM or Gravity.END)
                    .apply { setMargins(ui.dp(Space.M), ui.dp(Space.M), ui.dp(Space.M), ui.dp(Space.M)) },
            )
            ui.padToBars(this)
        }

    init {
        // A new session starts on the laptop's own pen, so say which one the preview starts on.
        sendStyle()
        paint()
        open(false)
    }

    /** Redraws the pad, for a laptop display that changed shape under it. */
    fun refresh() = canvas.refresh()

    /**
     * The menu: the tools, the colours in two rows with the custom one last, the width and the opacity, then undo,
     * clear, Sign and Exit.
     */
    private fun menu(): LinearLayout =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
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
            customSwatch = customSwatch()
            swatches.addAll(PenColor.entries.map { swatch(it) })
            for (colours in (swatches + customSwatch).chunked(SWATCHES_PER_ROW)) {
                addView(
                    row().apply {
                        for (swatch in colours) {
                            addView(swatch, LinearLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH)))
                        }
                    },
                )
            }
            addView(widthSlider())
            addView(opacitySlider())
            addView(actions())
            save.visibility = View.GONE
            addView(
                save,
                LinearLayout
                    .LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                    .apply { topMargin = ui.dp(Space.S) },
            )
        }

    /**
     * The menu, scrolled, and never taller than the room a short screen leaves it: the sliders made it taller than a
     * phone held sideways. The rounded card is the scroll view's, so it keeps its corners at any height.
     */
    private fun scrolling(content: View): View =
        CappedScroll(ui.context) { (ui.context.resources.displayMetrics.heightPixels * MENU_SCREEN_SHARE).toInt() }
            .apply {
                background = ui.rounded(ui.palette.background, Space.CARD_RADIUS)
                clipToOutline = true
                elevation = ui.dp(Space.S).toFloat()
                isVerticalScrollBarEnabled = false
                addView(content)
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
            ui.tappable(this, Space.TOUCH / 2) { open(menuScroll.visibility != View.VISIBLE) }
        }

    private fun open(on: Boolean) {
        menuScroll.visibility = if (on) View.VISIBLE else View.GONE
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
                if (strokes.selectColour(colour.index)) colourChanged()
            }
        }

    /** The swatch after the eight: it shows the picked colour when there is one, and opens the picker. */
    private fun customSwatch(): View =
        View(ui.context).apply {
            contentDescription = ui.string(R.string.annotate_colour_custom)
            ui.tappable(this, Space.TOUCH / 2) { pickColour() }
        }

    /** The picker the Appearance settings use, in a dialog; each colour it gives takes effect as it is tapped. */
    private fun pickColour() {
        val picker =
            ColorPicker.build(ui, ui.string(R.string.annotate_colour_custom), OPAQUE or strokes.rgb) { colour ->
                if (strokes.selectRgb(colour and RGB_MASK)) colourChanged()
            }
        val body =
            LinearLayout(ui.context).apply {
                setBackgroundColor(ui.palette.background)
                setPadding(ui.dp(Space.L), ui.dp(Space.M), ui.dp(Space.L), 0)
                addView(picker, LinearLayout.LayoutParams(MATCH, WRAP))
            }
        AlertDialog
            .Builder(ui.context)
            .setView(body)
            .setPositiveButton(android.R.string.ok, null)
            .showOver(view)
    }

    private fun colourChanged() {
        settings.penRgb = strokes.rgb
        send(Strokes.colourFrame(strokes.rgb))
        paint()
        canvas.refresh()
    }

    private fun widthSlider(): View =
        ui.slider(
            ui.string(R.string.annotate_width),
            WIDTH_STEPS,
            (strokes.width / Strokes.WIDTH_PER_SIZE - 1).coerceIn(0, WIDTH_STEPS),
            { step -> ui.string(R.string.annotate_width_value, step + 1) },
        ) { step ->
            if (strokes.selectWidth((step + 1) * Strokes.WIDTH_PER_SIZE)) {
                settings.penWidth = strokes.width
                send(ControlId.PEN_WIDTH.set(strokes.width))
            }
        }

    private fun opacitySlider(): View =
        ui.slider(
            ui.string(R.string.annotate_opacity),
            OPACITY_STEPS,
            ((strokes.opacity - Strokes.MIN_OPACITY) / Strokes.OPACITY_STEP).coerceIn(0, OPACITY_STEPS),
            { step -> ui.string(R.string.percent_value, Strokes.MIN_OPACITY + step * Strokes.OPACITY_STEP) },
        ) { step ->
            if (strokes.selectOpacity(Strokes.MIN_OPACITY + step * Strokes.OPACITY_STEP)) {
                settings.penOpacity = strokes.opacity
                send(ControlId.PEN_OPACITY.set(strokes.opacity))
            }
        }

    /** The pen's colour, width and opacity, which a laptop only keeps for as long as its session. */
    private fun sendStyle() = strokes.styleFrames().forEach(send)

    /** Each switch begins a new session on the laptop, which keeps no strokes from the last one. */
    private fun sign(on: Boolean) {
        strokes.clear()
        strokes.signing = on
        send((if (on) ActionId.SIGN_BEGIN else ActionId.ANNOTATE_BEGIN).frame())
        sendStyle()
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
        // A picked colour that is one of the eight rings that swatch: the laptop draws the same either way.
        val stock = PenColor.entries.indexOfFirst { it.rgb == strokes.rgb }
        swatches.forEachIndexed { index, swatch ->
            val on = index == stock
            swatch.background = disc(PenColor.entries[index].rgb, on)
            swatch.stateDescription = if (on) selected else null
        }
        customSwatch.background = disc(strokes.rgb.takeIf { stock < 0 }, stock < 0)
        customSwatch.stateDescription = if (stock < 0) selected else null
        paintHandle()
    }

    /** A round swatch inset in its touch target, ringed in the accent when chosen; a null [rgb] is the spectrum. */
    private fun disc(
        rgb: Int?,
        on: Boolean,
    ): InsetDrawable {
        val disc =
            GradientDrawable().apply {
                shape = GradientDrawable.OVAL
                if (rgb != null) {
                    setColor(OPAQUE or rgb)
                } else {
                    setGradientType(GradientDrawable.SWEEP_GRADIENT)
                    setColors(SPECTRUM)
                }
                setStroke(ui.dp(if (on) RING_DP else HAIR_DP), if (on) ui.palette.accent else ui.palette.line)
            }
        return InsetDrawable(disc, (ui.dp(Space.TOUCH) - ui.dp(SWATCH_DP)) / 2)
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
        val ink = OPAQUE or strokes.rgb
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
        const val SWATCHES_PER_ROW = 5
        const val OPAQUE = 0xFF000000.toInt()
        const val RGB_MASK = 0xFFFFFF

        // One slider position per size of WIDTH_PER_SIZE steps from size 1, and per OPACITY_STEP percent from the lowest.
        const val WIDTH_STEPS = Strokes.MAX_WIDTH / Strokes.WIDTH_PER_SIZE - 1
        const val OPACITY_STEPS = (Strokes.MAX_OPACITY - Strokes.MIN_OPACITY) / Strokes.OPACITY_STEP

        /** The menu may take this much of the screen's height before it scrolls. */
        const val MENU_SCREEN_SHARE = 0.65f

        /** Round the wheel, for the swatch that has no colour of its own yet. */
        val SPECTRUM =
            listOf(PenColor.RED, PenColor.YELLOW, PenColor.GREEN, PenColor.BLUE, PenColor.PURPLE, PenColor.RED)
                .map { OPAQUE or it.rgb }
                .toIntArray()

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

/** A scroll view that is never taller than [limit] pixels, so its content can outgrow a short screen and scroll. */
private class CappedScroll(
    context: Context,
    private val limit: () -> Int,
) : ScrollView(context) {
    override fun onMeasure(
        widthMeasureSpec: Int,
        heightMeasureSpec: Int,
    ) {
        super.onMeasure(widthMeasureSpec, MeasureSpec.makeMeasureSpec(limit(), MeasureSpec.AT_MOST))
    }
}
