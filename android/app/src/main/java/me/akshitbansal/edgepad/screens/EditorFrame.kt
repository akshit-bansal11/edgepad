package me.akshitbansal.edgepad.screens

import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.InsetDrawable
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.PopupWindow
import android.widget.ScrollView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/**
 * What the media and gamepad layout editors and the gamepad itself share, after the design: the canvas
 * fills the screen and the way back, the title and an options button float over it. Options open in a
 * card sheet over a dimmed screen, which a tap outside or back closes.
 *
 * Two ways back, as the design has them. With a [build] `backLabel` it is the nav bar every other
 * sub-screen has, an accent "‹ Settings" link and a centred title, over the canvas. Without one, for the
 * gamepad and its editor, where the whole screen is controls, it is a round card-filled button in each
 * corner and a small title between them. Each round button is drawn smaller than the touch target it
 * answers to.
 */
object EditorFrame {
    /** A round button's drawn size, and where it is drawn from the corner. */
    private const val ROUND_DP = 40f
    private const val EDGE_DP = 8f
    private const val TOP_DP = 6f
    private const val TITLE_TOP_DP = 15f
    private const val POPUP_WIDTH_DP = 220f
    private const val POPUP_LEFT_DP = 14f
    private const val POPUP_TOP_PAD_DP = 10f
    private const val POPUP_RIGHT_DP = 12f
    private const val POPUP_GAP_DP = 8f
    private const val POPUP_ELEVATION_DP = 16f

    /** The round buttons' soft shadow on the light theme; on the dark one a shadow would not show. */
    private const val ROUND_ELEVATION_DP = 1f

    /** The dimming behind an open options sheet: black at 35%. */
    private const val SCRIM = 0x59000000

    /** [options] builds the popup's content; the function it is given closes the popup. */
    fun build(
        ui: Ui,
        title: CharSequence,
        canvas: View,
        onBack: () -> Unit,
        backLabel: CharSequence? = null,
        /** Handed a function that opens the options sheet, for a caller with another way to ask for it. */
        onOpener: ((open: () -> Unit) -> Unit)? = null,
        /** Handed a function that changes the title drawn between the round buttons, for a title that follows what is edited. */
        onRetitle: (((CharSequence) -> Unit) -> Unit)? = null,
        options: (close: () -> Unit) -> View,
    ): FrameLayout {
        val root = FrameLayout(ui.context).apply { setBackgroundColor(ui.palette.background) }
        root.addView(canvas, fill())

        var popup: PopupWindow? = null
        val scrim = View(ui.context).apply { setBackgroundColor(SCRIM) }
        lateinit var open: Toggle
        open =
            optionsButton(ui, round = backLabel == null) {
                popup?.dismiss() ?: run {
                    val window = PopupWindow(ui.context)
                    val content = options { window.dismiss() }
                    val panel =
                        LinearLayout(ui.context).apply {
                            orientation = LinearLayout.VERTICAL
                            setPadding(
                                ui.dp(POPUP_LEFT_DP),
                                ui.dp(POPUP_TOP_PAD_DP),
                                ui.dp(POPUP_RIGHT_DP),
                                ui.dp(Space.M),
                            )
                            addView(content)
                        }
                    window.contentView = ScrollView(ui.context).apply { addView(panel) }
                    window.width = ui.dp(POPUP_WIDTH_DP)
                    // Sideways the room under the button can be shorter than the options; the sheet then stops
                    // at the screen's edge and scrolls, so Reset and Done are never cut off.
                    val offset = ui.dp(POPUP_GAP_DP) - slack(ui)
                    panel.measure(
                        View.MeasureSpec.makeMeasureSpec(window.width, View.MeasureSpec.EXACTLY),
                        View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED),
                    )
                    window.height = minOf(panel.measuredHeight, window.getMaxAvailableHeight(open.view, offset))
                    window.isFocusable = true
                    window.isOutsideTouchable = true
                    window.setBackgroundDrawable(ui.rounded(ui.palette.card, Space.PANEL_RADIUS))
                    window.elevation = ui.dp(POPUP_ELEVATION_DP).toFloat()
                    window.setOnDismissListener {
                        popup = null
                        root.removeView(scrim)
                        open.paint(false)
                    }
                    popup = window
                    // Under whatever holds the options button, so the button that closes the sheet stays lit.
                    root.addView(scrim, root.indexOfChild(open.holder), fill())
                    open.paint(true)
                    // Measured from the drawn button, not from the taller target round it.
                    window.showAsDropDown(open.view, 0, offset, Gravity.END)
                }
            }
        onOpener?.invoke { open.view.performClick() }
        // A rotation or a theme change rebuilds the activity under an open sheet. It goes with the page it
        // belongs to, rather than outliving it and leaking its window.
        root.addOnAttachStateChangeListener(
            object : View.OnAttachStateChangeListener {
                override fun onViewAttachedToWindow(v: View) = Unit

                override fun onViewDetachedFromWindow(v: View) {
                    popup?.dismiss()
                }
            },
        )

