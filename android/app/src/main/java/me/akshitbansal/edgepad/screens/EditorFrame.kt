package me.akshitbansal.edgepad.screens

import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.InsetDrawable
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.PopupWindow
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
    private const val POPUP_WIDTH_DP = 260f
    private const val POPUP_GAP_DP = 8f
    private const val POPUP_TOP_DP = 4f
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
        options: (close: () -> Unit) -> View,
    ): View {
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
                            setPadding(ui.dp(Space.L), ui.dp(POPUP_TOP_DP), ui.dp(Space.L), ui.dp(Space.M))
                            addView(content)
                        }
                    window.contentView = panel
                    window.width = ui.dp(POPUP_WIDTH_DP)
                    window.height = ViewGroup.LayoutParams.WRAP_CONTENT
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
                    window.showAsDropDown(open.view, 0, ui.dp(POPUP_GAP_DP) - slack(ui), Gravity.END)
                }
            }
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
            val back = round(ui, Glyph.Shape.CHEVRON_LEFT, ui.string(R.string.back), onBack)
            root.addView(back, corner(ui, Gravity.TOP or Gravity.START))
            val label =
                ui.text(title, Type.VALUE, ui.palette.dim, face = Type.bold).apply {
                    isAccessibilityHeading = true
                }
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
        root.setOnApplyWindowInsetsListener { frame, insets ->
            val bars = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
            frame.setPadding(bars.left, bars.top, bars.right, bars.bottom)
            insets
        }
        return root
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

    /** Reset and Done, side by side at the foot of a popup: Reset in [resetStyle], Done the filled one. */
    fun resetAndDone(
        ui: Ui,
        onReset: () -> Unit,
        onDone: () -> Unit,
        resetStyle: Ui.Style = Ui.Style.QUIET,
    ): View =
        LinearLayout(ui.context).apply {
            setPadding(0, ui.dp(Space.S), 0, 0)
            addView(
                ui.button(ui.string(R.string.media_layout_reset), resetStyle, onReset),
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
            )
            addView(
                ui.button(ui.string(R.string.editor_done), Ui.Style.FILLED, onDone),
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                    marginStart =
                        ui.dp(Space.S)
                },
            )
        }
}
