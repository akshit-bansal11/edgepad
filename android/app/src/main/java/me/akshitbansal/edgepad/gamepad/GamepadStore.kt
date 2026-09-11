package me.akshitbansal.edgepad.gamepad

import android.content.Context

/** Persists the gamepad layout the player has arranged, in the encoded plain-text form. */
class GamepadStore(
    context: Context,
) {
    private val prefs = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)

    var current: GamepadLayout
        get() {
            val text = prefs.getString(KEY_LAYOUT, null) ?: return GamepadLayout.presets.first()
            return GamepadLayout.decode(text) ?: GamepadLayout.presets.first()
        }
        set(value) = save(value)

    fun save(layout: GamepadLayout) {
        prefs.edit().putString(KEY_LAYOUT, layout.encode()).apply()
    }

    /** Back to the preset sharing the current layout's name, or the first preset if none matches. */
    fun reset() {
        val name = current.name
        val preset = GamepadLayout.presets.firstOrNull { it.name == name } ?: GamepadLayout.presets.first()
        save(preset)
    }

    fun choosePreset(name: String) {
        val preset = GamepadLayout.presets.firstOrNull { it.name == name } ?: return
        save(preset)
    }

    private companion object {
        const val NAME = "edgepad.gamepad"
        const val KEY_LAYOUT = "layout"
    }
}
