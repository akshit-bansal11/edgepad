package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SpeedStripTest {
    private fun result(state: SpeedState) = SpeedResult(state, 94_200, 38_100, 9, 2, 29_800_000, 96_000_000)

    @Test
    fun noResultIsIdleAndTheButtonStartsATest() {
        assertEquals(StripKind.IDLE, SpeedStrip.kind(null))
        assertFalse(SpeedStrip.running(null))
    }

    @Test
    fun aRunningTestShowsStop() {
        assertEquals(StripKind.RUNNING, SpeedStrip.kind(result(SpeedState.RUNNING)))
        assertTrue(SpeedStrip.running(result(SpeedState.RUNNING)))
    }

    @Test
    fun anEndedTestOffersTestSpeedAgain() {
        for (state in listOf(SpeedState.DONE, SpeedState.CAPPED, SpeedState.FAILED, SpeedState.CANCELLED)) {
            assertFalse(SpeedStrip.running(result(state)))
        }
    }

    @Test
    fun theResultLineIsShownForDoneAndCappedAndAWordForTheRest() {
        assertEquals(StripKind.DONE, SpeedStrip.kind(result(SpeedState.DONE)))
        assertEquals(StripKind.DONE, SpeedStrip.kind(result(SpeedState.CAPPED)))
        assertEquals(StripKind.FAILED, SpeedStrip.kind(result(SpeedState.FAILED)))
        assertEquals(StripKind.STOPPED, SpeedStrip.kind(result(SpeedState.CANCELLED)))
    }
}
