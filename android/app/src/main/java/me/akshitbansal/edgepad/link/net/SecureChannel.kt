package me.akshitbansal.edgepad.link.net

import java.io.Closeable
import java.io.DataInputStream
import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.security.GeneralSecurityException
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * The encrypted half of the network link: AES-256-GCM records over the raw socket streams, so the frame code
 * above reads [input] and writes [output] exactly as it does an RFCOMM socket. Ported from the laptop's
 * `shared/Edgepad.Core/Net/Link/SecureStream.cs`.
 *
 * A record is `u16 length | ciphertext | 16-byte tag`, little-endian, the length counting the tag and
 * authenticated as the record's associated data. The nonce is never sent: it is the direction's 4-byte prefix and
 * a counter both ends keep, so a record replayed, dropped or moved fails its tag like a forged one. Any record
 * that fails, a length out of range, or any error writing closes the link for good; nothing after it is read and
 * nothing more is sent.
 *
 * One thread reads and any thread may write: each direction has its own lock, cipher and counter. It owns the
 * raw streams and closes them.
 *
 * @param prk the handshake's secret, from which both directions' keys are taken; the caller zeroes it.
 * @param isPhone which end this is: each sends with the other's receive key. Only tests build the laptop's end.
 */
class SecureChannel(
    rawInput: InputStream,
    private val rawOutput: OutputStream,
    prk: ByteArray,
    isPhone: Boolean,
) : Closeable {
    private val raw = DataInputStream(rawInput)
    private val sealKey: SecretKeySpec
    private val openKey: SecretKeySpec
    private val sendPrefix: ByteArray
    private val receivePrefix: ByteArray
    private val sealer = Cipher.getInstance(TRANSFORMATION)
    private val opener = Cipher.getInstance(TRANSFORMATION)

    private val readLock = Any()
    private val writeLock = Any()

    private val inRecord = ByteArray(LinkWire.MAX_RECORD_PLAIN + LinkCrypto.AEAD_TAG_LENGTH)
    private val plain = ByteArray(LinkWire.MAX_RECORD_PLAIN)
    private val outRecord = ByteArray(2 + LinkWire.MAX_RECORD_PLAIN + LinkCrypto.AEAD_TAG_LENGTH)

    private var sent = 0L
    private var received = 0L
    private var plainOffset = 0
    private var plainCount = 0

    // Set by the first record that fails. A link that has carried one forged or misplaced record is not trusted
    // with another in either direction.
    @Volatile private var failed = false

    init {
        val (p2l, l2p, iv) = LinkCrypto.sessionKeys(prk)
        // SecretKeySpec and copyOfRange keep copies of their own, so the derived arrays can be zeroed.
        sealKey = SecretKeySpec(if (isPhone) p2l else l2p, "AES")
        openKey = SecretKeySpec(if (isPhone) l2p else p2l, "AES")
        val first = iv.copyOfRange(0, PREFIX_LENGTH)
        val second = iv.copyOfRange(PREFIX_LENGTH, iv.size)
        sendPrefix = if (isPhone) first else second
        receivePrefix = if (isPhone) second else first
        p2l.fill(0)
        l2p.fill(0)
        iv.fill(0)
    }

    /** The decrypted bytes. -1 only when the peer closed cleanly between records. */
    val input: InputStream =
        object : InputStream() {
            override fun read(): Int {
                val one = ByteArray(1)
                return if (read(one, 0, 1) < 0) -1 else one[0].toInt() and 0xFF
            }

            override fun read(
                b: ByteArray,
                off: Int,
                len: Int,
            ): Int = readPlain(b, off, len)

            override fun close() = this@SecureChannel.close()
        }

    /** Seals what is written into as many records as it takes, each at most 16 KiB of plaintext. */
    val output: OutputStream =
        object : OutputStream() {
            override fun write(b: Int) = write(byteArrayOf(b.toByte()), 0, 1)

            override fun write(
                b: ByteArray,
                off: Int,
                len: Int,
            ) = writePlain(b, off, len)

            override fun flush() = flushRaw()

            override fun close() = this@SecureChannel.close()
        }

    /** Closes the raw streams; a read blocked on a socket ends with an [IOException]. */
    override fun close() = fail()

    private fun readPlain(
        buffer: ByteArray,
        offset: Int,
        length: Int,
    ): Int {
        if (length == 0) return 0
        synchronized(readLock) {
            if (plainCount == 0 && !nextRecord()) return -1
            val n = minOf(length, plainCount)
            plain.copyInto(buffer, offset, plainOffset, plainOffset + n)
            plainOffset += n
            plainCount -= n
            return n
        }
    }

    private fun writePlain(
        buffer: ByteArray,
        offset: Int,
        length: Int,
    ) {
        synchronized(writeLock) {
            var at = offset
            val end = offset + length
            while (at < end) {
                if (failed) throw IOException("The link was closed after a record failed")
                val chunk = minOf(end - at, LinkWire.MAX_RECORD_PLAIN)
                val recordLength = chunk + LinkCrypto.AEAD_TAG_LENGTH
                outRecord[0] = recordLength.toByte()
                outRecord[1] = (recordLength shr 8).toByte()
                val nonce = LinkCrypto.nonce(sendPrefix, sent)
                // Spent before anything can fail: a nonce is sealed under once, whatever then happens to the record.
                sent++
                sealer.init(Cipher.ENCRYPT_MODE, sealKey, GCMParameterSpec(TAG_BITS, nonce))
                sealer.updateAAD(outRecord, 0, 2)
                sealer.doFinal(buffer, at, chunk, outRecord, 2)
                try {
                    rawOutput.write(outRecord, 0, 2 + recordLength)
                } catch (e: Exception) {
                    // Closed for good: part of a record may be on the wire, and the peer would read whatever came
                    // next as its continuation.
                    fail()
                    throw e
                }
                at += chunk
            }
        }
    }

    private fun flushRaw() {
        synchronized(writeLock) {
            try {
                rawOutput.flush()
            } catch (e: Exception) {
                fail()
                throw e
            }
        }
    }

    /**
     * Reads and opens the next record into [plain]. False for a clean end, before a record's first byte; a stream
     * that ends inside a record throws [EOFException]. Called under the read lock.
     */
    private fun nextRecord(): Boolean {
        if (failed) throw IOException("The link was closed after a record failed")
        val first = raw.read()
        if (first < 0) return false
        val second = raw.read()
        if (second < 0) throw EOFException("A link record was cut short")
        val length = first or (second shl 8)
        // A record always carries at least one byte: a write never seals an empty one.
        if (length <= LinkCrypto.AEAD_TAG_LENGTH || length > inRecord.size) {
            fail()
            throw IOException("A link record of $length bytes")
        }
        raw.readFully(inRecord, 0, length)
        try {
            val nonce = LinkCrypto.nonce(receivePrefix, received)
            opener.init(Cipher.DECRYPT_MODE, openKey, GCMParameterSpec(TAG_BITS, nonce))
            opener.updateAAD(byteArrayOf(first.toByte(), second.toByte()))
            opener.doFinal(inRecord, 0, length, plain, 0)
        } catch (e: GeneralSecurityException) {
            fail()
            throw IOException("A link record failed its check: forged, replayed, reordered or corrupted", e)
        }
        received++
        plainOffset = 0
        plainCount = length - LinkCrypto.AEAD_TAG_LENGTH
        return true
    }

    private fun fail() {
        failed = true
        for (stream in listOf<Closeable>(raw, rawOutput)) {
            try {
                stream.close()
            } catch (e: IOException) {
                // Already broken; the failure the caller is told about is the one that matters.
            }
        }
    }

    private companion object {
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val TAG_BITS = LinkCrypto.AEAD_TAG_LENGTH * 8
        const val PREFIX_LENGTH = 4
    }
}
