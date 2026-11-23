package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PhoneStatsTest {
    @Test
    fun bandFromFrequency() {
        assertEquals(NetBand.GHZ_2_4, PhoneStatsFormat.bandOf(2412))
        assertEquals(NetBand.GHZ_5, PhoneStatsFormat.bandOf(5180))
        assertEquals(NetBand.GHZ_5, PhoneStatsFormat.bandOf(5895))
        assertEquals(NetBand.GHZ_6, PhoneStatsFormat.bandOf(5955))
        assertEquals(NetBand.GHZ_6, PhoneStatsFormat.bandOf(7125))
        assertEquals(NetBand.UNKNOWN, PhoneStatsFormat.bandOf(5900))
        assertEquals(NetBand.UNKNOWN, PhoneStatsFormat.bandOf(-1))
    }

    @Test
    fun signalFromRssi() {
        assertEquals(SignalLevel.EXCELLENT, PhoneStatsFormat.signalOf(-40))
        assertEquals(SignalLevel.GOOD, PhoneStatsFormat.signalOf(-60))
        assertEquals(SignalLevel.FAIR, PhoneStatsFormat.signalOf(-75))
        assertEquals(SignalLevel.WEAK, PhoneStatsFormat.signalOf(-90))
        assertEquals(SignalLevel.UNKNOWN, PhoneStatsFormat.signalOf(-127))
        assertEquals(SignalLevel.UNKNOWN, PhoneStatsFormat.signalOf(0))
    }

    @Test
    fun thermalNamesFollowTheStatusNumbers() {
        assertEquals(ThermalLevel.NONE, PhoneStatsFormat.thermalOf(0))
        assertEquals(ThermalLevel.SEVERE, PhoneStatsFormat.thermalOf(3))
        assertEquals(ThermalLevel.SHUTDOWN, PhoneStatsFormat.thermalOf(6))
        assertEquals(ThermalLevel.UNKNOWN, PhoneStatsFormat.thermalOf(-1))
        assertEquals(ThermalLevel.UNKNOWN, PhoneStatsFormat.thermalOf(7))
    }

    @Test
    fun nothingIsRegisteredUntilObservedAndEverythingIsClosedOnStop() {
        var opened = 0
        var closed = 0
        val registration = {
            opened++
            AutoCloseable { closed++ }
        }
        val observation = Observation(listOf(registration, registration, registration))
        assertEquals(0, opened)
        assertFalse(observation.observing)

        observation.observe()
        observation.observe()
        assertEquals(3, opened)
        assertTrue(observation.observing)

        observation.stop()
        observation.stop()
        assertEquals(3, closed)
        assertFalse(observation.observing)

        observation.observe()
        assertEquals(6, opened)
    }
}
