package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.link.xfer.XferFailed
import me.akshitbansal.edgepad.link.xfer.XferFailure
import me.akshitbansal.edgepad.link.xfer.XferPhase
import me.akshitbansal.edgepad.link.xfer.XferSnapshot
import org.junit.Assert.assertEquals
import org.junit.Test
import java.util.Locale

class XferCardTest {
    private val base = XferSnapshot.IDLE

    private fun failed(n: Int) = List(n) { XferFailed("f$it", XferFailure.REFUSED) }

    @Test
    fun titleKinds() {
        val running = base.copy(phase = XferPhase.RUNNING, sending = true)
        assertEquals(CardTitle.RUNNING_SEND, titleKind(running))
        assertEquals(CardTitle.RUNNING_RECEIVE, titleKind(running.copy(sending = false)))
        val finished = running.copy(phase = XferPhase.FINISHED)
        assertEquals(CardTitle.DONE_ALL, titleKind(finished.copy(done = 5)))
        assertEquals(CardTitle.DONE_SOME, titleKind(finished.copy(done = 3, failed = failed(2))))
        assertEquals(CardTitle.FAILED_ONE, titleKind(finished.copy(failed = failed(1))))
        assertEquals(CardTitle.FAILED_MANY, titleKind(finished.copy(failed = failed(2))))
    }

    @Test
    fun buttonsFollowTheState() {
        val running = base.copy(phase = XferPhase.RUNNING, sending = true)
        assertEquals(CardButtons(hold = true, resume = false, stop = true, retry = false), buttonsOf(running))
        val held = CardButtons(hold = true, resume = true, stop = true, retry = false)
        assertEquals(held, buttonsOf(running.copy(paused = true)))
        val failedSend = running.copy(phase = XferPhase.FINISHED, failed = failed(1))
        assertEquals(CardButtons(hold = false, resume = false, stop = false, retry = true), buttonsOf(failedSend))
        val received = failedSend.copy(sending = false)
        assertEquals(CardButtons(hold = false, resume = false, stop = false, retry = false), buttonsOf(received))
    }

    @Test
    fun countShowsOnlyForABatchOfSeveral() {
        val running = base.copy(phase = XferPhase.RUNNING, sending = true, index = 2, total = 5)
        assertEquals(true, showsCount(running))
        assertEquals(false, showsCount(running.copy(total = 1)))
        assertEquals(false, showsCount(running.copy(index = 0)))
    }

    @Test
    fun sizesShowTheUnitOnceWhenShared() {
        assertEquals("12.4" to "48 MB", sizesOf(12_400_000, 48_000_000, Locale.US))
        assertEquals("980 KB" to "1.2 GB", sizesOf(980_000, 1_200_000_000, Locale.US))
        assertEquals("0" to "512 B", sizesOf(0, 512, Locale.US))
    }

    @Test
    fun batchFormMatchesTheDetail() {
        // "2 of 5 · 12.4 of 48 MB": the count is the pattern's, the figures are sizesOf's.
        val (sent, size) = sizesOf(12_400_000, 48_000_000, Locale.US)
        assertEquals("2 of 5 · $sent of $size", "%1\$d of %2\$d · %3\$s".format(2, 5, "$sent of $size"))
    }

    @Test
    fun oneFailureBesideOneArrivedIsPartialNotFailedOne() {
        val finished = base.copy(phase = XferPhase.FINISHED, sending = true, done = 1, failed = failed(1))
        assertEquals(CardTitle.DONE_SOME, titleKind(finished))
    }
}
