package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.TextKind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class LaptopStateTest {
    private val state = LaptopState()

    @Test
    fun keepsTheLatestLevelAndFlagPerControl() {
        assertNull(state.level(ControlId.VOLUME))
        assertTrue(state.take(Frame.StateReport(ControlId.VOLUME.id, 42, 1)))
        assertEquals(42, state.level(ControlId.VOLUME))
        assertTrue(state.flag(ControlId.VOLUME))
        state.take(Frame.StateReport(ControlId.VOLUME.id, 43, 0))
        assertEquals(43, state.level(ControlId.VOLUME))
        assertFalse(state.flag(ControlId.VOLUME))
    }

    @Test
    fun anUnknownControlIsNotKept() {
        assertFalse(state.take(Frame.StateReport(9, 1, 0)))
    }

    @Test
    fun readsWhatIsPlayingAndWhere() {
        state.take(Frame.Text(TextKind.NOW_PLAYING.id, "Polygon Window · Aphex Twin"))
        state.take(Frame.Text(TextKind.APP.id, "Spotify"))
        state.take(Frame.Text(TextKind.TIMELINE.id, "84/227"))
        assertEquals("Polygon Window · Aphex Twin", state.nowPlaying)
        assertEquals("Spotify", state.app)
        assertEquals(84, state.position)
        assertEquals(227, state.duration)
    }

    @Test
    fun aTimelineThatDoesNotParseMeansNone() {
        state.take(Frame.Text(TextKind.TIMELINE.id, "84/227"))
        state.take(Frame.Text(TextKind.TIMELINE.id, ""))
        assertEquals(-1, state.position)
        assertEquals(0, state.duration)
        state.take(Frame.Text(TextKind.TIMELINE.id, "5/0"))
        assertEquals(0, state.duration)
    }

    @Test
    fun framesThatAreNotStateAreNotKept() {
        assertFalse(state.take(Frame.Pong(1)))
        assertFalse(state.take(Frame.Text(7, "x")))
    }
}
