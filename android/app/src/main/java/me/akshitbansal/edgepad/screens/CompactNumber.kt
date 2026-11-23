package me.akshitbansal.edgepad.screens

/** Token counts the way a ring caption can hold them: 999, 1.2K, 1.9M, 12.3B. Pure, so a table can test it. */
object CompactNumber {
    private val units = charArrayOf('K', 'M', 'B')
    private const val STEP = 1000L

    /** [n] below zero reads as 0. The tenth is cut, never rounded up, so 999_999 is 999.9K and never 1000K. */
    fun format(
        n: Long,
        decimal: Char = '.',
    ): String {
        val v = n.coerceAtLeast(0L)
        if (v < STEP) return v.toString()
        var unit = STEP
        var index = 0
        while (index < units.lastIndex && v >= unit * STEP) {
            unit *= STEP
            index++
        }
        val whole = v / unit
        val tenth = (v % unit) * 10 / unit
        return if (tenth == 0L) "$whole${units[index]}" else "$whole$decimal$tenth${units[index]}"
    }
}
