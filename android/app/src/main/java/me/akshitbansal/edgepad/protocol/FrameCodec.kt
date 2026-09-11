package me.akshitbansal.edgepad.protocol

import java.io.StreamCorruptedException
import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * Frame layout: one type byte, then a payload whose length is fixed by the type, except TEXT, whose two
 * header bytes (kind, length) say how much UTF-8 follows. Little-endian. protocol/frames.txt holds the
 * golden bytes both apps are tested against.
 */
object FrameCodec {
    const val MAX_TEXT_BYTES = 255
    const val TEXT_HEADER_LENGTH = 2
    const val MAX_FRAME_LENGTH = 1 + TEXT_HEADER_LENGTH + MAX_TEXT_BYTES

    /** What [payloadLength] returns for TEXT: read the header, then as many bytes as it says. */
    const val LENGTH_PREFIXED = -2

    private const val HELLO = 0x01
    private const val HELLO_ACK = 0x02
    private const val MOVE = 0x10
    private const val BUTTON = 0x11
    private const val SCROLL = 0x12
    private const val ZOOM = 0x13
    private const val RUN_ACTION = 0x20
    private const val SET_VALUE = 0x21
    private const val PING = 0x30
    private const val PONG = 0x31
    private const val STATE = 0x40
    private const val TEXT = 0x41

    /** Payload length for a type byte, [LENGTH_PREFIXED] for TEXT, or -1 when the type is unknown. */
    fun payloadLength(type: Int): Int =
        when (type) {
            HELLO -> 5
            HELLO_ACK, RUN_ACTION -> 1
            BUTTON, ZOOM, SET_VALUE -> 2
            STATE -> 3
            MOVE, SCROLL -> 4
            PING, PONG -> 8
            TEXT -> LENGTH_PREFIXED
            else -> -1
        }

    fun encode(frame: Frame): ByteArray {
        val out = ByteArray(MAX_FRAME_LENGTH)
        return out.copyOf(encode(frame, out, 0))
    }

    /** Writes [frame] into [out] at [offset] and returns the number of bytes written. */
    fun encode(
        frame: Frame,
        out: ByteArray,
        offset: Int,
    ): Int {
        val b = ByteBuffer.wrap(out, offset, minOf(MAX_FRAME_LENGTH, out.size - offset)).order(ByteOrder.LITTLE_ENDIAN)
        when (frame) {
            is Frame.Hello -> {
                b.type(HELLO).put(magicBytes).u8(frame.version)
            }

            is Frame.HelloAck -> {
                b.type(HELLO_ACK).u8(frame.version)
            }

            is Frame.Move -> {
                b.type(MOVE).i16(frame.dx).i16(frame.dy)
            }

            is Frame.PointerButton -> {
                b.type(BUTTON).u8(frame.id).u8(if (frame.down) 1 else 0)
            }

            is Frame.Scroll -> {
                b.type(SCROLL).i16(frame.dx).i16(frame.dy)
            }

            is Frame.Zoom -> {
                b.type(ZOOM).i16(frame.delta)
            }

            is Frame.RunAction -> {
                b.type(RUN_ACTION).u8(frame.id)
            }

            is Frame.SetValue -> {
                b.type(SET_VALUE).u8(frame.control).u8(frame.value)
            }

            is Frame.Ping -> {
                b.type(PING).putLong(frame.time)
            }

            is Frame.Pong -> {
                b.type(PONG).putLong(frame.time)
            }

            is Frame.StateReport -> {
                b
                    .type(STATE)
                    .u8(frame.control)
                    .u8(frame.value)
                    .u8(frame.flags)
            }

            is Frame.Text -> {
                val bytes = truncate(frame.text).toByteArray(Charsets.UTF_8)
                b
                    .type(TEXT)
                    .u8(frame.kind)
                    .u8(bytes.size)
                    .put(bytes)
            }
        }
        return b.position() - offset
    }

    /** Decodes one frame. Anything malformed throws, and the caller drops the connection. */
    fun decode(
        type: Int,
        payload: ByteArray,
    ): Frame {
        val expected = payloadLength(type)
        if (expected == LENGTH_PREFIXED) return decodeText(payload)
        if (expected < 0) throw StreamCorruptedException("Unknown frame type 0x%02x".format(type))
        if (payload.size != expected) {
            throw StreamCorruptedException(
                "Frame 0x%02x needs $expected payload bytes, got ${payload.size}".format(type),
            )
        }
        val b = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN)
        return when (type) {
            HELLO -> decodeHello(b)
            HELLO_ACK -> Frame.HelloAck(b.u8())
            MOVE -> Frame.Move(b.short.toInt(), b.short.toInt())
            BUTTON -> Frame.PointerButton(b.u8(), flag(b.u8()))
            SCROLL -> Frame.Scroll(b.short.toInt(), b.short.toInt())
            ZOOM -> Frame.Zoom(b.short.toInt())
            RUN_ACTION -> Frame.RunAction(b.u8())
            SET_VALUE -> Frame.SetValue(b.u8(), b.u8())
            PING -> Frame.Ping(b.long)
            PONG -> Frame.Pong(b.long)
            STATE -> Frame.StateReport(b.u8(), b.u8(), b.u8())
            else -> throw StreamCorruptedException("Unknown frame type 0x%02x".format(type))
        }
    }

    /** Cuts text to [MAX_TEXT_BYTES] of UTF-8 without splitting a character. */
    fun truncate(text: String): String {
        var t = text
        while (t.toByteArray(Charsets.UTF_8).size > MAX_TEXT_BYTES) t = t.dropLast(1)
        return t
    }

    private val magicBytes = ProtocolConstants.MAGIC.toByteArray(Charsets.US_ASCII)

    private fun decodeText(payload: ByteArray): Frame.Text {
        if (payload.size < TEXT_HEADER_LENGTH || payload.size != TEXT_HEADER_LENGTH + (payload[1].toInt() and 0xFF)) {
            throw StreamCorruptedException("TEXT length does not match its header")
        }
        val text = String(payload, TEXT_HEADER_LENGTH, payload.size - TEXT_HEADER_LENGTH, Charsets.UTF_8)
        return Frame.Text(payload[0].toInt() and 0xFF, text)
    }

    private fun decodeHello(b: ByteBuffer): Frame.Hello {
        val magic = ByteArray(magicBytes.size).also { b.get(it) }
        if (!magic.contentEquals(magicBytes)) {
            throw StreamCorruptedException("HELLO without the Edgepad magic")
        }
        return Frame.Hello(b.u8())
    }

    private fun flag(value: Int): Boolean =
        when (value) {
            0 -> false
            1 -> true
            else -> throw StreamCorruptedException("Flag byte must be 0 or 1, got $value")
        }

    private fun ByteBuffer.type(type: Int): ByteBuffer = put(type.toByte())

    private fun ByteBuffer.u8(value: Int): ByteBuffer {
        require(value in 0..0xFF) { "u8 out of range: $value" }
        return put(value.toByte())
    }

    private fun ByteBuffer.i16(value: Int): ByteBuffer {
        require(value in Short.MIN_VALUE..Short.MAX_VALUE) { "i16 out of range: $value" }
        return putShort(value.toShort())
    }

    private fun ByteBuffer.u8(): Int = get().toInt() and 0xFF
}
