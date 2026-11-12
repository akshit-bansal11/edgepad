package me.akshitbansal.edgepad.screens

/**
 * A two-tap guard for a button that throws work away: the first tap only arms it (the button then says what
 * the second tap will do), the second runs the action. Pure, so the state is tested on the JVM.
 */
class ConfirmTap {
    var armed: Boolean = false
        private set

    /** True when this tap is the confirming one and the action should run. */
    fun tap(): Boolean {
        val confirmed = armed
        armed = !armed
        return confirmed
    }
}
