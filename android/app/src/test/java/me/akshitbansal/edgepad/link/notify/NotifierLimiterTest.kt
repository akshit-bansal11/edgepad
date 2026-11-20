package me.akshitbansal.edgepad.link.notify

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class NotifierLimiterTest {
    private var clock = 0L
    private val limiter = NotifyLimiter { clock }

    @Test
    fun aDuplicateWithinTenSecondsIsDroppedThenAllowed() {
        assertTrue(limiter.allow(1))
        clock = 9_999
        assertFalse(limiter.allow(1))
        clock = 10_000
        assertTrue(limiter.allow(1))
    }

    @Test
    fun elevenInAMinuteStopAtTen() {
        for (i in 1..10) assertTrue(limiter.allow(i))
        assertFalse(limiter.allow(11))
        clock = 60_000
        assertTrue(limiter.allow(11))
    }
}
