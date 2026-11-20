package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Test

class CompactNumberTest {
    @Test
    fun table() {
        val cases =
            listOf(
                0L to "0",
                999L to "999",
                1000L to "1K",
                1500L to "1.5K",
                999_999L to "999.9K",
                1_900_000L to "1.9M",
                12_340_000_000L to "12.3B",
                -5L to "0",
            )
        for ((n, expected) in cases) assertEquals("$n", expected, CompactNumber.format(n))
    }

    @Test
    fun decimalSeparatorIsTheCallers() {
        assertEquals("1,5K", CompactNumber.format(1500, ','))
    }
}
