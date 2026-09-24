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
    fun aSetNeverJumpsAheadOfAnActionBetween() {
        // Volume 10, mute, volume 12: the mute must still land between the two volumes' effect.
        val frames = listOf(Frame.SetValue(0, 10), Frame.RunAction(1), Frame.SetValue(0, 12))
        val merged = Coalesce.merge(frames)
        assertEquals(listOf(Frame.RunAction(1), Frame.SetValue(0, 12)), merged)
    }

    @Test
    fun onlyTheNewestPadStateSurvives() {
        val newest = Frame.PadState(0x1000, 0, 255, 100, -100, 0, 0)
        val merged =
            Coalesce.merge(
                listOf(
                    Frame.PadState(0, 0, 0, 0, 0, 0, 0),
                    Frame.PadState(0x1000, 0, 120, 50, -50, 0, 0),
                    newest,
                ),
            )
        assertEquals(listOf(newest), merged)
    }

    @Test
    fun aPadStateDoesNotSwallowTheKeysAroundIt() {
        // A key is an edge and a pad state is a snapshot: collapsing the run must leave both key frames
        // exactly where they were, or the laptop is left holding a key nobody is pressing.
        val frames =
            listOf(
                Frame.Key(0x41, true),
                Frame.PadState(0, 0, 0, 0, 0, 0, 0),
                Frame.Key(0x41, false),
                Frame.PadState(0x1000, 0, 0, 0, 0, 0, 0),
            )
        val merged = Coalesce.merge(frames)
        assertEquals(listOf(frames[0], frames[2], frames[3]), merged)
    }

    @Test
    fun repeatedKeysAreNeverCollapsedIntoOne() {
        val frames = listOf(Frame.Key(0x41, true), Frame.Key(0x41, false), Frame.Key(0x41, true))
        assertEquals(frames, Coalesce.merge(frames))
    }

    @Test
    fun zoomsAreSummedToo() {
        assertEquals(listOf(Frame.Zoom(240)), Coalesce.merge(listOf(Frame.Zoom(120), Frame.Zoom(120))))
    }
}
