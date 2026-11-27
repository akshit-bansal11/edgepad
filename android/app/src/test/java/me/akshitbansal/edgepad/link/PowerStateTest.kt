package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.PowerStateToken
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class PowerStateTest {
    @Test
    fun parsesARestartAndAShutDown() {
        assertEquals(PowerState(PowerStateToken.RESTART, 60, 5L), PowerState.parse("restart/60", 5L))
        assertEquals(PowerState(PowerStateToken.SHUT_DOWN, 7, 5L), PowerState.parse("shutdown/7", 5L))
    }

    @Test
    fun emptyOrUnknownIsNone() {
        assertNull(PowerState.parse("", 0L))
        assertNull(PowerState.parse("reboot/3", 0L))
        assertNull(PowerState.parse("restart/x", 0L))
        assertNull(PowerState.parse("restart", 0L))
    }

    @Test
    fun countsDownAndClamps() {
        val state = PowerState.parse("shutdown/60", 1_000L)!!
        assertEquals(60, state.secondsLeft(1_000L))
        assertEquals(59, state.secondsLeft(2_000L))
        assertEquals(1, state.secondsLeft(60_999L))
        assertEquals(0, state.secondsLeft(61_000L))
        assertEquals(0, state.secondsLeft(90_000L))
        assertEquals(60, state.secondsLeft(0L))
    }
}
