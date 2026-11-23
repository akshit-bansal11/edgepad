package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Test

class TrafficRateTest {
    @Test
    fun aSecondOfBytesIsKibPerSecond() {
        assertEquals(100, TrafficRate.kibPerSecond(1_000_000, 1_102_400, 1_000))
    }

    @Test
    fun theElapsedTimeScalesTheRate() {
        assertEquals(100, TrafficRate.kibPerSecond(0, 204_800, 2_000))
        assertEquals(200, TrafficRate.kibPerSecond(0, 102_400, 500))
    }

    @Test
    fun nothingMovedIsZeroNotUnknown() {
        assertEquals(0, TrafficRate.kibPerSecond(500, 500, 1_000))
    }

    @Test
    fun unsupportedResetOrNoTimeIsUnknown() {
        assertEquals(-1, TrafficRate.kibPerSecond(-1, 5_000, 1_000))
        assertEquals(-1, TrafficRate.kibPerSecond(0, -1, 1_000))
        assertEquals(-1, TrafficRate.kibPerSecond(9_000, 1_000, 1_000))
        assertEquals(-1, TrafficRate.kibPerSecond(0, 1_000, 0))
    }
}
