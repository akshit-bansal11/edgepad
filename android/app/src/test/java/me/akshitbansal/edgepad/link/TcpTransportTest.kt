package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.net.LinkInitiator
import me.akshitbansal.edgepad.link.net.LinkWire
import me.akshitbansal.edgepad.link.net.PairingKeys
import me.akshitbansal.edgepad.link.net.Sas
import me.akshitbansal.edgepad.link.net.SecureChannel
import me.akshitbansal.edgepad.link.net.Vectors
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.fail
import org.junit.Test
import java.io.DataInputStream
import java.io.IOException
import java.io.OutputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import kotlin.concurrent.thread

/**
 * The network transport against a laptop on a loopback socket that replays protocol/link-vectors.txt: what the
 * phone sends is checked byte for byte, and what the laptop sends is fixed in advance.
 */
class TcpTransportTest {
    private val phone = Vectors.phone()
    private val laptop = Vectors.laptop()
    private val phoneId = Vectors.bytes("PAIR_INPUT", "phoneId")
    private val laptopId = Vectors.bytes("PAIR_INPUT", "laptopId")
    private val nameP = Vectors.bytes("PAIR_INPUT", "nameP")
    private val nameL = Vectors.bytes("PAIR_INPUT", "nameL")
    private val magic = "EDGL".toByteArray()
    private val plain = "EDGL1 record".toByteArray()
    private val loopback = InetAddress.getLoopbackAddress()

    @Test(timeout = TIMEOUT_MS)
    fun infoThenAResumeReachTheEncryptedChannel() {
        val keys = FakeKeys().apply { save(laptopId, Vectors.bytes("RESUME_INPUT", "pk")) }
        ServerSocket(0, 2, loopback).use { server ->
            val laptopDone =
                respond {
                    // First a probe's INFO, which carries no secret.
                    server.accept().use { s ->
                        val input = DataInputStream(s.getInputStream())
                        assertArrayEquals(Vectors.frame(magic + byteArrayOf(1, 0)), readFrame(input))
                        s.getOutputStream().write(Vectors.frame(byteArrayOf(0) + laptopId + byteArrayOf(5) + nameL))
                    }
                    // Then the resume.
                    server.accept().use { s ->
                        val input = DataInputStream(s.getInputStream())
                        val output = s.getOutputStream()
                        val request =
                            magic + byteArrayOf(1, 2) + phoneId + phone.publicKey + Vectors.bytes("RESUME_INPUT", "nP")
                        assertArrayEquals(Vectors.frame(request), readFrame(input))
                        val reply =
                            byteArrayOf(0) + laptop.publicKey + Vectors.bytes("RESUME_INPUT", "nL") +
                                Vectors.bytes("RESUME", "tagL")
                        output.write(Vectors.frame(reply))
                        assertArrayEquals(Vectors.frame(Vectors.bytes("RESUME", "tagP")), readFrame(input))
                        val records = Vectors.records("RESUME_RECORD")
                        val sealed = records[0] + records[1]
                        assertArrayEquals(sealed, readBytes(input, sealed.size))
                        laptopChannel(input, output, Vectors.bytes("RESUME", "prk")).output.write(plain)
                    }
                }

            Socket(loopback, server.localPort).use { s ->
                val info = LinkInitiator().info(s.getInputStream(), s.getOutputStream())
                assertArrayEquals(laptopId, info.laptopId)
                assertEquals("Laptop", info.name)
            }
            val resumer = LinkInitiator({ phone }, { Vectors.bytes("RESUME_INPUT", "nP") })
            val transport = transport(server.localPort, keys, resumer)
            val streams =
                transport.open(
                    showCode = { fail("A resume shows no code") },
                    confirmed = {
                        fail("Nor asks")
                        false
                    },
                )
            streams.output.write(plain)
            streams.output.write(plain)
            streams.output.flush()
            assertArrayEquals(plain, readBytes(DataInputStream(streams.input), plain.size))
            laptopDone()
            transport.close()
        }
    }

    @Test(timeout = TIMEOUT_MS)
    fun aPairingShowsTheCodeAndKeepsTheKeyOnlyOnTheOwnersYes() {
        val keys = FakeKeys()
        val shown = ArrayList<String>()
        pairing(keys, ownerSaysYes = true) { shown.add(it.display()) }
        assertEquals(listOf("009 019"), shown)
        assertArrayEquals(Vectors.bytes("PAIR_KEYS", "pk"), keys.pairingKey(laptopId))
    }

    @Test(timeout = TIMEOUT_MS)
    fun anOwnersNoSendsNoConfirmationAndKeepsNothing() {
        // The laptop's confirmation checks out, which a device in the middle could send just the same.
        val keys = FakeKeys()
        val failure = assertThrows(LinkFailure::class.java) { pairing(keys, ownerSaysYes = false) }
        assertEquals(LaptopLink.Cause.PAIRING_FAILED, failure.closure.cause)
        assertNull(keys.pairingKey(laptopId))
    }

