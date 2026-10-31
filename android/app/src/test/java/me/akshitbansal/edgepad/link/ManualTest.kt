package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ManualTest {
    private fun parsed(text: String): String? = parseHostPort(text)?.let { "${it.host}|${it.port}" }

    @Test
    fun aHostWithoutAPortGetsTheDefault() {
        assertEquals("192.168.1.20|47321", parsed("192.168.1.20"))
        assertEquals("desk.local|47321", parsed("  desk.local "))
    }

    @Test
    fun aPortAfterAColonIsTaken() {
        assertEquals("192.168.1.20|5000", parsed("192.168.1.20:5000"))
        assertEquals("desk|65535", parsed("desk:65535"))
    }

    @Test
    fun anIpv6LiteralNeedsBracketsToCarryAPort() {
        assertEquals("fe80::1|47321", parsed("fe80::1"))
        assertEquals("::1|47321", parsed("[::1]"))
        assertEquals("::1|6000", parsed("[::1]:6000"))
    }

    @Test
    fun aPortOutOfRangeOrNotANumberIsRejected() {
        assertNull(parsed("desk:0"))
        assertNull(parsed("desk:65536"))
        assertNull(parsed("desk:80x"))
        assertNull(parsed("desk:"))
        assertNull(parsed("[::1]:99999"))
    }

    @Test
    fun nonsenseIsRejected() {
        assertNull(parsed(""))
        assertNull(parsed("   "))
        assertNull(parsed(":5000"))
        assertNull(parsed("[::1"))
        assertNull(parsed("[::1]x"))
        assertNull(parsed("[host]"))
        assertNull(parsed("two words"))
        assertNull(parsed("http://desk"))
    }
}
