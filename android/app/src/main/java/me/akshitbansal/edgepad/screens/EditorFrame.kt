package me.akshitbansal.edgepad.screens

import android.graphics.drawable.GradientDrawable
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
 * fills the screen, and a round back button, a small centred title and a round options button float
 * over it. Options open in a popup, which a tap outside or back closes.
 */
object EditorFrame {
    private const val ROUND_DP = 40f
    private const val EDGE_DP = 8f
    private const val TOP_DP = 6f
    private const val TITLE_TOP_DP = 18f
    private const val TITLE_TRACKING = 0.14f
    private const val POPUP_WIDTH_DP = 232f
    private const val POPUP_GAP_DP = 8f

    /** [options] builds the popup's content; the function it is given closes the popup. */
    fun build(
        ui: Ui,
        title: CharSequence,
        canvas: View,
        onBack: () -> Unit,
        options: (close: () -> Unit) -> View,
    ): View {
        val root = FrameLayout(ui.context).apply { setBackgroundColor(ui.palette.background) }
        root.addView(
            canvas,
            FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT),
        )

        val back = round(ui, Glyph.Shape.CHEVRON_LEFT, ui.string(R.string.back), onBack)
        root.addView(back, corner(ui, Gravity.TOP or Gravity.START))

        val label =
            ui.mono(title, Type.MICRO, ui.palette.dim, TITLE_TRACKING).apply {
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

        var popup: PopupWindow? = null
        lateinit var open: Glyph
        open =
            round(ui, Glyph.Shape.OPTIONS, ui.string(R.string.editor_options)) {
                popup?.dismiss() ?: run {
                    val window = PopupWindow(ui.context)
                    val content = options { window.dismiss() }
                    val panel =
                        LinearLayout(ui.context).apply {
                            orientation = LinearLayout.VERTICAL
                            setPadding(ui.dp(Space.L), ui.dp(Space.M), ui.dp(Space.L), ui.dp(Space.M))
                            addView(content)
                        }
                    window.contentView = panel
                    window.width = ui.dp(POPUP_WIDTH_DP)
                    window.height = ViewGroup.LayoutParams.WRAP_CONTENT
                    window.isFocusable = true
                    window.isOutsideTouchable = true
                    window.setBackgroundDrawable(
                        GradientDrawable().apply {
                            setColor(ui.palette.background)
                            setStroke(ui.dp(Space.HAIR), ui.palette.ink)
                        },
                    )
                    window.setOnDismissListener {
                        popup = null
                        outline(ui, open, on = false)
                    }
                    popup = window
                    outline(ui, open, on = true)
                    window.showAsDropDown(open, 0, ui.dp(POPUP_GAP_DP), Gravity.END)
                }
            }
        outline(ui, open, on = false)
        root.addView(open, corner(ui, Gravity.TOP or Gravity.END))

        // Keep the floating buttons clear of a cutout or any system bar that is showing.
        root.setOnApplyWindowInsetsListener { frame, insets ->
            val bars = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
            frame.setPadding(bars.left, bars.top, bars.right, bars.bottom)
            insets
        }
        return root
    }

    /** A 40 dp round icon button on the canvas, filled with the panel so the grid does not show through. */
    private fun round(
        ui: Ui,
        icon: Glyph.Shape,
        label: CharSequence,
        onTap: () -> Unit,
    ): Glyph =
        ui.icon(icon, label, onTap).apply {
            background =
                GradientDrawable().apply {
                    shape = GradientDrawable.OVAL
                    setColor(ui.palette.background)
                }
        }

    /** The options button's ring: ink while its popup is open, a hairline otherwise. */
    private fun outline(
        ui: Ui,
        button: View,
        on: Boolean,
    ) {
        (button.background as? GradientDrawable)?.setStroke(
            ui.dp(Space.HAIR),
            if (on) ui.palette.ink else ui.palette.line,
        )
    }

    private fun corner(
        ui: Ui,
        gravity: Int,
    ) = FrameLayout.LayoutParams(ui.dp(ROUND_DP), ui.dp(ROUND_DP), gravity).apply {
        topMargin = ui.dp(TOP_DP)
        marginStart = ui.dp(EDGE_DP)
        marginEnd = ui.dp(EDGE_DP)
    }

    /** RESET and DONE, side by side at the foot of a popup. */
    fun resetAndDone(
        ui: Ui,
        onReset: () -> Unit,
        onDone: () -> Unit,
    ): View =
        LinearLayout(ui.context).apply {
            setPadding(0, ui.dp(Space.S), 0, 0)
            addView(
                ui.button(ui.string(R.string.media_layout_reset), Ui.Style.QUIET, onReset),
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
