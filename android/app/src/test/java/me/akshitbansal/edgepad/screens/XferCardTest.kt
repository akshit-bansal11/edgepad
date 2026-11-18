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
    fun everyFailureHasAReasonOfItsOwn() {
        val reasons = XferFailure.entries.map(::reasonRes)
        assertEquals(XferFailure.entries.size, reasons.toSet().size)
    }

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
    fun oneFailureBesideOneArrivedIsPartialNotFailedOne() {
        val finished = base.copy(phase = XferPhase.FINISHED, sending = true, done = 1, failed = failed(1))
        assertEquals(CardTitle.DONE_SOME, titleKind(finished))
    }
}
