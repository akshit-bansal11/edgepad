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
    fun readsTheRefreshRatesTheLaptopOffers() {
        assertTrue(state.take(Frame.Text(TextKind.REFRESH_RATES.id, "60/120/144")))
        assertEquals(listOf(60, 120, 144), state.refreshRates)
    }

    @Test
    fun aRateListWithAnUnreadableEntryIsNoList() {
        state.take(Frame.Text(TextKind.REFRESH_RATES.id, "60/120/144"))
        // Dropping the bad entry would slide every index after it onto the wrong rate.
        state.take(Frame.Text(TextKind.REFRESH_RATES.id, "60//144"))
        assertEquals(emptyList<Int>(), state.refreshRates)
    }

    @Test
    fun readsTheMacroNamesTheLaptopOffers() {
        assertTrue(state.take(Frame.Text(TextKind.MACROS.id, "Chrome/Spotify/ Notes ")))
        assertEquals(listOf("Chrome", "Spotify", "Notes"), state.macros)
    }

    @Test
    fun aBlankMacroNameKeepsItsSlot() {
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome//Notes"))
        // Dropping the empty name would send index 1 for Notes, and the laptop would launch slot 1 instead.
        assertEquals(listOf("Chrome", "", "Notes"), state.macros)
        assertEquals(2, state.macros.indexOf("Notes"))
    }

    @Test
    fun noMacrosAtAllIsAnEmptyList() {
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome/Spotify"))
        state.take(Frame.Text(TextKind.MACROS.id, ""))
        assertEquals(emptyList<String>(), state.macros)
    }

    @Test
    fun namesPastTheReservedBlockAreDropped() {
        // ACTION 64..95 is 32 wide; a 33rd name has no action id that could run it.
        state.take(Frame.Text(TextKind.MACROS.id, (1..40).joinToString("/") { "M$it" }))
        assertEquals(32, state.macros.size)
        assertEquals("M32", state.macros.last())
    }

    @Test
    fun framesThatAreNotStateAreNotKept() {
        assertFalse(state.take(Frame.Pong(1)))
        assertFalse(state.take(Frame.Text(7, "x")))
    }
}
