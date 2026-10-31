package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.LinkedBlockingQueue

class PacedBatchTest {
    private fun batch(
        first: Frame,
        queued: List<Frame>,
        paceMs: Int,
    ): List<Frame> {
        val outbox = LinkedBlockingQueue<Frame>()
        outbox.addAll(queued)
        val into = ArrayList<Frame>()
        PacedBatch.collect(first, outbox, paceMs, System::nanoTime, into)
        return into
    }

    @Test
    fun noPaceIsTheOldBatch() {
        val key = Frame.Key(30, true)
        val started = System.nanoTime()
        val got = batch(Frame.Move(1, 1), listOf(Frame.Move(2, 2), key), 0)
        assertEquals(listOf(Frame.Move(1, 1), Frame.Move(2, 2), key), got)
        assertTrue(System.nanoTime() - started < 50_000_000L)
    }

    @Test
    fun aKeyBehindMovesEndsTheWaitAtOnce() {
        val key = Frame.Key(30, true)
        val started = System.nanoTime()
        val got = batch(Frame.Move(1, 1), listOf(Frame.Move(2, 2), key), 2_000)
        assertEquals(listOf(Frame.Move(1, 1), Frame.Move(2, 2), key), got)
        assertTrue(System.nanoTime() - started < 1_000_000_000L)
    }

    @Test
    fun aLoneMoveWaitsAboutThePace() {
        val started = System.nanoTime()
        val got = batch(Frame.Move(1, 1), emptyList(), 100)
        assertEquals(listOf(Frame.Move(1, 1)), got)
        assertTrue(System.nanoTime() - started >= 50_000_000L)
    }

    @Test
    fun aFirstFrameThatIsAnEdgeNeverWaits() {
        val started = System.nanoTime()
        val got = batch(Frame.Key(30, true), emptyList(), 2_000)
        assertEquals(listOf(Frame.Key(30, true)), got)
        assertTrue(System.nanoTime() - started < 1_000_000_000L)
    }

    @Test
    fun aPadStateNeverWaitsOnThePace() {
        val pad = Frame.PadState(1, 0, 0, 0, 0, 0, 0)
        val started = System.nanoTime()
        val got = batch(pad, emptyList(), 2_000)
        assertEquals(listOf(pad), got)
        assertTrue(System.nanoTime() - started < 1_000_000_000L)
    }

    @Test
    fun aKeyArrivingDuringTheWaitEndsIt() {
        val outbox = LinkedBlockingQueue<Frame>()
        val into = ArrayList<Frame>()
        val sender =
            Thread {
                Thread.sleep(50)
                outbox.put(Frame.Key(30, true))
            }
        val started = System.nanoTime()
        sender.start()
        PacedBatch.collect(Frame.Move(1, 1), outbox, 5_000, System::nanoTime, into)
        sender.join()
        assertEquals(listOf(Frame.Move(1, 1), Frame.Key(30, true)), into)
        assertTrue(System.nanoTime() - started < 2_500_000_000L)
    }

    @Test
    fun mergedMovesStillSumTheirTravel() {
        val got = batch(Frame.Move(1, 2), listOf(Frame.Move(3, 4), Frame.Move(5, 6)), 20)
        assertEquals(listOf(Frame.Move(9, 12)), Coalesce.merge(got))
    }
}