    @Test(timeout = TIMEOUT_MS)
    fun pairingAgainReplacesTheStoredKeyOnlyOnTheOwnersYes() {
        val old = ByteArray(Vectors.bytes("PAIR_KEYS", "pk").size) { 7 }
        val refused = FakeKeys().apply { save(laptopId, old) }
        assertThrows(LinkFailure::class.java) { pairing(refused, ownerSaysYes = false, forcePair = true) }
        assertArrayEquals(old, refused.pairingKey(laptopId))

        val accepted = FakeKeys().apply { save(laptopId, old) }
        pairing(accepted, ownerSaysYes = true, forcePair = true)
        assertArrayEquals(Vectors.bytes("PAIR_KEYS", "pk"), accepted.pairingKey(laptopId))
    }

    @Test(timeout = TIMEOUT_MS)
    fun aPairingAnsweredByAnotherLaptopEndsBeforeAnyCodeIsShown() {
        val other = laptopId.copyOf()
        other[0] = (other[0].toInt() xor 1).toByte()
        val keys = FakeKeys()
        val shown = ArrayList<Sas>()
        ServerSocket(0, 1, loopback).use { server ->
            val laptopDone =
                respond {
                    server.accept().use { s ->
                        val input = DataInputStream(s.getInputStream())
                        readFrame(input)
                        s.getOutputStream().write(Vectors.frame(offer()))
                        // No reveal: the phone hangs up.
                        assertEquals(-1, input.read())
                    }
                }
            val transport = transport(server.localPort, keys, pairingInitiator(), expected = other)
            val failure =
                assertThrows(LinkFailure::class.java) {
                    transport.open(showCode = { shown.add(it) }, confirmed = { true })
                }
            laptopDone()
            assertEquals(LaptopLink.Cause.PAIRING_FAILED, failure.closure.cause)
        }
        assertEquals(0, shown.size)
        assertNull(keys.pairingKey(other))
    }

    @Test(timeout = TIMEOUT_MS)
    fun aLaptopThatSaysItDoesNotKnowThePhoneCostsNoKey() {
        val key = Vectors.bytes("RESUME_INPUT", "pk")
        val keys = FakeKeys().apply { save(laptopId, key) }
        val failure = refusedWith(byteArrayOf(LinkWire.STATUS_UNKNOWN_PHONE.toByte()), keys)
        assertEquals(LaptopLink.Cause.NOT_PAIRED, failure.closure.cause)
        assertArrayEquals(key, keys.pairingKey(laptopId))
    }

    @Test(timeout = TIMEOUT_MS)
    fun aResumeTheLaptopCannotConfirmCostsNoKey() {
        val key = Vectors.bytes("RESUME_INPUT", "pk")
        val keys = FakeKeys().apply { save(laptopId, key) }
        val wrongTag = ByteArray(Vectors.bytes("RESUME", "tagL").size)
        val reply = byteArrayOf(0) + laptop.publicKey + Vectors.bytes("RESUME_INPUT", "nL") + wrongTag
        val failure = refusedWith(reply, keys)
        assertEquals(LaptopLink.Cause.NOT_PAIRED, failure.closure.cause)
        assertArrayEquals(key, keys.pairingKey(laptopId))
    }

    @Test(timeout = TIMEOUT_MS)
    fun aBusyLaptopIsPairingLocked() {
        val failure = refusedWith(byteArrayOf(LinkWire.STATUS_BUSY.toByte()), FakeKeys())
        assertEquals(LaptopLink.Cause.PAIRING_LOCKED, failure.closure.cause)
    }

    @Test(timeout = TIMEOUT_MS)
    fun aLaptopNotListeningIsUnreachable() {
        val port = ServerSocket(0, 1, loopback).use { it.localPort }
        val transport = transport(port, FakeKeys(), LinkInitiator())
        val e = assertThrows(IOException::class.java) { transport.open(showCode = {}, confirmed = { true }) }
        assertFalse(e is LinkFailure)
        assertEquals(LaptopLink.Cause.UNREACHABLE, closureOf(e, connectReturned = false, handshakeDone = false).cause)
    }

    @Test
    fun aTransportsOwnReasonOutranksHowFarTheLinkGot() {
        val failure = LinkFailure(LaptopLink.Closure(LaptopLink.Cause.PAIRING_FAILED))
        val reason = closureOf(failure, connectReturned = false, handshakeDone = false)
        assertEquals(LaptopLink.Cause.PAIRING_FAILED, reason.cause)
        val dropped = closureOf(IOException(), connectReturned = true, handshakeDone = true)
        assertEquals(LaptopLink.Cause.LOST, dropped.cause)
    }

    @Test
    fun eachStatusClosesWithItsCause() {
        assertNull(closureForStatus(0))
        assertEquals(LaptopLink.Cause.PAIRING_LOCKED, closureForStatus(1)?.cause)
        assertEquals(LaptopLink.Cause.UNEXPECTED, closureForStatus(2)?.cause)
        assertEquals(LaptopLink.Cause.NOT_PAIRED, closureForStatus(3)?.cause)
    }

