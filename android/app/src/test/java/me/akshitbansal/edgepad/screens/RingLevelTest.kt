package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Test

class RingLevelTest {
    @Test
    fun levelsChangeAtFiftySeventyFiveAndNinety() {
        val expected = mapOf(0 to 0, 49 to 0, 50 to 1, 74 to 1, 75 to 2, 89 to 2, 90 to 3, 100 to 3)
        for ((pct, index) in expected) assertEquals("pct $pct", index, RingLevel.colourIndex(pct))
    }
}
