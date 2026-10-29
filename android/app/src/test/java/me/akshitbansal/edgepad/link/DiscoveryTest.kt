package me.akshitbansal.edgepad.link

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.net.InetAddress

class DiscoveryTest {
    private val id = "00112233445566778899aabbccddeeff"

    private fun txt(vararg pairs: Pair<String, String?>): Map<String, ByteArray?> =
        pairs.associate { (k, v) -> k to v?.toByteArray() }

    private fun found(
        idHex: String,
        name: String,
        transport: Transport,
        vararg hosts: String,
    ) = Found(parseHex(idHex)!!, name, hosts.map(InetAddress::getByName), 47321, transport)

    @Test
    fun aRecordWithAnIdIsParsedAndItsVersionRecorded() {
        val parsed = parseTxt(txt("id" to id, "n" to "Desk", "v" to "5", "lv" to "1"))!!
        assertArrayEquals(parseHex(id), parsed.laptopId)
        assertEquals("Desk", parsed.name)
        assertEquals(5, parsed.version)
    }

    @Test
    fun aRecordWithoutAnIdIsIgnored() {
        assertNull(parseTxt(txt("n" to "Desk", "v" to "5")))
        assertNull(parseTxt(txt("id" to null)))
    }

    @Test
    fun aBadIdIsIgnored() {
        assertNull(parseTxt(txt("id" to "zz112233445566778899aabbccddeeff")))
        assertNull(parseTxt(txt("id" to "0011")))
        assertNull(parseTxt(txt("id" to "+0112233445566778899aabbccddeeff")))
        assertNull(parseTxt(txt("id" to "${id}00")))
    }

    @Test
    fun aMissingNameOrVersionIsNull() {
        val parsed = parseTxt(txt("id" to id, "v" to "five"))!!
        assertNull(parsed.name)
        assertNull(parsed.version)
    }

    @Test
    fun theSameLaptopAnnouncedTwiceIsOneRowWithTheFirstAnnouncementsAddressesOnly() {
        // The second could be anything on the LAN announcing the laptop's id: its address must not join the row.
        val rows =
            mergeFound(
                listOf(
                    found(id, "Desk", Transport.WIFI, "192.168.1.5", "10.0.0.5"),
                    found(id, "Desk", Transport.WIFI, "192.168.1.66"),
                ),
            )
        assertEquals(1, rows.size)
        assertEquals(listOf("192.168.1.5", "10.0.0.5"), rows[0].hosts.map { it.hostAddress })
    }

    @Test
    fun theSameLaptopOnWifiAndUsbIsOneRowPerTransportEachMarkedWithTheOther() {
        val all =
            mergeFound(
                listOf(
                    found(id, "Desk", Transport.USB, "192.168.42.2"),
                    found(id, "Desk", Transport.WIFI, "192.168.1.5"),
                ),
            )
        assertEquals(listOf(Transport.WIFI, Transport.USB), all.map { it.transport })
        assertEquals(Transport.USB, otherTransport(all[0], all))
        assertEquals(Transport.WIFI, otherTransport(all[1], all))
    }

    @Test
    fun aLaptopOnOneTransportHasNoOther() {
        val all = mergeFound(listOf(found(id, "Desk", Transport.WIFI, "192.168.1.5")))
        assertNull(otherTransport(all[0], all))
    }

    @Test
    fun rowsAreSortedByNameIgnoringCase() {
        val rows =
            mergeFound(
                listOf(
                    found("ff112233445566778899aabbccddeeff", "zed", Transport.WIFI, "192.168.1.9"),
                    found(id, "Alpha", Transport.WIFI, "192.168.1.5"),
                ),
            )
        assertEquals(listOf("Alpha", "zed"), rows.map { it.name })
    }
}
