package me.akshitbansal.edgepad

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SoundRulesTest {
    private val silent = 0
    private val vibrate = 1
    private val normal = 2

    @Test
    fun playsOnlyWithTheSwitchOnAndTheRingerNormal() {
        assertTrue(SoundRules.allowed(true, normal))
        assertFalse(SoundRules.allowed(false, normal))
    }

    @Test
    fun silentAndVibrateAlwaysWin() {
        assertFalse(SoundRules.allowed(true, silent))
        assertFalse(SoundRules.allowed(true, vibrate))
    }

    @Test
    fun aFastSpinTicksNoMoreOftenThanTheGap() {
        val gate = SoundRules.TickGate(45L)
        val heard = (0L until 1000L step 5L).count { gate.accept(it) }
        // One step every 5 ms for a second: a tick each 45 ms from 0 is 23.
        assertTrue(heard == 23)
    }

    @Test
    fun theFirstStepAlwaysTicksAndASlowOneDoes() {
        val gate = SoundRules.TickGate(45L)
        assertTrue(gate.accept(0L))
        assertFalse(gate.accept(44L))
        assertTrue(gate.accept(90L))
    }
}
