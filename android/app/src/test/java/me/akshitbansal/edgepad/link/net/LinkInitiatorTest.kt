package me.akshitbansal.edgepad.link.net

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.DataInputStream
import java.io.IOException
import java.security.GeneralSecurityException

/**
 * The phone's half of EDGL1 against a laptop that replays the vector transcript: the laptop's every byte is
 * fixed in advance from protocol/link-vectors.txt, and everything the phone sends is checked byte for byte
 * against what the laptop's C# expects. The bytes the phone should send are spelled out here from the file, not
 * built with [LinkWire], so an encoding mistake there cannot hide behind the same mistake here.
 */
class LinkInitiatorTest {
    private val phone = Vectors.phone()
    private val laptop = Vectors.laptop()
    private val phoneId = Vectors.bytes("PAIR_INPUT", "phoneId")
    private val laptopId = Vectors.bytes("PAIR_INPUT", "laptopId")
    private val nameP = Vectors.bytes("PAIR_INPUT", "nameP")
    private val nameL = Vectors.bytes("PAIR_INPUT", "nameL")
    private val nP = Vectors.bytes("PAIR_INPUT", "nP")
    private val nL = Vectors.bytes("PAIR_INPUT", "nL")
    private val magic = "EDGL".toByteArray()
    private val plain = "EDGL1 record".toByteArray()

    private val pairRequest =
        Vectors.frame(magic + byteArrayOf(1, 1) + phoneId + nameP + Vectors.bytes("PAIR", "commit"))
    private val reveal = Vectors.frame(phone.publicKey + nP)
    private val offer = Vectors.frame(byteArrayOf(0) + laptopId + nameL + laptop.publicKey + nL)

    @Test
    fun infoReadsTheLaptopsIdVersionAndName() {
        val sent = ByteArrayOutputStream()
        val reply = Vectors.frame(byteArrayOf(0) + laptopId + byteArrayOf(5) + nameL)
        val info = LinkInitiator().info(ByteArrayInputStream(reply), sent)
        assertArrayEquals(Vectors.frame(magic + byteArrayOf(1, 0)), sent.toByteArray())
        assertEquals(LinkWire.STATUS_OK, info.status)
        assertArrayEquals(laptopId, info.laptopId)
        assertEquals(5, info.protocolVersion)
        assertEquals("Laptop", info.name)
    }

    @Test
    fun aReplyThatRunsOnPastItsFieldsIsRefused() {
        val reply = Vectors.frame(byteArrayOf(0) + laptopId + byteArrayOf(5) + nameL + byteArrayOf(9))
        assertThrows(IOException::class.java) {
            LinkInitiator().info(ByteArrayInputStream(reply), ByteArrayOutputStream())
        }
    }

    @Test
    fun aPairingShowsTheCodeAndReachesAnEncryptedChannel() {
        val prk = Vectors.bytes("PAIR_KEYS", "prk")
        val fromLaptop = offer + Vectors.frame(Vectors.bytes("PAIR_KEYS", "tagL")) + sealedByLaptop(prk)
        val sent = ByteArrayOutputStream()
        val shown = ArrayList<Sas>()

        val result = pair(fromLaptop, sent) { shown.add(it) }

        assertEquals("009 019", shown.single().display())
        assertArrayEquals(laptopId, result.laptopId)
        assertArrayEquals(Vectors.bytes("PAIR_KEYS", "pk"), result.pairingKey)
        val channel = result.channel
        assertNotNull(channel)
        channel!!.output.write(plain)
        channel.output.write(plain)
        val records = Vectors.records("PAIR_RECORD")
        val tagP = Vectors.frame(Vectors.bytes("PAIR_KEYS", "tagP"))
        assertArrayEquals(pairRequest + reveal + tagP + records[0] + records[1], sent.toByteArray())
        assertArrayEquals(plain, readAll(channel, plain.size))
    }

    @Test
    fun aWrongLaptopConfirmationMeansNothingIsSentAndNoPairing() {
        val tagL = Vectors.bytes("PAIR_KEYS", "tagL")
        tagL[0] = (tagL[0].toInt() xor 1).toByte()
        assertNotPaired(offer + Vectors.frame(tagL))
    }

    @Test
    fun aLaptopThatClosesWithoutConfirmingLeavesNoPairing() {
        assertNotPaired(offer)
    }

    @Test
    fun aResumeTagInPlaceOfAPairingOneIsRefused() {
        assertNotPaired(offer + Vectors.frame(Vectors.bytes("RESUME", "tagL")))
    }

    @Test
    fun anOwnerWhoSaysTheLaptopDidNotAcceptTheCodeSendsNoConfirmation() {
        // The laptop's confirmation checks out, which any peer that ran the exchange can send: only the owner's
        // yes on the phone sends the phone's half.
        assertNotPaired(offer + Vectors.frame(Vectors.bytes("PAIR_KEYS", "tagL")), ownerSaysYes = false)
    }

    @Test
    fun anotherLaptopAnsweringEndsThePairingBeforeTheReveal() {
        val other = laptopId.copyOf()
        other[0] = (other[0].toInt() xor 1).toByte()
        val sent = ByteArrayOutputStream()
        val shown = ArrayList<Sas>()
        val result = pair(offer, sent, expectedLaptopId = other) { shown.add(it) }
        assertNull(result.channel)
        assertEquals(0, shown.size)
        assertArrayEquals(pairRequest, sent.toByteArray())
    }

