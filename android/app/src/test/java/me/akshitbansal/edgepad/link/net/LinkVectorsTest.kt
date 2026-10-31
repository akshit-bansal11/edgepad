package me.akshitbansal.edgepad.link.net

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.DataInputStream
import java.security.GeneralSecurityException

/**
 * Runs protocol/link-vectors.txt, the values the laptop's C# worked out, through the Kotlin port: every derived
 * value and both sealed records must come out byte for byte.
 */
class LinkVectorsTest {
    private val phone = Vectors.phone()
    private val laptop = Vectors.laptop()
    private val phoneId = Vectors.bytes("PAIR_INPUT", "phoneId")
    private val laptopId = Vectors.bytes("PAIR_INPUT", "laptopId")
    private val nP = Vectors.bytes("PAIR_INPUT", "nP")
    private val nL = Vectors.bytes("PAIR_INPUT", "nL")

    @Test
    fun theFixtureHasEveryLine() {
        val expected =
            setOf(
                "PAIR_INPUT",
                "PAIR",
                "PAIR_KEYS",
                "PAIR_SESSION",
                "PAIR_RECORD",
                "RESUME_INPUT",
                "RESUME",
                "RESUME_SESSION",
                "RESUME_RECORD",
            )
        assertEquals(expected, Vectors.names)
        assertEquals(2, Vectors.records("PAIR_RECORD").size)
        assertEquals(2, Vectors.records("RESUME_RECORD").size)
    }

    @Test
    fun bothEndsAgreeOnTheSecret() {
        assertHex("PAIR", "z", LinkCrypto.agree(phone, laptop.publicKey))
        assertHex("PAIR", "z", LinkCrypto.agree(laptop, phone.publicKey))
    }

    @Test
    fun aPairingDerivesEveryValue() {
        assertArrayEquals(Vectors.bytes("PAIR_INPUT", "nameP"), LinkWire.nameField("Phone"))
        assertArrayEquals(Vectors.bytes("PAIR_INPUT", "nameL"), LinkWire.nameField("Laptop"))
        assertHex("PAIR", "commit", LinkCrypto.commit(phone.publicKey, nP))

        val th = pairTranscript()
        assertHex("PAIR", "th", th)
        val sas = Vectors.line("PAIR").getValue("sas")
        assertEquals(sas.substring(0, 3) + " " + sas.substring(3), LinkCrypto.sas(th).display())

        val prk = LinkCrypto.pairPrk(th, LinkCrypto.agree(phone, laptop.publicKey))
        assertHex("PAIR_KEYS", "prk", prk)
        assertHex("PAIR_KEYS", "tagL", LinkCrypto.laptopTag(prk, th, pairing = true))
        assertHex("PAIR_KEYS", "tagP", LinkCrypto.phoneTag(prk, th, pairing = true))
        assertHex("PAIR_KEYS", "pk", LinkCrypto.pairingKey(prk))
        assertSession("PAIR", prk)
    }

    @Test
    fun aResumeDerivesEveryValue() {
        val pairingKey = Vectors.bytes("RESUME_INPUT", "pk")
        val th =
            LinkCrypto.resumeTranscript(
                phoneId,
                laptopId,
                phone.publicKey,
                Vectors.bytes("RESUME_INPUT", "nP"),
                laptop.publicKey,
                Vectors.bytes("RESUME_INPUT", "nL"),
            )
        assertHex("RESUME", "th", th)

        val prk = LinkCrypto.resumePrk(th, LinkCrypto.agree(phone, laptop.publicKey), pairingKey)
        assertHex("RESUME", "prk", prk)
        assertHex("RESUME", "tagL", LinkCrypto.laptopTag(prk, th, pairing = false))
        assertHex("RESUME", "tagP", LinkCrypto.phoneTag(prk, th, pairing = false))
        assertSession("RESUME", prk)
    }

