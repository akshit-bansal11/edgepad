package me.akshitbansal.edgepad.screens

import android.view.View
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.Gesture
import me.akshitbansal.edgepad.surface.GestureAction

/**
 * Every assignable trackpad gesture, grouped by fingers, with what it does. Tapping one opens the list of
 * actions under it, the current one marked; picking one closes it again.
 */
object GestureScreen {
    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View {
        val groups = Gesture.entries.groupBy { it.fingers }
        val blocks =
            groups.keys.associateWith {
                LinearLayout(
                    ui.context,
                ).apply { orientation = LinearLayout.VERTICAL }
            }
        val actions = GestureAction.entries
        var open: Gesture? = null

        fun render() {
            for ((fingers, block) in blocks) {
                block.removeAllViews()
                block.addView(ui.section(ui.string(R.string.gestures_fingers, fingers)))
                for (gesture in groups.getValue(fingers)) {
                    val expanded = gesture == open
                    block.addView(
                        row(ui, settings, gesture, expanded) {
                            open = if (expanded) null else gesture
                            render()
                        },
                    )
                    block.addView(ui.hairline(), LinearLayout.LayoutParams.MATCH_PARENT, ui.dp(Space.HAIR))
                    if (expanded) {
                        val list =
                            ui.choices(
                                actions.map { ui.string(it.nameRes) },
                                actions.indexOf(settings.gesture(gesture)),
                            ) { i ->
                                settings.setGesture(gesture, actions[i])
                                open = null
                                render()
                            }
                        list.setPadding(ui.dp(Space.L), 0, 0, 0)
                        block.addView(list)
                    }
                }
            }
        }
        render()
        val (first, rest) = blocks.values.toList().let { it.first() to it.drop(1) }
        return ui.page(ui.bar(ui.string(R.string.gestures_title), onBack)) {
            columns({ add(first) }, { rest.forEach { add(it) } })
            mono(ui.string(R.string.gestures_footnote), topDp = Space.L)
            hairline(Space.L)
            add(ui.toggle(ui.string(R.string.gesture_hints), settings.hints) { settings.hints = it })
            hairline()
        }
    }

    private fun row(
        ui: Ui,
        settings: Settings,
        gesture: Gesture,
        expanded: Boolean,
        onToggle: () -> Unit,
    ): View {
        val action = settings.gesture(gesture)
        val color = if (expanded || action != GestureAction.NOTHING) ui.palette.ink else ui.palette.dim
        val current = ui.mono(ui.string(action.nameRes).uppercase(), Type.MICRO, color, Type.TRACKING_ROW)
        return ui.field(ui.string(gesture.nameRes), current).apply {
            minimumHeight = ui.dp(Space.TOUCH)
            ui.tappable(this, onToggle)
        }
    }
}
