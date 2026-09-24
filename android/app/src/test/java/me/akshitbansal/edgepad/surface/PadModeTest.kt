package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Test

/** The lock button's table, at the 300ms window a real phone's double-tap timeout hands it. */
class PadModeTest {
    private fun tap(
        mode: PadMode,
        now: Long,
        previousTap: Long,
    ): PadMode = mode.next(now, previousTap, WINDOW_MS)

    @Test
    fun aFirstTapFocusesWithoutWaitingToSeeWhetherASecondArrives() {
        // Long.MIN_VALUE is the never-tapped sentinel: the window is added to it, never subtracted from now.
        assertEquals(PadMode.FOCUS, tap(PadMode.NORMAL, now = 1_000, previousTap = Long.MIN_VALUE))
    }

    @Test
    fun aSecondTapInsideTheWindowLocksThePad() {
        assertEquals(PadMode.PAD_LOCKED, tap(PadMode.FOCUS, now = 1_200, previousTap = 1_000))
    }

    @Test
    fun theWindowsLastMillisecondStillCounts() {
        assertEquals(PadMode.PAD_LOCKED, tap(PadMode.FOCUS, now = 1_000 + WINDOW_MS, previousTap = 1_000))
    }

    @Test
    fun aTapAfterTheWindowPutsTheDialsBack() {
        assertEquals(PadMode.NORMAL, tap(PadMode.FOCUS, now = 1_000 + WINDOW_MS + 1, previousTap = 1_000))
    }

    @Test
    fun oneTapLeavesALockedPadAndItComesBackWhole() {
        // Not back to focus: a locked pad unlocks to a working surface, never to a half-mode nobody asked for.
        assertEquals(PadMode.NORMAL, tap(PadMode.PAD_LOCKED, now = 1_010, previousTap = 1_000))
        assertEquals(PadMode.NORMAL, tap(PadMode.PAD_LOCKED, now = 9_000, previousTap = 1_000))
    }

    companion object {
        private const val WINDOW_MS = 300L
    }
}
