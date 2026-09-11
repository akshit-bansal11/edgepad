package me.akshitbansal.edgepad.gamepad

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
 * One control on the gamepad surface. [x] and [y] are the fractions (0..1) of the screen where its
 * centre sits; [size] is its diameter (BUTTON/STICK/DPAD) or width (SHOULDER) in dp. [keys] holds the
 * Windows virtual-key codes it presses: one for BUTTON/SHOULDER, four as [up, down, left, right] for
 * DPAD/STICK.
 */
data class Control(
    val id: String,
    val kind: ControlKind,
    val label: String,
    val x: Float,
    val y: Float,
    val size: Float,
    val keys: List<Int>,
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
    /** One line per control, `|`-separated fields, keys `,`-separated; the layout name on the first line. */
    fun encode(): String {
        val lines = mutableListOf(name)
        for (c in controls) {
            lines += listOf(c.id, c.kind.name, c.label, c.x, c.y, c.size, c.keys.joinToString(",")).joinToString("|")
        }
        return lines.joinToString("\n")
    }

    companion object {
        private const val FIELD_COUNT = 7

        /** Parses [encode]'s format; malformed input of any kind yields null rather than a partial layout. */
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
                val keys = fields[6].split(",").map { it.toIntOrNull() ?: return null }
                val expectedKeys = if (kind == ControlKind.DPAD || kind == ControlKind.STICK) 4 else 1
                if (keys.size != expectedKeys) return null
                controls += Control(id, kind, label, x, y, size, keys)
            }
            if (controls.isEmpty()) return null
            return GamepadLayout(name, controls)
        }

        val presets: List<GamepadLayout> = listOf(xbox(), platformer(), racing(), shooter())

        private fun xbox(): GamepadLayout =
            GamepadLayout(
                "Xbox",
                listOf(
                    Control(
                        "stick",
                        ControlKind.STICK,
                        "L",
                        0.14f,
                        0.55f,
                        Control.STICK_SIZE_DP,
                        listOf(0x57, 0x53, 0x41, 0x44),
                    ),
                    Control(
                        "dpad",
                        ControlKind.DPAD,
                        "D",
                        0.30f,
                        0.72f,
                        Control.DPAD_SIZE_DP,
                        listOf(0x26, 0x28, 0x25, 0x27),
                    ),
                    Control("a", ControlKind.BUTTON, "A", 0.82f, 0.65f, Control.BUTTON_SIZE_DP, listOf(0x20)),
                    Control("b", ControlKind.BUTTON, "B", 0.90f, 0.55f, Control.BUTTON_SIZE_DP, listOf(0x45)),
                    Control("x", ControlKind.BUTTON, "X", 0.74f, 0.55f, Control.BUTTON_SIZE_DP, listOf(0x51)),
                    Control("y", ControlKind.BUTTON, "Y", 0.82f, 0.45f, Control.BUTTON_SIZE_DP, listOf(0x52)),
                    Control("lb", ControlKind.SHOULDER, "LB", 0.12f, 0.12f, Control.SHOULDER_SIZE_DP, listOf(0x10)),
                    Control("rb", ControlKind.SHOULDER, "RB", 0.88f, 0.12f, Control.SHOULDER_SIZE_DP, listOf(0x46)),
                    Control("lt", ControlKind.SHOULDER, "LT", 0.24f, 0.12f, Control.SHOULDER_SIZE_DP, listOf(0x09)),
                    Control("rt", ControlKind.SHOULDER, "RT", 0.76f, 0.12f, Control.SHOULDER_SIZE_DP, listOf(0x0D)),
                    Control("start", ControlKind.BUTTON, "Start", 0.58f, 0.9f, START_SIZE_DP, listOf(0x1B)),
                    Control("select", ControlKind.BUTTON, "Select", 0.42f, 0.9f, START_SIZE_DP, listOf(0x08)),
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
                        listOf(0x26, 0x28, 0x25, 0x27),
                    ),
                    Control("jump", ControlKind.BUTTON, "Jump", 0.85f, 0.6f, PLATFORMER_BUTTON_DP, listOf(0x20)),
                    Control("run", ControlKind.BUTTON, "Run", 0.72f, 0.72f, PLATFORMER_BUTTON_DP, listOf(0x58)),
                    Control("pause", ControlKind.BUTTON, "Pause", 0.5f, 0.9f, START_SIZE_DP, listOf(0x1B)),
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
                        listOf(0x26),
                    ),
                    Control("brake", ControlKind.SHOULDER, "Brake", 0.85f, 0.85f, RACING_PEDAL_DP, listOf(0x28)),
                    Control("left", ControlKind.BUTTON, "Left", 0.12f, 0.7f, RACING_STEER_DP, listOf(0x25)),
                    Control("right", ControlKind.BUTTON, "Right", 0.28f, 0.7f, RACING_STEER_DP, listOf(0x27)),
                    Control("nitro", ControlKind.BUTTON, "Nitro", 0.5f, 0.85f, RACING_NITRO_DP, listOf(0x10)),
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
                        listOf(0x57, 0x53, 0x41, 0x44),
                    ),
                    Control("fire", ControlKind.BUTTON, "Fire", 0.85f, 0.6f, SHOOTER_FIRE_DP, listOf(0x11)),
                    Control("jump", ControlKind.BUTTON, "Jump", 0.72f, 0.75f, SHOOTER_JUMP_DP, listOf(0x20)),
                    Control("reload", ControlKind.BUTTON, "Reload", 0.9f, 0.35f, SHOOTER_SMALL_DP, listOf(0x52)),
                    Control("crouch", ControlKind.BUTTON, "Crouch", 0.72f, 0.4f, SHOOTER_SMALL_DP, listOf(0x43)),
                    Control("aim", ControlKind.SHOULDER, "Aim", 0.85f, 0.12f, Control.SHOULDER_SIZE_DP, listOf(0x10)),
                ),
            )

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