    @Test
    fun aPointOffTheCurveIsRefused() {
        val moved = laptop.publicKey.copyOf()
        moved[moved.size - 1] = (moved[moved.size - 1].toInt() xor 1).toByte()
        assertThrows(GeneralSecurityException::class.java) { LinkCrypto.agree(phone, moved) }
    }

    @Test
    fun thePointAtInfinityIsRefusedInEitherSpelling() {
        // SEC 1 writes infinity as the single byte 00; the all-zero point is the nearest a 65-byte field gets.
        assertThrows(GeneralSecurityException::class.java) { LinkCrypto.agree(phone, byteArrayOf(0)) }
        val zero = ByteArray(LinkWire.PUBLIC_KEY_LENGTH)
        zero[0] = 0x04
        assertThrows(GeneralSecurityException::class.java) { LinkCrypto.agree(phone, zero) }
    }

    @Test
    fun anythingButAnUncompressedPointIsRefused() {
        val compressed = byteArrayOf(0x02) + laptop.publicKey.copyOfRange(1, 33)
        assertThrows(GeneralSecurityException::class.java) { LinkCrypto.agree(phone, compressed) }
        val badPrefix = laptop.publicKey.copyOf()
        badPrefix[0] = 0x05
        assertThrows(GeneralSecurityException::class.java) { LinkCrypto.agree(phone, badPrefix) }
    }

    @Test
    fun aCoordinateAtOrAboveThePrimeIsRefused() {
        val p = Vectors.hex("ffffffff00000001000000000000000000000000ffffffffffffffffffffffff")
        val tooBig = byteArrayOf(0x04) + p + laptop.publicKey.copyOfRange(33, 65)
        assertThrows(GeneralSecurityException::class.java) { LinkCrypto.agree(phone, tooBig) }
    }

    @Test
    fun theCodeNeverPrintsItsDigits() {
        val sas = LinkCrypto.sas(pairTranscript())
        assertEquals("******", sas.toString())
        assertEquals("******", "$sas")
        assertFalse(sas.toString().any { it.isDigit() })
        assertTrue(sas.display().any { it.isDigit() })
    }

    @Test
    fun aNameIsCutAtACharacterBoundary() {
        // 128 two-byte characters are 256 bytes; the cut must not leave half of the last one.
        val field = LinkWire.nameField("é".repeat(128))
        assertEquals(254, field[0].toInt() and 0xFF)
        assertEquals(255, field.size)
    }

    private fun pairTranscript(): ByteArray =
        LinkCrypto.pairTranscript(
            phoneId,
            laptopId,
            LinkWire.nameField("Phone"),
            LinkWire.nameField("Laptop"),
            phone.publicKey,
            nP,
            laptop.publicKey,
            nL,
        )

    private fun assertSession(
        name: String,
        prk: ByteArray,
    ) {
        val (p2l, l2p, iv) = LinkCrypto.sessionKeys(prk)
        assertHex(name + "_SESSION", "kP2L", p2l)
        assertHex(name + "_SESSION", "kL2P", l2p)
        assertHex(name + "_SESSION", "iv", iv)

        // The phone seals the same plaintext twice, as counters 0 and 1, and the laptop's end opens both.
        val expected = Vectors.records(name + "_RECORD")
        val plain = "EDGL1 record".toByteArray()
        val wire = ByteArrayOutputStream()
        val sealer = SecureChannel(ByteArrayInputStream(ByteArray(0)), wire, prk, isPhone = true)
        for (record in expected) {
            val before = wire.size()
            sealer.output.write(plain)
            assertArrayEquals(record, wire.toByteArray().copyOfRange(before, wire.size()))
        }

        val laptopEnd = ByteArrayInputStream(wire.toByteArray())
        val opener = SecureChannel(laptopEnd, ByteArrayOutputStream(), prk, isPhone = false)
        val back = ByteArray(plain.size * expected.size)
        DataInputStream(opener.input).readFully(back)
        assertArrayEquals(plain + plain, back)
    }

    private fun assertHex(
        line: String,
        key: String,
        actual: ByteArray,
    ) = assertArrayEquals("$line $key", Vectors.bytes(line, key), actual)
}
