package me.akshitbansal.edgepad.gamepad

import me.akshitbansal.edgepad.protocol.PadButton

/** What shape a control draws as and how its keys are pressed. */
enum class ControlKind {
    /** Round, one key. */
    BUTTON,

    /** A cross; four keys (up, down, left, right); a diagonal press presses two. */
    DPAD,

    /** A round base with a thumb the finger drags; four keys like a dpad, pressed by direction past a dead zone. */
    STICK,

    /** A wide rounded rectangle, one key. */
    SHOULDER,
}

/**
 * What one control drives on the laptop: a real controller input, or keyboard keys.
 *
 * Both, rather than only the controller, because the laptop decides at run time whether it can offer a
 * virtual pad at all. A laptop with no driver still has a keyboard, so a layout built out of [Keys] is the
 * only kind that works everywhere, and it stays the way the four game presets are written. A control bound
 * to anything else simply does nothing while the pad is unavailable.
 */
sealed interface Binding {
    /** Which one of a pair: the left or the right stick, the left or the right trigger, the left or right bumper. */
    enum class Side { LEFT, RIGHT }

    /**
     * Windows virtual-key codes: one for a button or a shoulder, four as [up, down, left, right] for a
     * d-pad or a stick. The binding that needs nothing of the laptop but a keyboard.
     */
    data class Keys(
        val codes: List<Int>,
    ) : Binding

    /** One controller button, held for exactly as long as the control is. */
    data class Button(
        val button: PadButton,
    ) : Binding

    /**
     * The d-pad's four buttons, pressed by the direction the finger is from the centre. A kind of its own
     * rather than four [Button]s, because XInput fixes which four bits a d-pad is and nothing chooses them.
     */
    data object Dpad : Binding

    /** One analog stick: the thumb's offset from the centre becomes both of that stick's axes. */
    data class Stick(
        val side: Side,
    ) : Binding

    /** One analog trigger: how far down the control the finger sits becomes its 0..255 pull. */
    data class Trigger(
        val side: Side,
    ) : Binding

    /**
     * Whether a control of [kind] can drive this binding. A stick's two axes need a thumb to move, a
     * trigger's travel needs a shoulder's height to measure against, and the d-pad's four bits need four
     * arms to press them, so the kind and the binding are checked together or a stored layout could name
     * a control that cannot possibly work the way it says.
     */
    fun fits(kind: ControlKind): Boolean =
        when (this) {
            is Keys -> codes.size == if (kind == ControlKind.DPAD || kind == ControlKind.STICK) 4 else 1
            is Button -> kind == ControlKind.BUTTON || kind == ControlKind.SHOULDER
            Dpad -> kind == ControlKind.DPAD
            is Stick -> kind == ControlKind.STICK
            is Trigger -> kind == ControlKind.SHOULDER
        }

    /** The stored form: a one-letter tag, then what that tag needs. */
    fun encode(): String =
        when (this) {
            is Keys -> "$KEYS:${codes.joinToString(",")}"
            is Button -> "$BUTTON:${button.name}"
            Dpad -> DPAD
            is Stick -> "$STICK:${side.name}"
            is Trigger -> "$TRIGGER:${side.name}"
        }

    companion object {
        private const val KEYS = "K"
        private const val BUTTON = "B"
        private const val DPAD = "D"
        private const val STICK = "S"
        private const val TRIGGER = "T"

        /** Parses [encode]'s form. Anything else — an unknown tag, an unknown name, no tag at all — is null. */
        fun decode(text: String): Binding? {
            if (text == DPAD) return Dpad
            val payload = text.substringAfter(':', "")
            return when (text.substringBefore(':', "")) {
                KEYS -> Keys(payload.split(",").map { it.toIntOrNull() ?: return null })
                BUTTON -> PadButton.entries.firstOrNull { it.name == payload }?.let(::Button)
                STICK -> side(payload)?.let(::Stick)
                TRIGGER -> side(payload)?.let(::Trigger)
                else -> null
            }
        }

        private fun side(name: String): Side? = Side.entries.firstOrNull { it.name == name }
    }
}

