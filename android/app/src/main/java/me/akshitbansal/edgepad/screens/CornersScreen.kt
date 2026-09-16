package me.akshitbansal.edgepad.screens

import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.LayerDrawable
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.Perimeter

/** What each corner of the surface holds: pick a corner, then pick its dial from the list beside or below it. */
object CornersScreen {
    private const val ICON_DP = 18f
    private const val ICON_MARK_DP = 8f
    private const val ICON_INSET_DP = 2f
    private const val ICON_GAP_DP = 14f

    private val names =
        listOf(
            R.string.corner_top_left,
            R.string.corner_top_right,
            R.string.corner_bottom_right,
            R.string.corner_bottom_left,
        )

    /** Where the filled square sits in each corner's icon, clockwise from the top left. */
    private val gravities =
        intArrayOf(
            Gravity.TOP or Gravity.START,
            Gravity.TOP or Gravity.END,
            Gravity.BOTTOM or Gravity.END,
            Gravity.BOTTOM or Gravity.START,
        )

    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View {
        val corners = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        val assign = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        var picked = 0
        val kinds = listOf<DialKind?>(null) + DialKind.entries

        fun render() {
            corners.removeAllViews()
            assign.removeAllViews()
            for (corner in 0 until Perimeter.CORNERS) {
                corners.addView(
                    row(ui, corner, kindName(ui, settings.corner(corner)), corner == picked) {
                        picked = corner
                        render()
                    },
                )
                corners.addView(ui.hairline(), LinearLayout.LayoutParams.MATCH_PARENT, ui.dp(Space.HAIR))
            }
            val title = ui.string(names[picked]).uppercase()
            assign.addView(ui.section(ui.string(R.string.corner_assign, title)))
            assign.addView(
                ui.choices(kinds.map { kindName(ui, it) }, kinds.indexOf(settings.corner(picked))) { i ->
                    settings.setCorner(picked, kinds[i])
                    render()
                },
            )
        }
        render()
        return ui.page(ui.bar(ui.string(R.string.corners_title), onBack)) {
            columns({ add(corners) }, { add(assign) })
        }
    }

    private fun row(
        ui: Ui,
        corner: Int,
        value: String,
        picked: Boolean,
        onPick: () -> Unit,
    ): View {
        val start =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(
                    icon(ui, corner, picked),
                    LinearLayout.LayoutParams(ui.dp(ICON_DP), ui.dp(ICON_DP)).apply { marginEnd = ui.dp(ICON_GAP_DP) },
                )
                addView(ui.text(ui.string(names[corner]), Type.BODY, ui.palette.ink))
            }
        val end =
            ui.mono(
                value.uppercase(),
                Type.MICRO,
                if (picked) ui.palette.ink else ui.palette.dim,
                Type.TRACKING_ROW,
            )
        return ui.row(start, end).apply {
            isSelected = picked
            if (picked) stateDescription = ui.string(R.string.chosen)
            ui.tappable(this, onPick)
        }
    }

    /** A small square with a filled square in the corner it stands for; decoration beside the corner's name. */
    private fun icon(
        ui: Ui,
        corner: Int,
        picked: Boolean,
    ): View =
        View(ui.context).apply {
            importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            val box =
                GradientDrawable().apply {
                    setStroke(
                        ui.dp(Space.HAIR),
                        if (picked) ui.palette.ink else ui.palette.dim,
                    )
                }
            val mark = GradientDrawable().apply { setColor(ui.palette.ink) }
            background =
                LayerDrawable(arrayOf(box, mark)).apply {
                    val inset = ui.dp(ICON_INSET_DP)
                    setLayerSize(1, ui.dp(ICON_MARK_DP), ui.dp(ICON_MARK_DP))
                    setLayerGravity(1, gravities[corner])
                    setLayerInset(1, inset, inset, inset, inset)
                }
        }

    private fun kindName(
        ui: Ui,
        kind: DialKind?,
    ): String = if (kind == null) ui.string(R.string.corner_off) else ui.string(kind.nameRes)
}
