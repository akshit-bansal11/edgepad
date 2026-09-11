package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class RttStatsTest {
    @Test
    fun isNaNUntilASampleArrives() {
        val stats = RttStats()
        assertTrue(stats.median().isNaN())
    }

    @Test
    fun medianOfAnOddCountIsTheMiddleSample() {
        val stats = RttStats()
        listOf(9.0, 1.0, 5.0).forEach(stats::add)
        assertEquals(5.0, stats.median(), 0.0)
    }

    @Test
    fun medianOfAnEvenCountAveragesTheMiddlePair() {
        val stats = RttStats()
        listOf(4.0, 1.0, 2.0, 8.0).forEach(stats::add)
        assertEquals(3.0, stats.median(), 0.0)
    }

    @Test
    fun oldSamplesLeaveTheWindow() {
        val stats = RttStats(window = 2)
        listOf(1.0, 10.0, 20.0).forEach(stats::add)
        assertEquals(15.0, stats.median(), 0.0)
    }

    @Test
    fun clearForgetsEverything() {
        val stats = RttStats()
        stats.add(3.0)
        stats.clear()
        assertTrue(stats.median().isNaN())
    }
}
