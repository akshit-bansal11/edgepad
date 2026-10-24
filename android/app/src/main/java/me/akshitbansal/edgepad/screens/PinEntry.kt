package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.protocol.Pin

/**
 * The digits typed on the PIN screen so far, and nothing else. Kept in a char array rather than a string so
 * [clear] can overwrite them instead of leaving a copy for the collector, and owned by the screen alone, so it
 * goes when the screen does: nothing saves it.
 */
class PinEntry {
    private val digits = CharArray(Pin.LENGTH)

    /** How many digits are in, which is all the screen shows of them. */
    var length = 0
        private set

    val isComplete: Boolean get() = length == Pin.LENGTH

    /** Adds [digit], 0 to 9. Ignored once all six are in, so a seventh tap cannot change the answer. */
    fun press(digit: Int) {
        require(digit in 0..MAX_DIGIT) { "Not a digit" }
        if (isComplete) return
        digits[length++] = '0' + digit
    }

    fun delete() {
        if (length > 0) digits[--length] = BLANK
    }

    fun clear() {
        digits.fill(BLANK)
        length = 0
    }

    /** The six digits as a [Pin]. Only once [isComplete]. */
    fun pin(): Pin {
        check(isComplete) { "The PIN is not complete" }
        return Pin(String(digits))
    }

    private companion object {
        const val MAX_DIGIT = 9
        const val BLANK = ' '
    }
}
