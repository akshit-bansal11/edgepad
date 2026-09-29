package me.akshitbansal.edgepad.screens

import android.graphics.drawable.Drawable
import android.graphics.drawable.LayerDrawable
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.Perimeter

/**
 * What each corner of the surface holds: pick a corner, then pick its dial from the list beside or below it.
 *
 * Both lists change in place. Until 3.2 every pick rebuilt both, so TalkBack lost its place on each one and
 * started again from the top of the page: picking a corner meant finding the list again, and picking a dial
 * meant finding the corner again.
 */
object CornersScreen {
    private const val ICON_DP = 20f
    private const val ICON_RADIUS_DP = 6f
    private const val ICON_MARK_DP = 8f
    private const val ICON_MARK_RADIUS_DP = 2f
    private const val ICON_INSET_DP = 2f
    private const val ICON_GAP_DP = 14f

    private val names =
        listOf(
            R.string.corner_top_left,
            R.string.corner_top_right,
            R.string.corner_bottom_right,
            R.string.corner_bottom_left,
        )

    /** Where the small mark sits in each corner's icon, clockwise from the top left. */
    private val gravities =
        intArrayOf(
            Gravity.TOP or Gravity.START,
            Gravity.TOP or Gravity.END,
            Gravity.BOTTOM or Gravity.END,
            Gravity.BOTTOM or Gravity.START,
        )

    /** One corner's row, and the three parts of it that change when another corner is picked. */
    private class Row(
        val view: View,
        val icon: View,
        val name: TextView,
        val value: TextView,
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
        val rows = ArrayList<Row>(Perimeter.CORNERS)

        // The dial list is the one part rebuilt, and only when the corner it is for changes: it is a list
        // for another corner then, with another heading. A dial picked in it moves its own check.
        fun fillAssign() {
            assign.removeAllViews()
            Column(ui, assign).apply {
                section(ui.string(R.string.corner_assign, ui.string(names[picked]).lowercase()))
                card {
                    add(
                        ui.choices(kinds.map { kindName(ui, it) }, kinds.indexOf(settings.corner(picked))) { i ->
                            settings.setCorner(picked, kinds[i])
                            rows[picked].value.text = kindName(ui, kinds[i])
                        },
                    )
                }
            }
        }

        for (corner in 0 until Perimeter.CORNERS) {
            if (corner > 0) corners.addView(ui.hairline(), LinearLayout.LayoutParams.MATCH_PARENT, ui.dp(Space.HAIR))
            val row =
                row(ui, corner, kindName(ui, settings.corner(corner))) {
                    if (picked != corner) {
                        picked = corner
                        rows.forEachIndexed { i, each -> paint(ui, i, each, i == picked) }
                        fillAssign()
                    }
                }
            rows += row
            corners.addView(row.view)
            paint(ui, corner, row, corner == picked)
        }
        fillAssign()
        return ui.page(
            ui.bar(ui.string(R.string.corners_title), onBack, backLabel = ui.string(R.string.settings_title)),
        ) {
            columns(
                {
                    section(ui.string(R.string.corners_pick))
                    card { add(corners) }
                },
                { add(assign) },
            )
        }
    }

    private fun row(
        ui: Ui,
        corner: Int,
        value: String,
        onPick: () -> Unit,
    ): Row {
        val icon = View(ui.context).apply { importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO }
        val name = ui.text(ui.string(names[corner]), Type.BODY, ui.palette.ink)
        val start =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(
                    icon,
                    LinearLayout.LayoutParams(ui.dp(ICON_DP), ui.dp(ICON_DP)).apply { marginEnd = ui.dp(ICON_GAP_DP) },
                )
                addView(name)
            }
        val end = ui.secondary(value, Type.VALUE)
        val view = ui.row(start, end).apply { ui.tappable(this, onPick) }
        return Row(view, icon, name, end)
    }

    /** Shows [row] as the picked corner or not: a bold name, an accent value and mark, and TalkBack's "chosen". */
    private fun paint(
        ui: Ui,
        corner: Int,
        row: Row,
        picked: Boolean,
    ) {
        row.view.isSelected = picked
        row.view.stateDescription = if (picked) ui.string(R.string.chosen) else null
        row.name.typeface = if (picked) Type.bold else Type.face
        row.value.setTextColor(if (picked) ui.palette.accent else ui.palette.dim)
        row.icon.background = icon(ui, corner, picked)
    }

    /** A small rounded tile with a mark in the corner it stands for; decoration beside the corner's name. */
    private fun icon(
        ui: Ui,
        corner: Int,
        picked: Boolean,
    ): Drawable {
        val tile = ui.rounded(ui.palette.faint, ICON_RADIUS_DP)
        val mark = ui.rounded(if (picked) ui.palette.accent else ui.palette.dim, ICON_MARK_RADIUS_DP)
        return LayerDrawable(arrayOf(tile, mark)).apply {
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
