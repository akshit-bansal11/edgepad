package me.akshitbansal.edgepad

import android.content.Context
import android.content.SharedPreferences
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.Gesture
import me.akshitbansal.edgepad.surface.GestureAction
import me.akshitbansal.edgepad.surface.MediaPiece
import me.akshitbansal.edgepad.surface.Perimeter
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

    init {
        // Dials used to live in four corner keys and now live in eight slot keys. An install that
        // predates the change brings its corners across here, once, so that nothing further down has to
        // know the old shape, and so an old key can never quietly outrank what the user set since.
        val old = (0 until Perimeter.CORNERS).map { cornerKey(it) }.filter { prefs.contains(it) }
        if (old.isNotEmpty()) {
            val edit = prefs.edit()
            for ((key, value) in migratedSlots { prefs.getString(it, null) }) edit.putString(key, value)
            // The old keys go with the move: nothing writes them any more, so this runs exactly once.
            for (key in old) edit.remove(key)
            edit.apply()
        }
    }

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

    /** The guide as one scrolling page rather than steps. */
    var guideScroll by flag("guideScroll", false)

    /** Half a corner ruler's length along the edge, in dp. */
    var dialLength by bounded(KEY_DIAL_LENGTH, DEFAULT_DIAL_LENGTH, MIN_DIAL_LENGTH, MAX_DIAL_LENGTH)

    /** How tall the ticks stand, as a multiple of the base height. */
    var dialHeight by bounded(KEY_DIAL_HEIGHT, DEFAULT_DIAL_HEIGHT, MIN_DIAL_HEIGHT, MAX_DIAL_HEIGHT)

    /** How much a dial moves per dp of slide, as a multiple of the base rate. The fallback for every corner. */
    var sensitivity by bounded(KEY_SENSITIVITY, DEFAULT_SENSITIVITY, MIN_SENSITIVITY, MAX_SENSITIVITY)

    /** How far the laptop's pointer travels per unit of finger travel, as a multiple of the base rate. */
    var pointerSpeed by bounded(KEY_POINTER_SPEED, DEFAULT_SPEED, MIN_SPEED, MAX_SPEED)

    /** How much wheel one dp of two-finger drag is worth, as a multiple of the base rate. */
    var scrollSpeed by bounded(KEY_SCROLL_SPEED, DEFAULT_SPEED, MIN_SPEED, MAX_SPEED)

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

    /** The dial in [slot] (0 top-left, clockwise, the odd slots the edge midpoints between), or null for none. */
    fun slot(slot: Int): DialKind? {
        val name = prefs.getString(slotKey(slot), null)
        if (name == OFF) return null
        DialKind.entries.firstOrNull { it.name == name }?.let { return it }
        // An untouched slot falls back to the kind that calls it home. DialKind still names that home by
        // corner index, 0 to 3, because a kind's home is a corner; the corners are the even slots, so an
        // edge midpoint starts empty and stays empty until the user puts something there.
        return DialKind.entries.firstOrNull { kind -> kind.defaultCorner?.let { Perimeter.cornerSlot(it) } == slot }
    }

    fun setSlot(
        slot: Int,
        kind: DialKind?,
    ) {
        prefs.edit().putString(slotKey(slot), kind?.name ?: OFF).apply()
    }

    /**
     * How fast [kind]'s ruler moves, as a multiple of the base rate. Volume wants a slower ruler than the
     * app switcher does, so each kind may override [sensitivity]; none does until the user sets one, and
     * clearing an override puts the kind back on the shared value rather than on a second default.
     */
    fun sensitivityOf(kind: DialKind): Float =
        prefs
            .getFloat(sensitivityKey(kind), sensitivity)
            .coerceIn(MIN_SENSITIVITY, MAX_SENSITIVITY)

    fun setSensitivityOf(
        kind: DialKind,
        value: Float?,
    ) {
        val edit = prefs.edit()
        val key = sensitivityKey(kind)
        if (value == null) edit.remove(key) else edit.putFloat(key, value.coerceIn(MIN_SENSITIVITY, MAX_SENSITIVITY))
        edit.apply()
    }

    /** True when [kind] has a sensitivity of its own rather than following [sensitivity]. */
    fun hasOwnSensitivity(kind: DialKind): Boolean = prefs.contains(sensitivityKey(kind))

    private fun sensitivityKey(kind: DialKind): String = "sensitivity.${kind.name}"

    /** True when some slot holds [kind]. */
    fun hasDial(kind: DialKind): Boolean = (0 until Perimeter.SLOTS).any { slot(it) == kind }

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

    /**
     * One stored setting. Nothing is cached on either side: every read goes to SharedPreferences and
     * every write is flushed on assignment, so a screen rebuilt straight after a change reads back what
     * was just stored. Writes stay on apply() rather than commit(), which lint rejects on a UI thread.
     */
    private fun <T> pref(
        read: () -> T,
        write: SharedPreferences.Editor.(T) -> Unit,
    ) = object : ReadWriteProperty<Any?, T> {
        override fun getValue(
            thisRef: Any?,
            property: KProperty<*>,
        ): T = read()

        override fun setValue(
            thisRef: Any?,
            property: KProperty<*>,
            value: T,
        ) {
            val edit = prefs.edit()
            edit.write(value)
            edit.apply()
        }
    }

    private fun flag(
        key: String,
        default: Boolean,
    ) = pref({ prefs.getBoolean(key, default) }) { putBoolean(key, it) }

    /** A number kept inside its range on the way in and on the way out, so an old stored value cannot escape it. */
    private fun bounded(
        key: String,
        default: Float,
        min: Float,
        max: Float,
    ) = pref({ prefs.getFloat(key, default).coerceIn(min, max) }) { putFloat(key, it.coerceIn(min, max)) }

    private fun color(
        key: String,
        default: Int,
    ) = pref({ prefs.getInt(key, default) }) { putInt(key, it) }

    companion object {
        const val MIN_SPEED = 0.4f
        const val MAX_SPEED = 3f
        const val DEFAULT_SPEED = 1f
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

        /**
         * What a slot the user emptied stores. An absent key means they never chose, which hands the slot
         * to the kind that calls it home; this means they took that dial away. The two are not the same,
         * so the sentinel travels through the move from corner keys to slot keys like any other value.
         */
        const val OFF = "-"

        fun slotKey(slot: Int): String = "slot.$slot"

        /** The key a dial's corner was stored under before there were eight slots. */
        fun cornerKey(corner: Int): String = "corner.$corner"

        /**
         * The move from the four corner keys to the eight slot keys, as a pure map of what to write: the
         * one change here that can quietly ruin a setup someone already has, so it is provable on the JVM
         * rather than only on a phone that has been through an update. Corner c's value belongs to corner
         * c's slot, [OFF] included. A slot already written under the new scheme is never overwritten.
         */
        fun migratedSlots(stored: (String) -> String?): Map<String, String> =
            buildMap {
                for (corner in 0 until Perimeter.CORNERS) {
                    val value = stored(cornerKey(corner)) ?: continue
                    val key = slotKey(Perimeter.cornerSlot(corner))
                    // A slot already written under the new scheme is the later choice, and keeps it.
                    if (stored(key) == null) put(key, value)
                }
            }

        private const val NAME = "edgepad"
        private const val IMAGE_FILE = "background.img"
        private const val KEY_LAPTOP = "laptop"
        private const val KEY_SENSITIVITY = "sensitivity"
        private const val KEY_POINTER_SPEED = "pointerSpeed"
        private const val KEY_SCROLL_SPEED = "scrollSpeed"
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
