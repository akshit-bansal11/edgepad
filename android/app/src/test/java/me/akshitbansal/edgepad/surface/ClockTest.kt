package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Test

class ClockTest {
    @Test
    fun formatsLikeAPlayer() {
        assertEquals("0:00", Clock.format(0))
        assertEquals("1:24", Clock.format(84))
        assertEquals("59:59", Clock.format(3599))
        assertEquals("1:02:05", Clock.format(3725))
    }

    @Test
    fun aNegativeTimeIsZero() {
        assertEquals("0:00", Clock.format(-3))
    }
}
