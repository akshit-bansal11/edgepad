package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.Gesture
import me.akshitbansal.edgepad.surface.GestureAction

/** Every assignable trackpad gesture with what it does; tap a row to pick another action. */
object GestureScreen {
    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View =
        ui.page(ui.bar(ui.string(R.string.gestures_title), onBack)) {
            body(ui.string(R.string.gestures_hint), Space.M)
            var fingers = 0
            for (gesture in Gesture.entries) {
                if (gesture.fingers != fingers) {
                    fingers = gesture.fingers
                    section(ui.string(R.string.gestures_fingers, fingers))
                }
                hairline()
                add(row(ui, settings, gesture))
            }
            hairline()
        }

    private fun row(
        ui: Ui,
        settings: Settings,
        gesture: Gesture,
    ): View {
        val current =
            ui.mono(
                ui.string(settings.gesture(gesture).nameRes),
                Type.SMALL,
                ui.palette.dim,
                Type.TRACKING_ROW,
            )
        val chevron = Glyph(ui.context, Glyph.Shape.CHEVRON_RIGHT, ui.palette.dim)
        val end =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(current)
                addView(chevron, LinearLayout.LayoutParams(ui.dp(Space.XL), ui.dp(Space.XL)))
            }
        val row = ui.row(ui.text(ui.string(gesture.nameRes), Type.BODY, ui.palette.ink), end)
        ui.tappable(row) { pick(ui, settings, gesture, current) }
        return row
    }

    private fun pick(
        ui: Ui,
        settings: Settings,
        gesture: Gesture,
        current: TextView,
    ) {
        val actions = GestureAction.entries
        val names = actions.map { ui.string(it.nameRes) }.toTypedArray()
        AlertDialog
            .Builder(ui.context)
            .setTitle(ui.string(gesture.nameRes))
            .setSingleChoiceItems(names, actions.indexOf(settings.gesture(gesture))) { dialog, which ->
                settings.setGesture(gesture, actions[which])
                current.text = names[which]
                dialog.dismiss()
            }.show()
    }
}