/**
 * One control on the gamepad surface. [x] and [y] are the fractions (0..1) of the screen where its
 * centre sits; [size] is its diameter (BUTTON/STICK/DPAD) or width (SHOULDER) in dp. [binding] is what it
 * drives on the laptop, and always one [Binding.fits] this [kind].
 */
data class Control(
    val id: String,
    val kind: ControlKind,
    val label: String,
    val x: Float,
    val y: Float,
    val size: Float,
    val binding: Binding,
) {
    companion object {
        const val BUTTON_SIZE_DP = 64f
        const val DPAD_SIZE_DP = 150f
        const val STICK_SIZE_DP = 150f
        const val SHOULDER_SIZE_DP = 110f
    }
}

/** A named set of controls; what the play screen renders and drives the laptop from. */
data class GamepadLayout(
    val name: String,
    val controls: List<Control>,
) {
    /** One line per control, `|`-separated fields, the binding in its own encoded form; the name on the first line. */
    fun encode(): String {
        val lines = mutableListOf(name)
        for (c in controls) {
            lines += listOf(c.id, c.kind.name, c.label, c.x, c.y, c.size, c.binding.encode()).joinToString("|")
        }
        return lines.joinToString("\n")
    }

    companion object {
        private const val FIELD_COUNT = 7

        /**
         * Parses [encode]'s format; malformed input of any kind yields null rather than a partial layout.
         *
         * A layout saved by 2.x is malformed input here, deliberately: its last field is a bare list of
         * key codes with no binding tag, so [Binding.decode] refuses it and [GamepadStore] falls back to
         * the first preset. Refused rather than migrated because the migration would be a lie — 3.0.0's
         * first preset is a real controller and a 2.x layout has no controller bindings in it at all, so
         * the honest outcome of reading one is the controller layout the owner would have had to choose
         * anyway. Only the dragged positions are lost, and only once.
         */
        fun decode(text: String): GamepadLayout? {
            val lines = text.split("\n")
            val name = lines.firstOrNull() ?: return null
            val controls = mutableListOf<Control>()
            for (line in lines.drop(1)) {
                if (line.isEmpty()) continue
                val fields = line.split("|")
                if (fields.size != FIELD_COUNT) return null
                val id = fields[0]
                val kind = ControlKind.entries.firstOrNull { it.name == fields[1] } ?: return null
                val label = fields[2]
                val x = fields[3].toFloatOrNull() ?: return null
                val y = fields[4].toFloatOrNull() ?: return null
                val size = fields[5].toFloatOrNull() ?: return null
                val binding = Binding.decode(fields[6]) ?: return null
                if (!binding.fits(kind)) return null
                controls += Control(id, kind, label, x, y, size, binding)
            }
            if (controls.isEmpty()) return null
            return GamepadLayout(name, controls)
        }

        // Declared before [presets], which builds the layouts that hold them: a companion initialises its
        // properties in the order they are written, and these are not compile-time constants.
        private val leftStick = Binding.Stick(Binding.Side.LEFT)
        private val rightStick = Binding.Stick(Binding.Side.RIGHT)
        private val leftTrigger = Binding.Trigger(Binding.Side.LEFT)
        private val rightTrigger = Binding.Trigger(Binding.Side.RIGHT)
        private val leftBumper = Binding.Button(PadButton.LEFT_SHOULDER)
        private val rightBumper = Binding.Button(PadButton.RIGHT_SHOULDER)

        /**
         * The controller layout first: it is what the laptop can now be asked for, and it is also what a
         * refused stored layout falls back to. The four after it are keyboard layouts and stay that way —
         * they are the only thing that works when the laptop cannot offer a pad at all.
         */
        val presets: List<GamepadLayout> = listOf(xbox(), xboxKeys(), platformer(), racing(), shooter())

        /**
         * XInput's whole standard set, laid out the way the controller it copies is: sticks and d-pad on
         * the left, the four face buttons on the right, bumpers and triggers along the top edge, Start and
         * Back in the middle, and the two stick clicks in the bottom corners where a thumb can reach them
         * without leaving the stick it belongs to.
         *
         * Every binding here is a controller input, so this layout does nothing in keyboard mode. The pad
         * says so on screen when that happens, and the keyboard presets are one tap away in the same popup.
         */
        private fun xbox(): GamepadLayout =
            GamepadLayout(
                "Xbox",
                listOf(
                    Control("lstick", ControlKind.STICK, "L", 0.13f, 0.46f, Control.STICK_SIZE_DP, leftStick),
                    Control("rstick", ControlKind.STICK, "R", 0.70f, 0.78f, Control.STICK_SIZE_DP, rightStick),
                    Control("dpad", ControlKind.DPAD, "D", 0.30f, 0.78f, Control.DPAD_SIZE_DP, Binding.Dpad),
                    face("a", "A", 0.84f, 0.58f, PadButton.A),
                    face("b", "B", 0.92f, 0.46f, PadButton.B),
                    face("x", "X", 0.76f, 0.46f, PadButton.X),
                    face("y", "Y", 0.84f, 0.34f, PadButton.Y),
                    Control("lt", ControlKind.SHOULDER, "LT", 0.10f, 0.12f, Control.SHOULDER_SIZE_DP, leftTrigger),
                    Control("lb", ControlKind.SHOULDER, "LB", 0.24f, 0.12f, Control.SHOULDER_SIZE_DP, leftBumper),
                    Control("rb", ControlKind.SHOULDER, "RB", 0.76f, 0.12f, Control.SHOULDER_SIZE_DP, rightBumper),
                    Control("rt", ControlKind.SHOULDER, "RT", 0.90f, 0.12f, Control.SHOULDER_SIZE_DP, rightTrigger),
                    face("start", "Start", 0.56f, 0.92f, PadButton.START, START_SIZE_DP),
                    face("back", "Back", 0.44f, 0.92f, PadButton.BACK, START_SIZE_DP),
                    face("l3", "L3", 0.05f, 0.88f, PadButton.LEFT_THUMB, START_SIZE_DP),
                    face("r3", "R3", 0.95f, 0.88f, PadButton.RIGHT_THUMB, START_SIZE_DP),
                ),
            )

        /** The same shape as [xbox], bound to keys: what the pad was before the laptop could offer a controller. */
        private fun xboxKeys(): GamepadLayout =
            GamepadLayout(
                "Xbox keys",
                listOf(
                    Control(
                        "stick",
                        ControlKind.STICK,
                        "L",
                        0.14f,
                        0.55f,
                        Control.STICK_SIZE_DP,
                        keys(0x57, 0x53, 0x41, 0x44),
                    ),
                    Control(
                        "dpad",
                        ControlKind.DPAD,
                        "D",
                        0.30f,
                        0.72f,
                        Control.DPAD_SIZE_DP,
                        keys(0x26, 0x28, 0x25, 0x27),
                    ),
                    Control("a", ControlKind.BUTTON, "A", 0.82f, 0.65f, Control.BUTTON_SIZE_DP, keys(0x20)),
                    Control("b", ControlKind.BUTTON, "B", 0.90f, 0.55f, Control.BUTTON_SIZE_DP, keys(0x45)),
                    Control("x", ControlKind.BUTTON, "X", 0.74f, 0.55f, Control.BUTTON_SIZE_DP, keys(0x51)),
                    Control("y", ControlKind.BUTTON, "Y", 0.82f, 0.45f, Control.BUTTON_SIZE_DP, keys(0x52)),
                    Control("lb", ControlKind.SHOULDER, "LB", 0.12f, 0.12f, Control.SHOULDER_SIZE_DP, keys(0x10)),
                    Control("rb", ControlKind.SHOULDER, "RB", 0.88f, 0.12f, Control.SHOULDER_SIZE_DP, keys(0x46)),
                    Control("lt", ControlKind.SHOULDER, "LT", 0.24f, 0.12f, Control.SHOULDER_SIZE_DP, keys(0x09)),
                    Control("rt", ControlKind.SHOULDER, "RT", 0.76f, 0.12f, Control.SHOULDER_SIZE_DP, keys(0x0D)),
                    Control("start", ControlKind.BUTTON, "Start", 0.58f, 0.9f, START_SIZE_DP, keys(0x1B)),
                    Control("select", ControlKind.BUTTON, "Select", 0.42f, 0.9f, START_SIZE_DP, keys(0x08)),
                ),
            )

        private fun platformer(): GamepadLayout =
            GamepadLayout(
                "Platformer",
                listOf(
                    Control(
                        "dpad",
                        ControlKind.DPAD,
                        "D",
                        0.18f,
                        0.6f,
                        Control.DPAD_SIZE_DP,
                        keys(0x26, 0x28, 0x25, 0x27),
                    ),
                    Control("jump", ControlKind.BUTTON, "Jump", 0.85f, 0.6f, PLATFORMER_BUTTON_DP, keys(0x20)),
                    Control("run", ControlKind.BUTTON, "Run", 0.72f, 0.72f, PLATFORMER_BUTTON_DP, keys(0x58)),
                    Control("pause", ControlKind.BUTTON, "Pause", 0.5f, 0.9f, START_SIZE_DP, keys(0x1B)),
                ),
            )

        private fun racing(): GamepadLayout =
            GamepadLayout(
                "Racing",
                listOf(
                    Control(
                        "accelerate",
                        ControlKind.SHOULDER,
                        "Accelerate",
                        0.85f,
                        0.6f,
                        RACING_PEDAL_DP,
                        keys(0x26),
                    ),
                    Control("brake", ControlKind.SHOULDER, "Brake", 0.85f, 0.85f, RACING_PEDAL_DP, keys(0x28)),
                    Control("left", ControlKind.BUTTON, "Left", 0.12f, 0.7f, RACING_STEER_DP, keys(0x25)),
                    Control("right", ControlKind.BUTTON, "Right", 0.28f, 0.7f, RACING_STEER_DP, keys(0x27)),
                    Control("nitro", ControlKind.BUTTON, "Nitro", 0.5f, 0.85f, RACING_NITRO_DP, keys(0x10)),
                ),
            )

        private fun shooter(): GamepadLayout =
            GamepadLayout(
                "Shooter",
                listOf(
                    Control(
                        "stick",
                        ControlKind.STICK,
                        "L",
                        0.14f,
                        0.55f,
                        Control.STICK_SIZE_DP,
                        keys(0x57, 0x53, 0x41, 0x44),
                    ),
                    Control("fire", ControlKind.BUTTON, "Fire", 0.85f, 0.6f, SHOOTER_FIRE_DP, keys(0x11)),
                    Control("jump", ControlKind.BUTTON, "Jump", 0.72f, 0.75f, SHOOTER_JUMP_DP, keys(0x20)),
                    Control("reload", ControlKind.BUTTON, "Reload", 0.9f, 0.35f, SHOOTER_SMALL_DP, keys(0x52)),
                    Control("crouch", ControlKind.BUTTON, "Crouch", 0.72f, 0.4f, SHOOTER_SMALL_DP, keys(0x43)),
                    Control("aim", ControlKind.SHOULDER, "Aim", 0.85f, 0.12f, Control.SHOULDER_SIZE_DP, keys(0x10)),
                ),
            )

        /** A round button holding one controller button down. Spelled out because the four face buttons repeat it. */
        private fun face(
            id: String,
            label: String,
            x: Float,
            y: Float,
            button: PadButton,
            size: Float = Control.BUTTON_SIZE_DP,
        ): Control = Control(id, ControlKind.BUTTON, label, x, y, size, Binding.Button(button))

        private fun keys(vararg codes: Int): Binding = Binding.Keys(codes.toList())

        private const val START_SIZE_DP = 44f
        private const val PLATFORMER_BUTTON_DP = 72f
        private const val RACING_PEDAL_DP = 160f
        private const val RACING_STEER_DP = 80f
        private const val RACING_NITRO_DP = 56f
        private const val SHOOTER_FIRE_DP = 80f
        private const val SHOOTER_JUMP_DP = 60f
        private const val SHOOTER_SMALL_DP = 52f
    }
}
