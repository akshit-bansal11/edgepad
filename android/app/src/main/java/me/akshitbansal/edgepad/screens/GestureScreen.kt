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
 * How the trackpad feels, then every assignable gesture grouped by fingers. Tapping a gesture opens the
 * list of actions under it, the current one marked; picking one closes it again. One and two fingers are
 * not in the list: they are fixed in [me.akshitbansal.edgepad.surface.TrackpadRecognizer] and the footnote
 * says what they do, so the screen does not read as though they were simply missing.
 */
object GestureScreen {
    private val speedRange = StepRange(Settings.MIN_SPEED, Settings.MAX_SPEED, 0.1f)

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
            columns(
                {
                    section(ui.string(R.string.trackpad_feel))
                    add(
                        speed(ui, ui.string(R.string.pointer_speed), settings.pointerSpeed) {
                            settings.pointerSpeed = it
                        },
                    )
                    hairline()
                    add(
                        speed(ui, ui.string(R.string.scroll_speed), settings.scrollSpeed) {
                            settings.scrollSpeed = it
                        },
                    )
                    hairline()
                    // Which way a two-finger drag moves the page is a scrolling setting, so it sits with the
                    // scroll speed rather than alone on the hub, where it was the only switch among links.
                    add(
                        ui.toggle(ui.string(R.string.natural_scrolling), settings.naturalScroll) {
                            settings.naturalScroll = it
                        },
                    )
                    hairline()
                    add(first)
                },
                { rest.forEach { add(it) } },
            )
            mono(ui.string(R.string.gestures_fixed), topDp = Space.L)
            mono(ui.string(R.string.gestures_footnote), topDp = Space.S)
            hairline(Space.L)
            add(ui.toggle(ui.string(R.string.gesture_hints), settings.hints) { settings.hints = it })
            hairline()
        }
    }

    /** One ×-multiplier slider over the shared speed range. */
    private fun speed(
        ui: Ui,
        label: String,
        current: Float,
        onChange: (Float) -> Unit,
    ): View =
        ui.slider(
            label,
            speedRange.steps,
            speedRange.stepOf(current),
            { step -> ui.string(R.string.multiplier_value, speedRange.valueAt(step)) },
        ) { step -> onChange(speedRange.valueAt(step)) }

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
