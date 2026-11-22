package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.link.xfer.XferFailed
import me.akshitbansal.edgepad.link.xfer.XferFailure
import me.akshitbansal.edgepad.link.xfer.XferPhase
import me.akshitbansal.edgepad.link.xfer.XferSnapshot
import org.junit.Assert.assertEquals
import org.junit.Test

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
    fun oneFailureBesideOneArrivedIsPartialNotFailedOne() {
        val finished = base.copy(phase = XferPhase.FINISHED, sending = true, done = 1, failed = failed(1))
        assertEquals(CardTitle.DONE_SOME, titleKind(finished))
    }
}
