package me.akshitbansal.edgepad.update

import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.concurrent.thread

class UpdateJobTest {
    @After
    fun clear() = UpdateJob.finish()

    @Test
    fun aSecondBeginIsRefusedUntilTheFirstFinishes() {
        assertTrue(UpdateJob.begin())
        assertFalse(UpdateJob.begin())

        UpdateJob.finish()

        assertTrue(UpdateJob.begin())
    }

    @Test
    fun progressFromTheDownloadThreadIsSeenHere() {
        assertTrue(UpdateJob.begin())

        thread { UpdateJob.progress = 42 }.join()

        assertEquals(42, UpdateJob.progress)
    }
}
