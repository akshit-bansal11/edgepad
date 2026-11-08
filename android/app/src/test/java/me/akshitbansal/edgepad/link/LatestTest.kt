package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.TextKind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class LatestTest {
    private val queue = ArrayList<Runnable>()
    private val delivered = ArrayList<Frame>()
    private val latest = Latest({ queue.add(it) }, { delivered.add(it) })

    private fun drain() {
        val run = ArrayList(queue)
        queue.clear()
        run.forEach(Runnable::run)
    }

    @Test
    fun aBurstOfOneKindIsOnePostCarryingTheNewest() {
        for (value in 1..50) assertTrue(latest.offer(Frame.StateReport(ControlId.VOLUME.id, value, 0)))
        assertEquals(1, queue.size)
        drain()
        assertEquals(listOf<Frame>(Frame.StateReport(ControlId.VOLUME.id, 50, 0)), delivered)
    }

    @Test
    fun kindsDoNotShareASlotAndASlotCanPostAgainOnceDrained() {
        latest.offer(Frame.StateReport(ControlId.VOLUME.id, 1, 0))
        latest.offer(Frame.StateReport(ControlId.BRIGHTNESS.id, 2, 0))
        latest.offer(Frame.Text(TextKind.NOW_PLAYING.id, "a"))
        latest.offer(Frame.Text(TextKind.NOW_PLAYING.id, "b"))
        assertEquals(3, queue.size)
        drain()
        assertEquals(3, delivered.size)
        latest.offer(Frame.Text(TextKind.NOW_PLAYING.id, "c"))
        assertEquals(1, queue.size)
        drain()
        assertEquals(Frame.Text(TextKind.NOW_PLAYING.id, "c"), delivered.last())
    }

    @Test
    fun oneShotFramesAreNeverTaken() {
        val oneShots =
            listOf(
                Frame.Text(TextKind.PAD_STATUS.id, "ready"),
                Frame.Text(TextKind.CAPTURE_STATUS.id, "failed"),
                Frame.Text(TextKind.MACROS.id, "a/b"),
                Frame.Text(TextKind.MACRO_OPEN.id, "01"),
                Frame.Text(TextKind.DEVICE.id, "x"),
                Frame.Text(TextKind.INPUT_STATE.id, "blocked"),
                Frame.HelloAck(5),
                Frame.Pong(1),
            )
        for (frame in oneShots) assertFalse(frame.toString(), latest.offer(frame))
        assertTrue(queue.isEmpty())
    }
}
