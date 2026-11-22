package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class NetSummaryTest {
    @Test
    fun anUnknownSignalIsUnknownAndKnownOnesFallIntoFourLevels() {
        assertEquals(SignalLevel.UNKNOWN, NetFormat.signal(-1))
        assertEquals(SignalLevel.WEAK, NetFormat.signal(0))
        assertEquals(SignalLevel.FAIR, NetFormat.signal(25))
        assertEquals(SignalLevel.GOOD, NetFormat.signal(50))
        assertEquals(SignalLevel.EXCELLENT, NetFormat.signal(75))
        assertEquals(SignalLevel.EXCELLENT, NetFormat.signal(100))
    }

    @Test
    fun linkSpeedsFromAThousandReadInGbps() {
        assertFalse(NetFormat.inGbps(433))
        assertFalse(NetFormat.inGbps(999))
        assertTrue(NetFormat.inGbps(1000))
        assertTrue(NetFormat.inGbps(2500))
    }

    @Test
    fun noNetworkNameIsKeptAnywhere() {
        val classes = listOf(NetInfo::class.java, PhoneNet::class.java, PhoneSnapshot::class.java)
        val fields = classes.flatMap { it.declaredFields.toList() }.map { it.name.lowercase() }
        assertTrue(fields.none { "ssid" in it || "bssid" in it || it == "name" || "network" in it })
    }
}
