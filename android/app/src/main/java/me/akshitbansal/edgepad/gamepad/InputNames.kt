package me.akshitbansal.edgepad.gamepad

import me.akshitbansal.edgepad.protocol.PadButton

/**
 * What the two things a control can be bound to are called on screen: a key on the laptop's keyboard,
 * and a button on the virtual controller.
 *
 * Pure, so the table is tested on the JVM rather than reached through a View, and in Kotlin rather than
 * in strings.xml because every name here is a legend — the letter moulded into a keycap, "LB", "R3" —
 * and a legend is the same characters whatever language the phone is set to. The prose that frames them
 * ("Left stick", "Key for up") stays in strings.xml, where a translator can reach it.
 */
object InputNames {
    /** One Windows virtual-key code and the legend on the key that sends it. */
    data class Key(
        val code: Int,
        val legend: String,
    )

    /**
     * The keys a control may be bound to.
     *
     * Deliberately a short list rather than all 256 codes. Most of the range is either impossible to
     * reach on a phone's picker without scrolling past two hundred rows, or meaningless in a game — OEM
     * punctuation, the IME keys, the browser-favourites keys a laptop maker wired to a button nobody has.
     * What is here is what a game actually binds: the letters, the digits, the arrows, the three
     * modifiers, and the five large keys. Every code the built-in presets use is in it, which a test holds.
     */
    val keys: List<Key> = build()

    /** The legend for [code], or the code itself when it is one this table does not name. */
    fun legendOf(code: Int): String = keys.firstOrNull { it.code == code }?.legend ?: code.toString()

    /**
     * What a controller button is called. Exhaustive on purpose: adding a button to [PadButton] should
     * stop this compiling rather than quietly draw a control with no name on it.
     */
    fun legendOf(button: PadButton): String =
        when (button) {
            // Never offered as a binding — it is the empty mask — but a stored layout could still name it.
            PadButton.NONE -> "-"

            PadButton.A -> "A"

            PadButton.B -> "B"

            PadButton.X -> "X"

            PadButton.Y -> "Y"

            PadButton.LEFT_SHOULDER -> "LB"

            PadButton.RIGHT_SHOULDER -> "RB"

            PadButton.START -> "START"

            PadButton.BACK -> "BACK"

            PadButton.GUIDE -> "GUIDE"

            PadButton.LEFT_THUMB -> "L3"

            PadButton.RIGHT_THUMB -> "R3"

            PadButton.DPAD_UP -> "D-PAD UP"

            PadButton.DPAD_DOWN -> "D-PAD DOWN"

            PadButton.DPAD_LEFT -> "D-PAD LEFT"

            PadButton.DPAD_RIGHT -> "D-PAD RIGHT"
        }

    /**
     * The large keys and the arrows first, because those are what a pad is mostly built from, then the
     * letters and the digits in their own order, which is the order anyone looking for one expects.
     */
    private fun build(): List<Key> = named() + letters() + digits()

    private fun named(): List<Key> =
        listOf(
            Key(0x20, "SPACE"),
            Key(0x0D, "ENTER"),
            Key(0x1B, "ESC"),
            Key(0x09, "TAB"),
            Key(0x08, "BACKSPACE"),
            Key(0x10, "SHIFT"),
            Key(0x11, "CTRL"),
            Key(0x12, "ALT"),
            Key(0x26, "UP"),
            Key(0x28, "DOWN"),
            Key(0x25, "LEFT"),
            Key(0x27, "RIGHT"),
        )

    private fun letters(): List<Key> = (0 until LETTERS).map { Key(FIRST_LETTER + it, ('A' + it).toString()) }

    private fun digits(): List<Key> = (0 until DIGITS).map { Key(FIRST_DIGIT + it, ('0' + it).toString()) }

    /** VK_A..VK_Z and VK_0..VK_9 are contiguous and are exactly the ASCII codes of the characters. */
    private const val FIRST_LETTER = 0x41
    private const val FIRST_DIGIT = 0x30
    private const val LETTERS = 26
    private const val DIGITS = 10
}
