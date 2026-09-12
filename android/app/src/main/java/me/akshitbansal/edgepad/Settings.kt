package me.akshitbansal.edgepad

import android.content.Context
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.Gesture
import me.akshitbansal.edgepad.surface.GestureAction
import me.akshitbansal.edgepad.surface.MediaPiece
import java.io.File
import kotlin.properties.ReadWriteProperty
import kotlin.reflect.KProperty

/** Everything the app remembers between launches. The theme is the system's own per-app night mode. */
class Settings(
    context: Context,
) {
    /** What fills the control surface behind everything else. */
    enum class Background { THEME, COLOR, GRADIENT, IMAGE }

    /** A repeating pattern drawn over the background. */
    enum class Pattern { NONE, SQUARES, DOTS, CHECKER }

    private val prefs = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)

    /** Where an imported background image is kept; absent until one has been imported. */
    val backgroundImage: File = File(context.filesDir, IMAGE_FILE)

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

    /** The surface, settings and finder are held sideways; the keyboard and gamepad always are. */
    var landscape by flag("landscape", false)

    /** Half a corner ruler's length along the edge, in dp. */
    var dialLength by bounded(KEY_DIAL_LENGTH, DEFAULT_DIAL_LENGTH, MIN_DIAL_LENGTH, MAX_DIAL_LENGTH)

    /** How tall the ticks stand, as a multiple of the base height. */
    var dialHeight by bounded(KEY_DIAL_HEIGHT, DEFAULT_DIAL_HEIGHT, MIN_DIAL_HEIGHT, MAX_DIAL_HEIGHT)

    /** How much a dial moves per dp of slide, as a multiple of the base rate. */
    var sensitivity by bounded(KEY_SENSITIVITY, DEFAULT_SENSITIVITY, MIN_SENSITIVITY, MAX_SENSITIVITY)

    /** The colour every control is drawn in, or null for whatever reads on the background. */
    var controlColor: Int?
        get() = if (prefs.contains(KEY_CONTROL_COLOR)) prefs.getInt(KEY_CONTROL_COLOR, 0) else null
        set(value) {
            val edit = prefs.edit()
            if (value == null) edit.remove(KEY_CONTROL_COLOR) else edit.putInt(KEY_CONTROL_COLOR, value)
            edit.apply()
        }

    var background: Background
        get() = enum(KEY_BACKGROUND, Background.THEME)
        set(value) = prefs.edit().putString(KEY_BACKGROUND, value.name).apply()

    var backgroundColor by color(KEY_BACKGROUND_COLOR, DEFAULT_BACKGROUND)

    /** The second colour of a gradient; the first is [backgroundColor]. */
    var gradientEnd by color(KEY_GRADIENT_END, DEFAULT_GRADIENT_END)

    /** The gradient's direction in degrees: 0 left to right, 90 top to bottom. */
    var gradientAngle by bounded(KEY_GRADIENT_ANGLE, DEFAULT_GRADIENT_ANGLE, 0f, MAX_ANGLE)

    var pattern: Pattern
        get() = enum(KEY_PATTERN, Pattern.SQUARES)
        set(value) = prefs.edit().putString(KEY_PATTERN, value.name).apply()

    /** How large the media pieces are drawn, as a multiple of their base size. */
    var mediaScale by bounded(KEY_MEDIA_SCALE, DEFAULT_MEDIA_SCALE, MIN_MEDIA_SCALE, MAX_MEDIA_SCALE)

    /** The pattern's cell size, in dp. */
    var patternSize by bounded(KEY_PATTERN_SIZE, DEFAULT_PATTERN_SIZE, MIN_PATTERN_SIZE, MAX_PATTERN_SIZE)

    var patternColor by color(KEY_PATTERN_COLOR, DEFAULT_PATTERN_COLOR)

    /** The pattern's opacity, 0 to 1. */
    var patternOpacity by bounded(KEY_PATTERN_OPACITY, DEFAULT_PATTERN_OPACITY, 0f, 1f)

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

    /** True when some corner holds [kind]. */
    fun hasDial(kind: DialKind): Boolean = (0 until CORNERS).any { corner(it) == kind }

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
            .putFloat("piece.${piece.name}.x", x.coerceIn(0f, 1f))
            .putFloat("piece.${piece.name}.y", y.coerceIn(0f, 1f))
            .apply()
    }

    /** Puts every media piece back where it started. */
    fun resetPieces() {
        val edit = prefs.edit()
        for (piece in MediaPiece.entries) edit.remove("piece.${piece.name}.x").remove("piece.${piece.name}.y")
        edit.apply()
    }

    private inline fun <reified E : Enum<E>> enum(
        key: String,
        default: E,
    ): E {
        val name = prefs.getString(key, null) ?: return default
        return enumValues<E>().firstOrNull { it.name == name } ?: default
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

    private fun bounded(
        key: String,
        default: Float,
        min: Float,
        max: Float,
    ) = object : ReadWriteProperty<Any?, Float> {
        override fun getValue(
            thisRef: Any?,
            property: KProperty<*>,
        ): Float = prefs.getFloat(key, default).coerceIn(min, max)

        override fun setValue(
            thisRef: Any?,
            property: KProperty<*>,
            value: Float,
        ) = prefs.edit().putFloat(key, value.coerceIn(min, max)).apply()
    }

    private fun color(
        key: String,
        default: Int,
    ) = object : ReadWriteProperty<Any?, Int> {
        override fun getValue(
            thisRef: Any?,
            property: KProperty<*>,
        ): Int = prefs.getInt(key, default)

        override fun setValue(
            thisRef: Any?,
            property: KProperty<*>,
            value: Int,
        ) = prefs.edit().putInt(key, value).apply()
    }

    companion object {
        const val CORNERS = 4
        const val MIN_SENSITIVITY = 0.5f
        const val MAX_SENSITIVITY = 2.5f
        const val DEFAULT_SENSITIVITY = 1.4f
        const val MIN_DIAL_LENGTH = 60f
        const val MAX_DIAL_LENGTH = 320f
        const val DEFAULT_DIAL_LENGTH = 100f
        const val MIN_DIAL_HEIGHT = 0.4f
        const val MAX_DIAL_HEIGHT = 1.8f
        const val DEFAULT_DIAL_HEIGHT = 0.6f
        const val MIN_MEDIA_SCALE = 0.5f
        const val MAX_MEDIA_SCALE = 1.5f
        const val DEFAULT_MEDIA_SCALE = 1f
        const val MAX_ANGLE = 360f
        const val DEFAULT_GRADIENT_ANGLE = 90f
        const val MIN_PATTERN_SIZE = 8f
        const val MAX_PATTERN_SIZE = 96f
        const val DEFAULT_PATTERN_SIZE = 35f
        const val DEFAULT_PATTERN_OPACITY = 0.15f
        const val DEFAULT_BACKGROUND = 0xFF0F0F12.toInt()
        const val DEFAULT_GRADIENT_END = 0xFF2B2B33.toInt()
        const val DEFAULT_PATTERN_COLOR = 0xFF84848C.toInt()
        private const val NAME = "edgepad"
        private const val NONE = "-"
        private const val IMAGE_FILE = "background.img"
        private const val KEY_LAPTOP = "laptop"
        private const val KEY_SENSITIVITY = "sensitivity"
        private const val KEY_DIAL_LENGTH = "dialLength"
        private const val KEY_DIAL_HEIGHT = "dialHeight"
        private const val KEY_CONTROL_COLOR = "controlColor"
        private const val KEY_BACKGROUND = "background"
        private const val KEY_BACKGROUND_COLOR = "backgroundColor"
        private const val KEY_GRADIENT_END = "gradientEnd"
        private const val KEY_GRADIENT_ANGLE = "gradientAngle"
        private const val KEY_PATTERN = "pattern"
        private const val KEY_MEDIA_SCALE = "mediaScale"
        private const val KEY_PATTERN_SIZE = "patternSize"
        private const val KEY_PATTERN_COLOR = "patternColor"
        private const val KEY_PATTERN_OPACITY = "patternOpacity"
    }
}
