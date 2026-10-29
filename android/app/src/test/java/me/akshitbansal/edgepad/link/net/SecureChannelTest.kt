package me.akshitbansal.edgepad.link.net

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.EOFException
import java.io.IOException
import java.io.OutputStream

/** The record layer: splitting, reassembly, and that any record out of place closes the link for good. */
class SecureChannelTest {
    private val prk = ByteArray(LinkCrypto.KEY_LENGTH) { it.toByte() }

    @Test
    fun aLargeWriteIsSplitIntoRecordsAndReadBackInPieces() {
        val data = ByteArray(40_000) { (it * 7).toByte() }
        val wire = phoneWrites(data)
        // 16384 + 16384 + 7232 bytes of plaintext, each record with its 2-byte length and 16-byte tag.
        assertEquals(data.size + 3 * (2 + LinkCrypto.AEAD_TAG_LENGTH), wire.size)

        val laptop = laptopReading(wire)
        val back = ByteArray(data.size)
        var at = 0
        while (at < back.size) {
            val n = laptop.input.read(back, at, minOf(1000, back.size - at))
            assertTrue(n > 0)
            at += n
        }
        assertArrayEquals(data, back)
        assertEquals(-1, laptop.input.read())
    }

    @Test
    fun aTamperedRecordClosesTheLinkForGood() {
        val wire = phoneWrites(byteArrayOf(1, 2, 3), byteArrayOf(4, 5, 6))
        wire[3] = (wire[3].toInt() xor 1).toByte()
        val laptop = laptopReading(wire)
        assertThrows(IOException::class.java) { laptop.input.read() }
        // The second record is intact, but nothing after a failure is read, and nothing more is sent.
        assertThrows(IOException::class.java) { laptop.input.read() }
        assertThrows(IOException::class.java) { laptop.output.write(1) }
    }

    @Test
    fun recordsOutOfOrderAreRefused() {
        val first = phoneWrites(byteArrayOf(1, 2, 3), byteArrayOf(4, 5, 6))
        val half = first.size / 2
        val swapped = first.copyOfRange(half, first.size) + first.copyOfRange(0, half)
        assertThrows(IOException::class.java) { laptopReading(swapped).input.read() }
    }

    @Test
    fun aReplayedRecordIsRefused() {
        val once = phoneWrites(byteArrayOf(1, 2, 3))
        val laptop = laptopReading(once + once)
        assertEquals(1, laptop.input.read())
        assertEquals(2, laptop.input.read())
        assertEquals(3, laptop.input.read())
        assertThrows(IOException::class.java) { laptop.input.read() }
    }

    @Test
    fun aLengthOutOfRangeIsRefused() {
        val empty = byteArrayOf(LinkCrypto.AEAD_TAG_LENGTH.toByte(), 0) + ByteArray(LinkCrypto.AEAD_TAG_LENGTH)
        assertThrows(IOException::class.java) { laptopReading(empty).input.read() }
        val tooLong = byteArrayOf(0xFF.toByte(), 0xFF.toByte())
        assertThrows(IOException::class.java) { laptopReading(tooLong).input.read() }
    }

    @Test
    fun aRecordCutShortIsAnEndOfStream() {
        val wire = phoneWrites(byteArrayOf(1, 2, 3))
        assertThrows(EOFException::class.java) { laptopReading(wire.copyOf(wire.size - 1)).input.read() }
    }

    @Test
    fun aFailedWriteClosesTheChannel() {
        val broken =
            object : OutputStream() {
                var closed = false

                override fun write(b: Int): Unit = throw IOException("The socket is gone")

                override fun close() {
                    closed = true
                }
            }
        val phone = SecureChannel(ByteArrayInputStream(ByteArray(0)), broken, prk, isPhone = true)
        assertThrows(IOException::class.java) { phone.output.write(byteArrayOf(1)) }
        assertTrue(broken.closed)
        val second = assertThrows(IOException::class.java) { phone.output.write(byteArrayOf(2)) }
        assertEquals("The link was closed after a record failed", second.message)
    }

    private fun phoneWrites(vararg writes: ByteArray): ByteArray {
        val wire = ByteArrayOutputStream()
        val phone = SecureChannel(ByteArrayInputStream(ByteArray(0)), wire, prk, isPhone = true)
        for (bytes in writes) phone.output.write(bytes)
        return wire.toByteArray()
    }

    private fun laptopReading(wire: ByteArray) =
        SecureChannel(ByteArrayInputStream(wire), ByteArrayOutputStream(), prk, isPhone = false)
}
