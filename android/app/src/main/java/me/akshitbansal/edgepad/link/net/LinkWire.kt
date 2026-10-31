package me.akshitbansal.edgepad.link.net

import java.io.DataInputStream
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream

/**
 * The cleartext half of the network link, EDGL1, as the phone speaks it: the messages it sends before anything is
 * encrypted, and the laptop's replies, ported from `shared/Edgepad.Core/Net/Link/LinkWire.cs`. Every message is
 * `u16 length | body`, little-endian, at most [MAX_MESSAGE] bytes of body, so a laptop that never authenticates
 * can never make this side allocate more than that. A malformed message is an [IOException], as a dropped
 * connection is, and the caller closes.
 */
object LinkWire {
    const val LINK_VERSION: Byte = 1

    const val MODE_INFO: Byte = 0
    const val MODE_PAIR: Byte = 1
    const val MODE_RESUME: Byte = 2

    const val STATUS_OK = 0

    /** Another pairing is under way on the laptop, or pairing is locked after failed attempts. */
    const val STATUS_BUSY = 1

    /** This phone's link version is not the laptop's. */
    const val STATUS_UNSUPPORTED = 2

    /** A resume the laptop holds no pairing key for: the phone should offer to pair again. */
    const val STATUS_UNKNOWN_PHONE = 3

    const val MAX_MESSAGE = 512
    const val MAX_RECORD_PLAIN = 16384

    const val ID_LENGTH = 16
    const val NONCE_LENGTH = 16
    const val PUBLIC_KEY_LENGTH = 65
    const val HASH_LENGTH = 32
    const val MAX_NAME_BYTES = 255

    private val MAGIC = "EDGL".toByteArray()

    /** Sends one message and flushes it. */
    fun write(
        output: OutputStream,
        body: ByteArray,
    ) {
        require(body.size <= MAX_MESSAGE) { "A link message is at most $MAX_MESSAGE bytes" }
        output.write(byteArrayOf(body.size.toByte(), (body.size shr 8).toByte()) + body)
        output.flush()
    }

    /** The next message's body. A stream that ends throws [java.io.EOFException]. */
    fun read(input: InputStream): ByteArray {
        val data = DataInputStream(input)
        val low = data.readUnsignedByte()
        val length = low or (data.readUnsignedByte() shl 8)
        if (length > MAX_MESSAGE) throw IOException("A link message of $length bytes, over the $MAX_MESSAGE allowed")
        val body = ByteArray(length)
        data.readFully(body)
        return body
    }

    fun infoRequest(): ByteArray = request(MODE_INFO)

    fun pairRequest(
        phoneId: ByteArray,
        name: String,
        commit: ByteArray,
    ): ByteArray = request(MODE_PAIR) + exact(phoneId, ID_LENGTH) + nameField(name) + exact(commit, HASH_LENGTH)

    fun resumeRequest(
        phoneId: ByteArray,
        publicKey: ByteArray,
        nonce: ByteArray,
    ): ByteArray =
        request(MODE_RESUME) + exact(phoneId, ID_LENGTH) + exact(publicKey, PUBLIC_KEY_LENGTH) +
            exact(nonce, NONCE_LENGTH)

    /** The phone's key and nonce, sent only once the laptop's are on the wire: they open its commitment. */
    fun reveal(
        publicKey: ByteArray,
        nonce: ByteArray,
    ): ByteArray = exact(publicKey, PUBLIC_KEY_LENGTH) + exact(nonce, NONCE_LENGTH)

    /** A key-confirmation tag on its own, as both sides send one. */
    fun tag(tag: ByteArray): ByteArray = exact(tag, HASH_LENGTH)

    fun decodeTag(body: ByteArray): ByteArray {
        val r = Reader(body)
        val tag = r.bytes(HASH_LENGTH)
        r.end()
        return tag
    }

    /** A laptop's reply: [LinkReply.status] alone for a refusal, otherwise the fields of the reply to [mode]. */
    fun decodeReply(
        mode: Byte,
        body: ByteArray,
    ): LinkReply {
        val r = Reader(body)
        val status = r.byte()
        val reply =
            when {
                status != STATUS_OK -> {
                    LinkReply(status)
                }

                mode == MODE_INFO -> {
                    LinkReply(
                        status,
                        laptopId = r.bytes(ID_LENGTH),
                        protocolVersion = r.byte(),
                        nameField = r.nameField(),
                    )
                }

                mode == MODE_PAIR -> {
                    LinkReply(
                        status,
                        laptopId = r.bytes(ID_LENGTH),
                        nameField = r.nameField(),
                        publicKey = r.bytes(PUBLIC_KEY_LENGTH),
                        nonce = r.bytes(NONCE_LENGTH),
                    )
                }

                mode == MODE_RESUME -> {
                    LinkReply(
                        status,
                        publicKey = r.bytes(PUBLIC_KEY_LENGTH),
                        nonce = r.bytes(NONCE_LENGTH),
                        tag = r.bytes(HASH_LENGTH),
                    )
                }

                else -> {
                    throw IllegalArgumentException("Unknown link mode $mode")
                }
            }
        r.end()
        return reply
    }

    /**
     * A display name as the wire carries it: a one-byte length, then UTF-8 cut at a character boundary to fit.
     * Both names go into the pairing transcript in exactly this form, so a relay that changes either changes the code.
     */
    fun nameField(name: String): ByteArray {
        val utf8 = name.toByteArray()
        var length = minOf(utf8.size, MAX_NAME_BYTES)
        while (length < utf8.size && length > 0 && (utf8[length].toInt() and 0xC0) == 0x80) length--
        return byteArrayOf(length.toByte()) + utf8.copyOf(length)
    }

    private fun request(mode: Byte): ByteArray = MAGIC + byteArrayOf(LINK_VERSION, mode)

    private fun exact(
        value: ByteArray,
        length: Int,
    ): ByteArray {
        require(value.size == length) { "Expected $length bytes, got ${value.size}" }
        return value
    }

    private class Reader(
        private val body: ByteArray,
    ) {
        private var at = 0

        fun bytes(count: Int): ByteArray {
            if (body.size - at < count) throw IOException("A link message ended early")
            val value = body.copyOfRange(at, at + count)
            at += count
            return value
        }

        fun byte(): Int = bytes(1)[0].toInt() and 0xFF

        /** A name with its length byte, exactly as sent. */
        fun nameField(): ByteArray {
            val length = byte()
            return byteArrayOf(length.toByte()) + bytes(length)
        }

        fun end() {
            if (at != body.size) throw IOException("A link message ran on past its fields")
        }
    }
}

/** A laptop's reply as the phone decodes it; fields its mode does not carry are empty. */
class LinkReply(
    val status: Int,
    val laptopId: ByteArray = ByteArray(0),
    val protocolVersion: Int = 0,
    val nameField: ByteArray = ByteArray(0),
    val publicKey: ByteArray = ByteArray(0),
    val nonce: ByteArray = ByteArray(0),
    val tag: ByteArray = ByteArray(0),
) {
    /** The laptop's name, decoded from [nameField]. */
    val name: String = if (nameField.isEmpty()) "" else String(nameField, 1, nameField.size - 1, Charsets.UTF_8)
}
