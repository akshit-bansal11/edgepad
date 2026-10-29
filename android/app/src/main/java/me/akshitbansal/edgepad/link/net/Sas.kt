package me.akshitbansal.edgepad.link.net

/**
 * The six-digit pairing code this phone shows, for the laptop's owner to type there. Six digits like a
 * [me.akshitbansal.edgepad.protocol.Pin], and kept out of logs the same way: [toString] returns stars, and the
 * digits come out only through [display], for the screen.
 */
class Sas(
    private val digits: String,
) {
    init {
        require(digits.length == LENGTH && digits.all { it in '0'..'9' }) { "A pairing code is exactly six digits" }
    }

    /** The code as the screen shows it, in two groups of three: "482 913". */
    fun display(): String = digits.substring(0, LENGTH / 2) + " " + digits.substring(LENGTH / 2)

    override fun toString(): String = "******"

    companion object {
        const val LENGTH = 6
    }
}