    @Test
    fun theSameSlash24ComesFirstThenIpv4ThenIpv6() {
        val v6 = InetAddress.getByName("fe80::1")
        val other = InetAddress.getByName("10.0.0.5")
        val same = InetAddress.getByName("192.168.1.20")
        val own = listOf(InetAddress.getByName("192.168.1.7"), InetAddress.getByName("fe80::2"))
        assertEquals(listOf(same, other, v6), orderHosts(listOf(v6, other, same), own))
        assertEquals(listOf(other, same, v6), orderHosts(listOf(v6, other, same), emptyList()))
    }

    /**
     * A whole pairing against the vector laptop, which confirms as if its owner typed the code. The laptop gets the
     * phone's confirmation and sends a record only when [ownerSaysYes]; otherwise it sees the phone hang up.
     */
    private fun pairing(
        keys: FakeKeys,
        ownerSaysYes: Boolean,
        forcePair: Boolean = false,
        showCode: (Sas) -> Unit = {},
    ) {
        ServerSocket(0, 1, loopback).use { server ->
            val laptopDone =
                respond {
                    server.accept().use { s ->
                        val input = DataInputStream(s.getInputStream())
                        val output = s.getOutputStream()
                        val request = magic + byteArrayOf(1, 1) + phoneId + nameP + Vectors.bytes("PAIR", "commit")
                        assertArrayEquals(Vectors.frame(request), readFrame(input))
                        output.write(Vectors.frame(offer()))
                        val reveal = phone.publicKey + Vectors.bytes("PAIR_INPUT", "nP")
                        assertArrayEquals(Vectors.frame(reveal), readFrame(input))
                        output.write(Vectors.frame(Vectors.bytes("PAIR_KEYS", "tagL")))
                        if (ownerSaysYes) {
                            assertArrayEquals(Vectors.frame(Vectors.bytes("PAIR_KEYS", "tagP")), readFrame(input))
                            laptopChannel(input, output, Vectors.bytes("PAIR_KEYS", "prk")).output.write(plain)
                        } else {
                            assertEquals(-1, input.read())
                        }
                    }
                }
            val transport = transport(server.localPort, keys, pairingInitiator(), forcePair = forcePair)
            try {
                val streams = transport.open(showCode = showCode, confirmed = { ownerSaysYes })
                assertArrayEquals(plain, readBytes(DataInputStream(streams.input), plain.size))
            } finally {
                transport.close()
                laptopDone()
            }
        }
    }

    private fun offer(): ByteArray =
        byteArrayOf(0) + laptopId + nameL + laptop.publicKey + Vectors.bytes("PAIR_INPUT", "nL")

    private fun pairingInitiator() = LinkInitiator({ phone }, { Vectors.bytes("PAIR_INPUT", "nP") })

    private fun refusedWith(
        reply: ByteArray,
        keys: FakeKeys,
    ): LinkFailure =
        ServerSocket(0, 1, loopback).use { server ->
            val laptopDone =
                respond {
                    server.accept().use { s ->
                        readFrame(DataInputStream(s.getInputStream()))
                        s.getOutputStream().write(Vectors.frame(reply))
                    }
                }
            val transport = transport(server.localPort, keys, LinkInitiator())
            val failure =
                assertThrows(LinkFailure::class.java) { transport.open(showCode = {}, confirmed = { true }) }
            laptopDone()
            failure
        }

    private fun transport(
        port: Int,
        keys: PairingKeys,
        initiator: LinkInitiator,
        expected: ByteArray = laptopId,
        forcePair: Boolean = false,
    ) = TcpTransport(listOf(loopback), port, expected, keys, "Phone", forcePair, initiator) { emptyList() }

    private fun laptopChannel(
        input: DataInputStream,
        output: OutputStream,
        prk: ByteArray,
    ) = SecureChannel(input, output, prk, isPhone = false)

    /** Runs the laptop's side on a thread of its own; the returned call waits for it and rethrows what it threw. */
    private fun respond(body: () -> Unit): () -> Unit {
        var failure: Throwable? = null
        val laptopThread =
            thread {
                try {
                    body()
                } catch (e: Throwable) {
                    failure = e
                }
            }
        return {
            laptopThread.join()
            failure?.let { throw it }
        }
    }

    private fun readFrame(input: DataInputStream): ByteArray {
        val low = input.readUnsignedByte()
        val high = input.readUnsignedByte()
        return Vectors.frame(readBytes(input, low or (high shl 8)))
    }

    private fun readBytes(
        input: DataInputStream,
        count: Int,
    ): ByteArray = ByteArray(count).also { input.readFully(it) }

    private class FakeKeys : PairingKeys {
        private val saved = HashMap<List<Byte>, ByteArray>()

        override val phoneId: ByteArray = Vectors.bytes("PAIR_INPUT", "phoneId")

        override fun pairingKey(laptopId: ByteArray): ByteArray? = saved[laptopId.toList()]?.copyOf()

        override fun save(
            laptopId: ByteArray,
            pairingKey: ByteArray,
        ) {
            saved[laptopId.toList()] = pairingKey.copyOf()
        }

        override fun forget(laptopId: ByteArray) {
            saved.remove(laptopId.toList())
        }
    }

    private companion object {
        const val TIMEOUT_MS = 10_000L
    }
}
