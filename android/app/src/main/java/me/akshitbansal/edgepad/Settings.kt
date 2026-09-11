package me.akshitbansal.edgepad

import android.content.Context
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.Gesture
import me.akshitbansal.edgepad.surface.GestureAction
import me.akshitbansal.edgepad.surface.MediaPiece
import kotlin.properties.ReadWriteProperty
import kotlin.reflect.KProperty

/** Everything the app remembers between launches. The theme is the system's own per-app night mode. */
class Settings(
    context: Context,
) {
    private val prefs = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)

    /** The Bluetooth address of the laptop to reconnect to, once one has been chosen. */
    var laptop: String?
        get() = prefs.getString(KEY_LAPTOP, null)
        set(value) = prefs.edit().putString(KEY_LAPTOP, value).apply()

    var onboarded by flag("onboarded", false)

    /** Connect to the remembered laptop when the app opens, and retry after a dropped link. */
    var reconnect by flag("reconnect", true)

    var haptics by flag("haptics", true)

    /** Round volume, brightness and mic to a multiple of 5 when the finger lifts. */
    var snap by flag("snap", false)

    /** Content follows the fingers, as on Windows' own touchpads; off scrolls the other way. */
    var naturalScroll by flag("naturalScroll", true)

    var hints by flag("hints", false)

    /** How much a dial moves per dp of slide, as a multiple of the base rate. */
    var sensitivity: Float
        get() = prefs.getFloat(KEY_SENSITIVITY, DEFAULT_SENSITIVITY).coerceIn(MIN_SENSITIVITY, MAX_SENSITIVITY)
        set(value) = prefs.edit().putFloat(KEY_SENSITIVITY, value.coerceIn(MIN_SENSITIVITY, MAX_SENSITIVITY)).apply()

    /** The dial in [corner] (0 top-left, clockwise), or null for none. */
    fun corner(corner: Int): DialKind? {
        val name = prefs.getString("corner.$corner", null)
        if (name == NONE) return null
        return DialKind.entries.firstOrNull { it.name == name }
            ?: DialKind.entries.firstOrNull { it.defaultCorner == corner }
    }

    fun setCorner(
        corner: Int,
        kind: DialKind?,
    ) {
        prefs.edit().putString("corner.$corner", kind?.name ?: NONE).apply()
    }

    fun gesture(gesture: Gesture): GestureAction {
        val name = prefs.getString("gesture.${gesture.name}", null) ?: return gesture.default
        return GestureAction.entries.firstOrNull { it.name == name } ?: gesture.default
    }

    fun setGesture(
        gesture: Gesture,
        action: GestureAction,
    ) {
        prefs.edit().putString("gesture.${gesture.name}", action.name).apply()
    }

    /** Where a media piece sits, as fractions of the surface's width and height. */
    fun piece(piece: MediaPiece): Pair<Float, Float> =
        prefs.getFloat("piece.${piece.name}.x", piece.defaultX).coerceIn(0f, 1f) to
            prefs.getFloat("piece.${piece.name}.y", piece.defaultY).coerceIn(0f, 1f)

    fun setPiece(
        piece: MediaPiece,
        x: Float,
        y: Float,
    ) {
        prefs
            .edit()
            .putFloat(
                "piece.${piece.name}.x",
                x.coerceIn(0f, 1f),
            ).putFloat("piece.${piece.name}.y", y.coerceIn(0f, 1f))
            .apply()
    }

    private fun flag(
        key: String,
        default: Boolean,
    ) = object : ReadWriteProperty<Any?, Boolean> {
        override fun getValue(
            thisRef: Any?,
            property: KProperty<*>,
        ): Boolean = prefs.getBoolean(key, default)

        override fun setValue(
            thisRef: Any?,
            property: KProperty<*>,
            value: Boolean,
        ) = prefs.edit().putBoolean(key, value).apply()
    }

    companion object {
        const val MIN_SENSITIVITY = 0.5f
        const val MAX_SENSITIVITY = 2.5f
        const val DEFAULT_SENSITIVITY = 1.4f
        private const val NAME = "edgepad"
        private const val NONE = "-"
        private const val KEY_LAPTOP = "laptop"
        private const val KEY_SENSITIVITY = "sensitivity"
    }
}