    @Test
    fun aBusyLaptopEndsThePairingAfterTheRequest() {
        val sent = ByteArrayOutputStream()
        val result = pair(Vectors.frame(byteArrayOf(1)), sent)
        assertEquals(LinkWire.STATUS_BUSY, result.status)
        assertNull(result.channel)
        assertArrayEquals(pairRequest, sent.toByteArray())
    }

    @Test
    fun aLaptopKeyOffTheCurveIsRefusedBeforeAnyCodeIsShown() {
        val bad = laptop.publicKey.copyOf()
        bad[bad.size - 1] = (bad[bad.size - 1].toInt() xor 1).toByte()
        val badOffer = Vectors.frame(byteArrayOf(0) + laptopId + nameL + bad + nL)
        val sent = ByteArrayOutputStream()
        val shown = ArrayList<Sas>()
        assertThrows(GeneralSecurityException::class.java) {
            pair(badOffer, sent) { shown.add(it) }
        }
        assertEquals(0, shown.size)
        assertArrayEquals(pairRequest + reveal, sent.toByteArray())
    }

    @Test
    fun aResumeReachesAnEncryptedChannel() {
        val sent = ByteArrayOutputStream()
        val result = resuming(Vectors.bytes("RESUME_INPUT", "pk"), sent, Vectors.bytes("RESUME", "tagL"))
        val channel = result.channel
        assertNotNull(channel)
        channel!!.output.write(plain)
        channel.output.write(plain)
        val records = Vectors.records("RESUME_RECORD")
        val tagP = Vectors.frame(Vectors.bytes("RESUME", "tagP"))
        assertArrayEquals(resumeRequest() + tagP + records[0] + records[1], sent.toByteArray())
    }

    @Test
    fun aResumeWithTheWrongKeySendsNoConfirmation() {
        val wrongKey = Vectors.bytes("RESUME_INPUT", "pk")
        wrongKey[0] = (wrongKey[0].toInt() xor 1).toByte()
        val sent = ByteArrayOutputStream()
        val result = resuming(wrongKey, sent, Vectors.bytes("RESUME", "tagL"))
        assertEquals(LinkWire.STATUS_OK, result.status)
        assertNull(result.channel)
        assertArrayEquals(resumeRequest(), sent.toByteArray())
    }

    @Test
    fun anUnknownPhoneIsToldSo() {
        val sent = ByteArrayOutputStream()
        val unknown = ByteArrayInputStream(Vectors.frame(byteArrayOf(3)))
        val result = resumer().resume(unknown, sent, phoneId, laptopId, ByteArray(LinkCrypto.KEY_LENGTH))
        assertEquals(LinkWire.STATUS_UNKNOWN_PHONE, result.status)
        assertNull(result.channel)
    }

    private fun pair(
        fromLaptop: ByteArray,
        sent: ByteArrayOutputStream,
        expectedLaptopId: ByteArray? = laptopId,
        ownerSaysYes: Boolean = true,
        showCode: (Sas) -> Unit = {},
    ): Initiated {
        val initiator = LinkInitiator({ phone }, { nP.copyOf() })
        val input = ByteArrayInputStream(fromLaptop)
        return initiator.pair(input, sent, phoneId, "Phone", expectedLaptopId, { ownerSaysYes }, showCode)
    }

    private fun assertNotPaired(
        fromLaptop: ByteArray,
        expectedLaptopId: ByteArray? = laptopId,
        ownerSaysYes: Boolean = true,
    ) {
        val sent = ByteArrayOutputStream()
        val result = pair(fromLaptop, sent, expectedLaptopId, ownerSaysYes)
        assertNull(result.channel)
        assertEquals(0, result.pairingKey.size)
        // The request and the reveal, and nothing after them: no confirmation, no record.
        assertArrayEquals(pairRequest + reveal, sent.toByteArray())
    }

    private fun resumeRequest(): ByteArray =
        Vectors.frame(magic + byteArrayOf(1, 2) + phoneId + phone.publicKey + Vectors.bytes("RESUME_INPUT", "nP"))

    private fun resuming(
        pairingKey: ByteArray,
        sent: ByteArrayOutputStream,
        tagL: ByteArray,
    ): Initiated {
        val reply = Vectors.frame(byteArrayOf(0) + laptop.publicKey + Vectors.bytes("RESUME_INPUT", "nL") + tagL)
        return resumer().resume(ByteArrayInputStream(reply), sent, phoneId, laptopId, pairingKey)
    }

    private fun resumer() = LinkInitiator({ phone }, { Vectors.bytes("RESUME_INPUT", "nP") })

    // One record from the laptop's end, which the phone's channel must open.
    private fun sealedByLaptop(prk: ByteArray): ByteArray {
        val wire = ByteArrayOutputStream()
        SecureChannel(ByteArrayInputStream(ByteArray(0)), wire, prk, isPhone = false).output.write(plain)
        return wire.toByteArray()
    }

    private fun readAll(
        channel: SecureChannel,
        count: Int,
    ): ByteArray {
        val bytes = ByteArray(count)
        DataInputStream(channel.input).readFully(bytes)
        return bytes
    }
}
