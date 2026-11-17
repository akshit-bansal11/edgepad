package me.akshitbansal.edgepad.screens

/** Rules for text that would be squeezed or cut off on a narrow screen, apart from the views so they can be tested. */
object Squeeze {
    /** Whether controls [total] px wide in all do not fit one row [room] px wide, so they stack instead. */
    fun stacks(
        total: Int,
        room: Int,
    ): Boolean = total > room

    /**
     * One segment's width: the [natural] width of the widest label, or a share of [room] when [count] of those do
     * not fit in it, in which case the labels wrap onto a second line.
     */
    fun segmentWidth(
        natural: Int,
        count: Int,
        room: Int,
    ): Int = minOf(natural, maxOf(room / count, 1))
}
