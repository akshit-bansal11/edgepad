package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertEquals
import org.junit.Test

class CoalesceTest {
    @Test
    fun consecutiveMovesAreSummed() {
        val merged = Coalesce.merge(listOf(Frame.Move(1, 2), Frame.Move(3, -5), Frame.Move(-1, 0)))
        assertEquals(listOf(Frame.Move(3, -3)), merged)
    }

    @Test
    fun aClickBetweenMovesKeepsItsPlace() {
        val frames = listOf(Frame.Move(1, 1), Frame.PointerButton(0, true), Frame.Move(2, 2))
        assertEquals(frames, Coalesce.merge(frames))
    }

    @Test
    fun onlyTheLastSetPerControlSurvives() {
        val merged = Coalesce.merge(listOf(Frame.SetValue(0, 10), Frame.SetValue(1, 50), Frame.SetValue(0, 12)))
        assertEquals(listOf(Frame.SetValue(1, 50), Frame.SetValue(0, 12)), merged)
    }

    @Test
    fun aSumStaysWithinSixteenBits() {
        val merged = Coalesce.merge(listOf(Frame.Scroll(0, 30_000), Frame.Scroll(0, 30_000)))
        assertEquals(listOf(Frame.Scroll(0, 32_767)), merged)
    }

    @Test
    fun zoomsAreSummedToo() {
        assertEquals(listOf(Frame.Zoom(240)), Coalesce.merge(listOf(Frame.Zoom(120), Frame.Zoom(120))))
    }
}
