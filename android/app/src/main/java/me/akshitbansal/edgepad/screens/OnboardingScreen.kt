package me.akshitbansal.edgepad.screens

import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/** First run: what the phone becomes, and what each number of fingers does. */
object OnboardingScreen {
    private const val STEPS = 3
    private const val STEP_WIDTH_DP = 18f
    private const val STEP_HEIGHT_DP = 2f
    private const val STEP_GAP_DP = 6f
    private const val GLYPH_WIDTH_DP = 38f
    private const val GLYPH_HEIGHT_DP = 30f
    private const val TITLE_GAP_DP = 46f
    private const val LIST_GAP_DP = 40f
    private const val ITEM_TRACKING = 0.14f
    private const val ITEM_LEADING = 1.4f

    fun build(
        ui: Ui,
        onDone: () -> Unit,
    ): View =
        ui.page {
            add(steps(ui))
            headline(ui.string(R.string.onboarding_title), Type.DISPLAY, TITLE_GAP_DP)
            body(ui.string(R.string.onboarding_intro), Space.L)
            item(dots(ui, 1), R.string.gesture_one_title, R.string.gesture_one, LIST_GAP_DP)
            item(dots(ui, 2), R.string.gesture_two_title, R.string.gesture_two)
            item(dots(ui, 3), R.string.gesture_three_title, R.string.gesture_three)
            item(dots(ui, 4), R.string.gesture_four_title, R.string.gesture_four)
            item(
                Glyph(ui.context, Glyph.Shape.CORNER, ui.palette.ink),
                R.string.gesture_dials_title,
                R.string.gesture_dials,
            )
            hairline()
            grow()
            add(ui.button(ui.string(R.string.onboarding_continue), Ui.Style.OUTLINED, onDone), Space.XXL)
        }

    private fun dots(
        ui: Ui,
        count: Int,
    ) = Glyph(ui.context, Glyph.Shape.DOT, ui.palette.ink, count)

    private fun Column.item(
        glyph: Glyph,
        titleRes: Int,
        bodyRes: Int,
        topDp: Float = 0f,
    ) {
        hairline(topDp)
        val words =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(ui.mono(ui.string(titleRes), Type.LABEL, ui.palette.ink, ITEM_TRACKING))
                addView(
                    ui.text(ui.string(bodyRes), Type.CAPTION, ui.palette.dim, Type.plain).apply {
                        setLineSpacing(0f, ITEM_LEADING)
                        setPadding(0, ui.dp(Space.XS), 0, 0)
                    },
                )
            }
        val row =
            LinearLayout(ui.context).apply {
                setPadding(0, ui.dp(Space.L), 0, ui.dp(Space.L))
                addView(glyph, LinearLayout.LayoutParams(ui.dp(GLYPH_WIDTH_DP), ui.dp(GLYPH_HEIGHT_DP)))
                addView(
                    words,
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                        marginStart =
                            ui.dp(Space.L)
                    },
                )
            }
        add(row)
    }

    /** The setup's three steps as short bars, the first lit: this, then finding the laptop, then the controls. */
    private fun steps(ui: Ui): View =
        LinearLayout(ui.context).apply {
            repeat(STEPS) { i ->
                val bar = View(ui.context).apply { setBackgroundColor(if (i == 0) ui.palette.ink else ui.palette.line) }
                addView(
                    bar,
                    LinearLayout.LayoutParams(ui.dp(STEP_WIDTH_DP), ui.dp(STEP_HEIGHT_DP)).apply {
                        marginEnd =
                            ui.dp(STEP_GAP_DP)
                    },
                )
            }
        }
}
