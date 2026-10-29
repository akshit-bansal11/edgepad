package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.InetAddress

class TetherProbeTest {
    private fun hosts(
        address: String,
        prefix: Int,
    ) = hostsOf(InetAddress.getByName(address), prefix).mapNotNull { it.hostAddress }

    @Test
    fun aSlash24IsEveryOtherHostButNotTheEnds() {
        val list = hosts("192.168.42.129", 24)
        assertEquals(253, list.size)
        assertEquals("192.168.42.1", list.first())
        assertEquals("192.168.42.254", list.last())
        assertFalse("192.168.42.129" in list)
        assertFalse("192.168.42.0" in list)
        assertFalse("192.168.42.255" in list)
    }

    @Test
    fun aSlash25IsOnlyItsOwnHalf() {
        val list = hosts("192.168.42.129", 25)
        assertEquals(125, list.size)
        assertEquals("192.168.42.130", list.first())
        assertEquals("192.168.42.254", list.last())
    }

    @Test
    fun aSlash16IsCutToTheSlash24AroundThePhone() {
        val list = hosts("10.1.7.20", 16)
        assertEquals(253, list.size)
        assertTrue(list.all { it.startsWith("10.1.7.") })
    }

    @Test
    fun aPointToPointSlash31IsJustTheOtherEnd() {
        assertEquals(listOf("10.0.0.1"), hosts("10.0.0.0", 31))
        assertEquals(emptyList<String>(), hosts("10.0.0.0", 32))
    }

    @Test
    fun anIpv6AddressHasNoHosts() {
        assertEquals(emptyList<String>(), hosts("fe80::1", 64))
    }

    @Test
    fun tetherInterfacesAreTheRndisUsbNcmApSwlanAndEthFamilies() {
        listOf("rndis0", "usb0", "ncm0", "ap0", "swlan0", "eth0").forEach { assertTrue(it, isTetherName(it)) }
        listOf("wlan0", "lo", "rmnet_data0", "dummy0", "tun0").forEach { assertFalse(it, isTetherName(it)) }
    }
}
