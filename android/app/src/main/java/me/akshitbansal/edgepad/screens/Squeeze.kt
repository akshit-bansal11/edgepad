package me.akshitbansal.edgepad.screens

/** Rules for text that would be squeezed or cut off on a narrow screen, apart from the views so they can be tested. */
object Squeeze {
    /** Whether controls [total] px wide in all do not fit one row [room] px wide, so they stack instead. */
    fun stacks(
        total: Int,
        room: Int,
    ): Boolean = total > room
}
