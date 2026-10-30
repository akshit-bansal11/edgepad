package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.net.LinkWire
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.atomic.AtomicInteger
import kotlin.concurrent.thread
import kotlin.system.measureTimeMillis

class TetherProbeTest {
    private val loopback = InetAddress.getLoopbackAddress()

    private fun hosts(
        address: String,
        prefix: Int,
    ) = hostsOf(InetAddress.getByName(address), prefix).mapNotNull { it.hostAddress }

    /** Calls a laptop's port [answer]s, the accepted connection's turn counting from 0; [test] gets the port. */
    private fun laptop(
        answer: (Int, Socket) -> Unit,
        test: (Int) -> Unit,
    ) {
        ServerSocket(0, 8, loopback).use { server ->
            val turn = AtomicInteger()
            thread(isDaemon = true) {
                try {
                    while (true) {
                        val socket = server.accept()
                        val mine = turn.getAndIncrement()
                        thread(isDaemon = true) { hearOut(socket) { answer(mine, socket) } }
                    }
                } catch (e: IOException) {
                    // The test is over and closed the server.
                }
            }
            test(server.localPort)
        }
    }

    /** Takes the INFO request off the wire, so closing afterwards cannot reset the reply away. */
    private fun hearOut(
        socket: Socket,
        then: () -> Unit,
    ) {
        try {
            socket.use {
                LinkWire.read(it.getInputStream())
                then()
            }
        } catch (e: IOException) {
            // The probe hung up, which is what these tests are for.
        }
    }

    /** A frame that promises a body and delivers a byte of it every so often, keeping each read within its timeout. */
    private fun drip(socket: Socket) {
        val out = socket.getOutputStream()
        out.write(byteArrayOf(LinkWire.MAX_MESSAGE.toByte(), (LinkWire.MAX_MESSAGE shr 8).toByte()))
        repeat(LinkWire.MAX_MESSAGE) {
            out.write(0)
            out.flush()
            Thread.sleep(DRIP_MS)
        }
    }

    private fun reply(
        socket: Socket,
        name: String,
    ) {
        val body = byteArrayOf(0) + ByteArray(LinkWire.ID_LENGTH) + byteArrayOf(4) + LinkWire.nameField(name)
        LinkWire.write(socket.getOutputStream(), body)
    }

    @Test
    fun aDrippingHostCostsOnlyTheDeadlineAndAnEarlierAnswerIsKeptWithItsNameCapped() {
        laptop({ turn, socket -> if (turn == 0) reply(socket, "x".repeat(100)) else drip(socket) }) { port ->
            val twice = List(2) { loopback to Transport.WIFI }
            var found = emptyList<Found>()
            val took = measureTimeMillis { found = TetherProbe().askAll(twice, port, 500) }
            assertTrue("took $took ms", took < WELL_PAST_THE_DEADLINE_MS)
            assertEquals(listOf("x".repeat(MAX_LAPTOP_NAME)), found.map { it.name })
        }
    }

    @Test
    fun stopEndsABlockedRead() {
        laptop({ _, socket -> drip(socket) }) { port ->
            val probe = TetherProbe()
            thread(isDaemon = true) {
                Thread.sleep(200)
                probe.stop()
            }
            val took =
                measureTimeMillis {
                    assertEquals(emptyList<Found>(), probe.askAll(listOf(loopback to Transport.USB), port, 60_000))
                }
            assertTrue("took $took ms", took < WELL_PAST_THE_DEADLINE_MS)
        }
    }

    @Test
    fun aProbeStoppedBeforeItRanAsksNobody() {
        var calls = 0
        laptop({ _, _ -> calls++ }) { port ->
            val probe = TetherProbe()
            probe.stop()
            assertEquals(emptyList<Found>(), probe.askAll(listOf(loopback to Transport.WIFI), port, 500))
        }
        assertEquals(0, calls)
    }

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
    fun interfacesAreClassifiedByName() {
        listOf("rndis0", "usb0", "ncm0", "eth0").forEach { assertEquals(it, Transport.USB, transportOfInterface(it)) }
        listOf("wlan0", "wifi0", "ap0", "swlan0").forEach { assertEquals(it, Transport.WIFI, transportOfInterface(it)) }
        listOf("lo", "rmnet_data0", "dummy0", "tun0").forEach { assertNull(it, transportOfInterface(it)) }
    }

    @Test
    fun wifiIsSweptOnlyWhileMdnsFoundNothingAndUsbAlways() {
        assertTrue(shouldSweep(Transport.WIFI, mdnsFound = false))
        assertFalse(shouldSweep(Transport.WIFI, mdnsFound = true))
        assertTrue(shouldSweep(Transport.USB, mdnsFound = false))
        assertTrue(shouldSweep(Transport.USB, mdnsFound = true))
    }

    private companion object {
        const val DRIP_MS = 50L
        const val WELL_PAST_THE_DEADLINE_MS = 4_000L
    }
}
