package me.akshitbansal.edgepad.link

import java.text.NumberFormat

/** The numbers of a STATS frame as text, one way for the System screen and the widgets. [none] is an unknown value. */
class StatsFormat(
    private val none: String,
) {
    private val oneDecimal = NumberFormat.getNumberInstance().apply { maximumFractionDigits = 1 }

    fun percent(v: Int): String = if (v < 0) none else "$v%"

    /** KiB/s as KB/s up to a thousand and MB/s past it. */
    fun rate(v: Int): String =
        when {
            v < 0 -> none
            v < KIB -> "$v KB/s"
            else -> "${oneDecimal.format(v / KIB.toDouble())} MB/s"
        }

    fun gib(mib: Int): String = "${oneDecimal.format(mib / KIB.toDouble())} GB"

    private companion object {
        const val KIB = 1024
    }
}
