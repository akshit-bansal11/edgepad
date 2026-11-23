package me.akshitbansal.edgepad.update

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class UpdateChoiceTest {
    @Test
    fun aSkippedVersionIsNeverShown() {
        assertFalse(UpdateChoice.shouldShow("5.0.0", 0L, "5.0.0", 1_000L))
    }

    @Test
    fun aNewerVersionThanTheSkippedOneIsShown() {
        assertTrue(UpdateChoice.shouldShow("5.0.0", 0L, "5.0.1", 1_000L))
    }

    @Test
    fun laterHidesUntilTheNextDailyCheck() {
        val until = 1_000L + Updates.CHECK_EVERY_MS
        assertFalse(UpdateChoice.shouldShow(null, until, "5.0.0", until - 1))
        assertTrue(UpdateChoice.shouldShow(null, until, "5.0.0", until))
    }

    @Test
    fun withNoChoiceTheNoticeIsShown() {
        assertTrue(UpdateChoice.shouldShow(null, 0L, "5.0.0", 1L))
    }
}