        if (backLabel != null) {
            val bar = ui.bar(title, onBack, open.view, backLabel = backLabel)
            open.holder = bar
            root.addView(
                bar,
                FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    Gravity.TOP,
                ),
            )
        } else {
            addBack(root, ui, onBack)
            val label =
                ui.text(title, Type.VALUE, ui.palette.dim, face = Type.bold).apply {
                    isAccessibilityHeading = true
                }
            onRetitle?.invoke { label.text = it }
            root.addView(
                label,
                FrameLayout
                    .LayoutParams(
                        ViewGroup.LayoutParams.WRAP_CONTENT,
                        ViewGroup.LayoutParams.WRAP_CONTENT,
                        Gravity.TOP or Gravity.CENTER_HORIZONTAL,
                    ).apply {
                        topMargin = ui.dp(TITLE_TOP_DP)
                    },
            )
            root.addView(open.view, corner(ui, Gravity.TOP or Gravity.END))
        }

        // Keep the floating buttons clear of a cutout or any system bar that is showing.
        ui.padToBars(root)
        return root
    }

    /** The round back button in a frame's top-left corner, the same on every full-screen surface. */
    fun addBack(
        root: FrameLayout,
        ui: Ui,
        onBack: () -> Unit,
    ) {
        root.addView(
            round(ui, Glyph.Shape.CHEVRON_LEFT, ui.string(R.string.back), onBack),
            corner(ui, Gravity.TOP or Gravity.START),
        )
    }

    /**
     * The options button and how it shows that its sheet is open. [holder] is the view on the frame that
     * carries it: the button itself in a corner, or the nav bar it sits in.
     */
    private class Toggle(
        val view: View,
        var holder: View,
        val paint: (open: Boolean) -> Unit,
    )

    /**
     * The options button. Round and card-filled in a corner, where it fills with the accent while its sheet
     * is open; in a nav bar a plain accent icon, which gains a tinted disc instead. [Glyph] draws in one
     * colour for good, so the round one stacks an accent icon and an on-accent one and shows whichever fits.
     */
    private fun optionsButton(
        ui: Ui,
        round: Boolean,
        onTap: () -> Unit,
    ): Toggle {
        val label = ui.string(R.string.editor_options)
        if (!round) {
            val icon = ui.icon(Glyph.Shape.OPTIONS, label, onTap)
            val tint = disc(ui, ui.palette.accentSoft)
            return Toggle(icon, icon) { on -> icon.background = if (on) tint else null }
        }
        val idle = Glyph(ui.context, Glyph.Shape.OPTIONS, ui.palette.accent)
        val lit = Glyph(ui.context, Glyph.Shape.OPTIONS, ui.palette.onAccent)
        val button = holder(ui, label, onTap)
        button.addView(idle, fill())
        button.addView(lit, fill())
        val card = button.background
        val accent = disc(ui, ui.palette.accent)

        fun show(on: Boolean) {
            idle.visibility = if (on) View.GONE else View.VISIBLE
            lit.visibility = if (on) View.VISIBLE else View.GONE
            button.background = if (on) accent else card
        }
        show(false)
        return Toggle(button, button, ::show)
    }

    /** A round icon button on the canvas, card-filled so the grid does not show through. */
    private fun round(
        ui: Ui,
        icon: Glyph.Shape,
        label: CharSequence,
        onTap: () -> Unit,
    ): View = holder(ui, label, onTap).apply { addView(Glyph(ui.context, icon, ui.palette.accent), fill()) }

    /** The round button's body, named for screen readers and long-press, with nothing drawn in it yet. */
    private fun holder(
        ui: Ui,
        label: CharSequence,
        onTap: () -> Unit,
    ): FrameLayout =
        FrameLayout(ui.context).apply {
            background = disc(ui, ui.palette.card)
            if (!ui.palette.dark) elevation = ui.dp(ROUND_ELEVATION_DP).toFloat()
            contentDescription = label
            tooltipText = label
            ui.tappable(this, ROUND_DP / 2, onTap)
            // The press and the focus ring land on the drawn circle.
            val room = slack(ui)
            foreground = InsetDrawable(foreground, room, room, room, room)
        }

    private fun fill() =
        FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)

    /** The clear room on each side between a round button's drawn circle and its touch target. */
    private fun slack(ui: Ui) = (ui.dp(Space.TOUCH) - ui.dp(ROUND_DP)) / 2

    /** A filled circle the drawn size of a round button, centred in its touch target. */
    private fun disc(
        ui: Ui,
        color: Int,
    ): InsetDrawable {
        val room = slack(ui)
        val oval =
            GradientDrawable().apply {
                shape = GradientDrawable.OVAL
                setColor(color)
            }
        return InsetDrawable(oval, room, room, room, room)
    }

    /** A round button's touch target in a corner, placed so the circle drawn in it sits where the design has it. */
    private fun corner(
        ui: Ui,
        gravity: Int,
    ) = FrameLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH), gravity).apply {
        topMargin = ui.dp(TOP_DP) - slack(ui)
        marginStart = ui.dp(EDGE_DP) - slack(ui)
        marginEnd = ui.dp(EDGE_DP) - slack(ui)
    }

    /** A slider for [sizePanel]: [valueOf] words each step beside the heading as it moves. */
    class Slider(
        val steps: Int,
        val progress: Int,
        val valueOf: (Int) -> CharSequence,
        val onChange: (Int) -> Unit,
    )

    /**
     * The options panel's content: a header of [title] and its value, then a round reset icon at the far end;
     * the [slider] under it; one filled Done at the foot. The first tap on the icon turns it red and shows the
     * hint, a second within [CONFIRM_MS] calls [onReset], otherwise it goes back. Without a [slider] there is
     * no heading, only the icon.
     */
    fun sizePanel(
        ui: Ui,
        title: CharSequence?,
        slider: Slider?,
        onReset: () -> Unit,
        onDone: () -> Unit,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            addView(resetRow(ui, title, slider, onReset))
            addView(doneButton(ui, onDone))
        }

    /** The Done at the foot of an options panel: filled, 36 dp tall. */
    fun doneButton(
        ui: Ui,
        onDone: () -> Unit,
    ): View =
        ui.text(ui.string(R.string.editor_done), Type.VALUE, ui.palette.onAccent, face = Type.bold).apply {
            gravity = Gravity.CENTER
            background = ui.rounded(ui.palette.accent, Space.TILE_RADIUS)
            ui.tappable(this, Space.TILE_RADIUS, onDone)
            layoutParams =
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(DONE_DP)).apply {
                    topMargin = ui.dp(Space.S)
                }
        }

    /**
     * The top of an options panel: [title] and its value, the reset icon at the far end and its hint under it,
     * then the [slider]. [label] names the icon and [confirm] is the hint, for a reset that is not positions.
     */
    fun resetRow(
        ui: Ui,
        title: CharSequence?,
        slider: Slider?,
        onReset: () -> Unit,
        label: String = ui.string(R.string.media_layout_reset),
        confirm: String = ui.string(R.string.editor_reset_confirm),
    ): View {
        val value = ui.text(slider?.let { it.valueOf(it.progress) } ?: "", Type.SMALL, ui.palette.dim)
        val room = ui.dp(Space.TOUCH) - ui.dp(RESET_DP)
        val hint =
            ui.text(confirm, Type.MICRO, ui.palette.danger).apply {
                maxLines = 1
                gravity = Gravity.END
                visibility = View.INVISIBLE
                setAutoSizeTextTypeUniformWithConfiguration(
                    HINT_MIN_SP,
                    Type.MICRO.toInt(),
                    1,
                    TypedValue.COMPLEX_UNIT_SP,
                )
            }
        val reset = resetIcon(ui, hint, label, onReset)
        // The 48 dp target overlaps the panel's padding by half of what it has over the circle, so the
        // circle stays where the design puts it and the row grows no taller than the target.
        val edge = -room / 2
        val header =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                if (title != null) {
                    addView(ui.text(title, Type.VALUE, ui.palette.ink, face = Type.bold))
                    addView(value, LinearLayout.LayoutParams(WRAP, WRAP).apply { marginStart = ui.dp(Space.S) })
                }
                addView(View(ui.context), LinearLayout.LayoutParams(0, 0, 1f))
                addView(reset, LinearLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH)))
            }
        return LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            addView(
                header,
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, WRAP).apply {
                    topMargin = edge
                    marginEnd = edge
                },
            )
            // Full width, so the hint shrinks to fit rather than wrapping.
            addView(
                hint,
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, WRAP).apply { topMargin = edge },
            )
            if (slider != null) {
                val track =
                    ui.ruler(slider.steps, slider.progress) { step ->
                        value.text = slider.valueOf(step)
                        slider.onChange(step)
                    }
                addView(track)
            }
        }
    }

    /**
     * The round reset button: a 36 dp circle centred in a 48 dp touch target; [hint] is shown while it waits
     * for its second tap.
     */
    private fun resetIcon(
        ui: Ui,
        hint: View,
        label: String,
        onReset: () -> Unit,
    ): View {
        val idle = Glyph(ui.context, Glyph.Shape.REFRESH, ui.palette.accent)
        val armed = Glyph(ui.context, Glyph.Shape.REFRESH, ui.palette.danger).apply { visibility = View.GONE }
        val guard = ConfirmTap()
        val button = FrameLayout(ui.context)
        val revert =
            Runnable {
                if (guard.armed) guard.tap()
                idle.visibility = View.VISIBLE
                armed.visibility = View.GONE
                hint.visibility = View.INVISIBLE
            }
        val icon = ui.dp(RESET_ICON_DP)
        val centre = FrameLayout.LayoutParams(icon, icon, Gravity.CENTER)
        return button.apply {
            val gap = (ui.dp(Space.TOUCH) - ui.dp(RESET_DP)) / 2
            background = InsetDrawable(ui.rounded(ui.palette.accentSoft, RESET_DP / 2), gap, gap, gap, gap)
            contentDescription = label
            tooltipText = label
            addView(idle, centre)
            addView(armed, FrameLayout.LayoutParams(icon, icon, Gravity.CENTER))
            ui.tappable(this, RESET_DP / 2) {
                removeCallbacks(revert)
                if (guard.tap()) {
                    revert.run()
                    onReset()
                } else {
                    idle.visibility = View.GONE
                    armed.visibility = View.VISIBLE
                    hint.visibility = View.VISIBLE
                    postDelayed(revert, CONFIRM_MS)
                }
            }
            // The press and the focus ring land on the drawn circle, not the whole target.
            foreground = InsetDrawable(foreground, gap, gap, gap, gap)
        }
    }

    private const val WRAP = ViewGroup.LayoutParams.WRAP_CONTENT
    private const val RESET_DP = 36f
    private const val RESET_ICON_DP = 18f
    private const val DONE_DP = 36f
    private const val HINT_MIN_SP = 9

    /** How long the reset icon waits, red, for its second tap. */
    private const val CONFIRM_MS = 3000L
}
