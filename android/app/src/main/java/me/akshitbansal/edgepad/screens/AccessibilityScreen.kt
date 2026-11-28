package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Sounds

/**
 * The single-switch features, together: Sounds, Haptic feedback, Air mouse (with its tuning, which shows while it is
 * on) and Show gesture hints. Each switch edits the value its old page did, under the same stored key.
 */
object AccessibilityScreen {
    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View =
        ui.page(ui.bar("", onBack, backLabel = ui.string(R.string.settings_title))) {
            largeTitle(ui.string(R.string.accessibility_title))
            card {
                add(
                    ui.toggle(ui.string(R.string.accessibility_sounds), settings.sounds) {
                        settings.sounds = it
                        Sounds.on = it
                        if (!it) Sounds.release()
                    },
                )
                hairline()
                add(ui.toggle(ui.string(R.string.haptic_ticks), settings.haptics) { settings.haptics = it })
                hairline()
                add(
                    ui.toggle(ui.string(R.string.gesture_hints), settings.hints) {
                        settings.hints = it
                        if (it) settings.hintsSeen = 0
                    },
                )
            }
            with(GestureScreen) { airMouse(ui, settings) }
        }
}
