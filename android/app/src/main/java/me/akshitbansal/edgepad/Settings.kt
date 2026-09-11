package me.akshitbansal.edgepad

import android.content.Context
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.Edge
import me.akshitbansal.edgepad.surface.Placement

/** Everything the app remembers between launches. The theme is the system's own per-app night mode. */
class Settings(
    context: Context,
) {
    private val prefs = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)

    /** The Bluetooth address of the laptop to reconnect to, once one has been chosen. */
    var laptop: String?
        get() = prefs.getString(KEY_LAPTOP, null)
        set(value) = prefs.edit().putString(KEY_LAPTOP, value).apply()

    var onboarded: Boolean
        get() = prefs.getBoolean(KEY_ONBOARDED, false)
        set(value) = prefs.edit().putBoolean(KEY_ONBOARDED, value).apply()

    fun placement(kind: DialKind): Placement {
        val edgeName = prefs.getString("$KEY_DIAL${kind.name}.edge", null)
        val edge = Edge.entries.firstOrNull { it.name == edgeName } ?: kind.defaultPlacement.edge
        val along = prefs.getFloat("$KEY_DIAL${kind.name}.along", kind.defaultPlacement.along)
        return Placement(edge, along.coerceIn(0f, 1f))
    }

    fun place(
        kind: DialKind,
        placement: Placement,
    ) {
        prefs
            .edit()
            .putString("$KEY_DIAL${kind.name}.edge", placement.edge.name)
            .putFloat("$KEY_DIAL${kind.name}.along", placement.along)
            .apply()
    }

    private companion object {
        const val NAME = "edgepad"
        const val KEY_LAPTOP = "laptop"
        const val KEY_ONBOARDED = "onboarded"
        const val KEY_DIAL = "dial."
    }
}
