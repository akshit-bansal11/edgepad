package me.akshitbansal.edgepad

import android.content.Context
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.Placement
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

    var hints by flag("hints", true)

    /** How much a dial moves per dp of slide, as a multiple of the base rate. */
    var sensitivity: Float
        get() = prefs.getFloat(KEY_SENSITIVITY, DEFAULT_SENSITIVITY).coerceIn(MIN_SENSITIVITY, MAX_SENSITIVITY)
        set(value) = prefs.edit().putFloat(KEY_SENSITIVITY, value.coerceIn(MIN_SENSITIVITY, MAX_SENSITIVITY)).apply()

    /** Where [kind] sits, or null when it is switched off. */
    fun placement(kind: DialKind): Placement? {
        val at = prefs.getFloat(dialKey(kind), kind.defaultAt ?: OFF)
        return if (at < 0f) null else Placement.of(at)
    }

    fun place(
        kind: DialKind,
        placement: Placement?,
    ) {
        prefs.edit().putFloat(dialKey(kind), placement?.at ?: OFF).apply()
    }

    private fun dialKey(kind: DialKind) = "dial.${kind.name}.at"

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
        private const val KEY_LAPTOP = "laptop"
        private const val KEY_SENSITIVITY = "sensitivity"
        private const val OFF = -1f
    }
}
