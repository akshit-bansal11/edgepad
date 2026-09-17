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

/** What each slot of the surface holds: pick a slot, then pick its dial from the list beside or below it. */
object CornersScreen {
    private const val ICON_DP = 18f
    private const val ICON_MARK_DP = 8f
    private const val ICON_INSET_DP = 2f
    private const val ICON_GAP_DP = 14f

    /** Each slot's name, clockwise from the top left: a corner, then the midpoint of the edge after it. */
    private val names =
        listOf(
            R.string.corner_top_left,
            R.string.edge_top,
            R.string.corner_top_right,
            R.string.edge_right,
            R.string.corner_bottom_right,
            R.string.edge_bottom,
            R.string.corner_bottom_left,
            R.string.edge_left,
        )

    /** Where the filled square sits in each slot's icon: in its corner, or against the middle of its edge. */
    private val gravities =
        intArrayOf(
            Gravity.TOP or Gravity.START,
            Gravity.TOP or Gravity.CENTER_HORIZONTAL,
            Gravity.TOP or Gravity.END,
            Gravity.CENTER_VERTICAL or Gravity.END,
            Gravity.BOTTOM or Gravity.END,
            Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL,
            Gravity.BOTTOM or Gravity.START,
            Gravity.CENTER_VERTICAL or Gravity.START,
        )

    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View {
        val slots = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        val assign = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        var picked = 0
        val kinds = listOf<DialKind?>(null) + DialKind.entries

        fun render() {
            slots.removeAllViews()
            assign.removeAllViews()
            for (slot in 0 until Perimeter.SLOTS) {
                slots.addView(
                    row(ui, slot, kindName(ui, settings.slot(slot)), slot == picked) {
                        picked = slot
                        render()
                    },
                )
                slots.addView(ui.hairline(), LinearLayout.LayoutParams.MATCH_PARENT, ui.dp(Space.HAIR))
            }
            val title = ui.string(names[picked]).uppercase()
            assign.addView(ui.section(ui.string(R.string.corner_assign, title)))
            assign.addView(
                ui.choices(kinds.map { kindName(ui, it) }, kinds.indexOf(settings.slot(picked))) { i ->
                    settings.setSlot(picked, kinds[i])
                    render()
                },
            )
        }
        render()
        // Eight rows and the list of kinds: side by side sideways, one after the other upright, where the
        // page scrolls. The picked row stays marked either way, so the list always says what it is assigning.
        return ui.page(ui.bar(ui.string(R.string.corners_title), onBack)) {
            columns({ add(slots) }, { add(assign) })
        }
    }

    private fun row(
        ui: Ui,
        slot: Int,
        value: String,
        picked: Boolean,
        onPick: () -> Unit,
    ): View {
        val start =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(
                    icon(ui, slot, picked),
                    LinearLayout.LayoutParams(ui.dp(ICON_DP), ui.dp(ICON_DP)).apply { marginEnd = ui.dp(ICON_GAP_DP) },
                )
                addView(ui.text(ui.string(names[slot]), Type.BODY, ui.palette.ink))
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

    /** A small square with a filled square where the slot it stands for sits; decoration beside its name. */
    private fun icon(
        ui: Ui,
        slot: Int,
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
                    setLayerGravity(1, gravities[slot])
                    setLayerInset(1, inset, inset, inset, inset)
                }
        }

    private fun kindName(
        ui: Ui,
        kind: DialKind?,
    ): String = if (kind == null) ui.string(R.string.corner_off) else ui.string(kind.nameRes)
}
