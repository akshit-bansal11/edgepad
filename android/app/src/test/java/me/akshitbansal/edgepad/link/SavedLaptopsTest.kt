package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.InetAddress

class SavedLaptopsTest {
    private val hex = "00112233445566778899aabbccddeeff"
    private val net = NET_KEY + hex
    private val bluetooth = "AA:BB:CC:DD:EE:FF"

    private fun wifi(
        hosts: List<String> = listOf("192.168.1.5"),
        name: String = "Desk",
    ) = SavedLaptop(net, Transport.WIFI, name, hosts = hosts, port = 47321)

    private fun found(transport: Transport) =
        Found(parseHex(hex)!!, "Desk", listOf(InetAddress.getByName("192.168.1.5")), 47321, transport)

    @Test
    fun aListRoundTrips() {
        val list =
            listOf(
                wifi(listOf("192.168.1.5", "fe80::1")),
                SavedLaptop(bluetooth, Transport.BLUETOOTH, "Old", btAddress = bluetooth, autoConnect = false),
            )
        assertEquals(list, decodeSaved(encodeSaved(list)))
    }

    @Test
    fun aNameCannotBreakTheFormat() {
        val list = listOf(wifi(name = "a\tb\nc"))
        assertEquals("a b c", decodeSaved(encodeSaved(list)).single().name)
    }

    @Test
    fun malformedLinesAreSkippedNotFatal() {
        val good = encodeSaved(listOf(wifi()))
        val noId = "\tWIFI\tn\t\t\t1\t1"
        val noKind = "$net\tCARRIER_PIGEON\tn\t\t\t1\t1"
        val noPort = "$net\tWIFI\tn\t\t\tport\t1"
        val text = listOf("nonsense", "", good, noId, noKind, noPort).joinToString("\n")
        assertEquals(listOf(wifi()), decodeSaved(text))
    }

    @Test
    fun anEmptyStringIsAnEmptyList() {
        assertTrue(decodeSaved("").isEmpty())
    }

    @Test
    fun aHostThatWouldNeedALookupIsDropped() {
        val text = encodeSaved(listOf(wifi(listOf("laptop.local", "192.168.1.5"))))
        assertEquals(listOf("192.168.1.5"), decodeSaved(text).single().hosts)
    }

    @Test
    fun theOldSingleLaptopBecomesOneBluetoothEntry() {
        val entry = decodeSaved(migrateSaved("", bluetooth)).single()
        assertEquals(bluetooth, entry.id)
        assertEquals(bluetooth, entry.btAddress)
        assertEquals(Transport.BLUETOOTH, entry.kind)
        assertTrue(entry.autoConnect)
    }

    @Test
    fun migrationKeepsWhatIsThereAndAddsNoSecondCopy() {
        val once = migrateSaved(encodeSaved(listOf(wifi())), bluetooth)
        assertEquals(2, decodeSaved(once).size)
        assertEquals(once, migrateSaved(once, bluetooth))
    }

    @Test
    fun rememberingTheSameLaptopRenamesItAndKeepsItsAddresses() {
        val entry = SavedLaptop(net, Transport.USB, "Renamed")
        val merged = rememberLaptop(listOf(wifi()), entry, first = false).single()
        assertEquals("Renamed", merged.name)
        assertEquals(Transport.USB, merged.kind)
        assertEquals(listOf("192.168.1.5"), merged.hosts)
        assertEquals(47321, merged.port)
    }

    @Test
    fun rememberingANewLaptopAddsItAtTheEndOrTheFront() {
        val other = SavedLaptop(bluetooth, Transport.BLUETOOTH, "Old", btAddress = bluetooth)
        assertEquals(listOf(wifi(), other), rememberLaptop(listOf(wifi()), other, first = false))
        assertEquals(listOf(other, wifi()), rememberLaptop(listOf(wifi()), other, first = true))
    }

    @Test
    fun theActiveLaptopGoesFirst() {
        val other = SavedLaptop(bluetooth, Transport.BLUETOOTH, "Old", btAddress = bluetooth)
        assertEquals(listOf(other, wifi()), promoted(listOf(wifi(), other), setOf(bluetooth)))
    }

    @Test
    fun forgettingRemovesOnlyThatLaptop() {
        val other = SavedLaptop(bluetooth, Transport.BLUETOOTH, "Old", btAddress = bluetooth)
        assertEquals(listOf(wifi()), forgotten(listOf(wifi(), other), setOf(bluetooth)))
    }

    @Test
    fun aNetworkTargetNeedsAnIdAndAnAddress() {
        assertEquals(net, wifi().networkTarget()?.key)
        assertNull(wifi(hosts = emptyList()).networkTarget())
        assertNull(SavedLaptop(bluetooth, Transport.BLUETOOTH, "Old", btAddress = bluetooth).networkTarget())
    }

    @Test
    fun oneLaptopOnWifiAndUsbIsOneRow() {
        val rows =
            laptopRows(listOf(wifi()), emptyList(), emptyList(), listOf(found(Transport.WIFI), found(Transport.USB)))
        val row = rows.single()
        assertEquals(listOf(Transport.WIFI, Transport.USB), row.reach)
        assertEquals(Transport.WIFI, row.via)
        assertEquals(RowStatus.REMEMBERED, row.status)
    }

    @Test
    fun aBondedLaptopAndAFoundLaptopAreTwoRowsAsNothingProvesThemOne() {
        val rows = laptopRows(emptyList(), emptyList(), listOf(bluetooth to "Desk"), listOf(found(Transport.WIFI)))
        assertEquals(2, rows.size)
    }

    @Test
    fun aRowFallsBackWhenItsLastTransportIsGone() {
        val saved = listOf(wifi(hosts = emptyList()))
        val row = laptopRows(saved, emptyList(), emptyList(), listOf(found(Transport.USB))).single()
        assertEquals(Transport.USB, row.via)
    }

    @Test
    fun aRowPrefersTheBondedNameOverAMigratedAddress() {
        val saved = listOf(SavedLaptop(bluetooth, Transport.BLUETOOTH, bluetooth, btAddress = bluetooth))
        assertEquals("Desk", laptopRows(saved, emptyList(), listOf(bluetooth to "Desk"), emptyList()).single().name)
    }

    @Test
    fun aRemoteLaptopNotAutoConnectingRestsAsNothing() {
        val row = laptopRows(listOf(wifi().copy(autoConnect = false)), emptyList(), emptyList(), emptyList()).single()
        assertEquals(RowStatus.NONE, row.status)
        assertFalse(row.reach.isEmpty())
    }
}
