package me.akshitbansal.edgepad.protocol

import java.io.StreamCorruptedException
import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * Frame layout: one type byte, then a payload whose length is fixed by the type. Little-endian.
 * protocol/frames.txt holds the golden bytes both apps are tested against.
 */
object FrameCodec {
    const val MAX_FRAME_LENGTH = 9

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

    /** Payload length for a type byte, or -1 when the type is unknown. */
    fun payloadLength(type: Int): Int =
        when (type) {
            HELLO -> 5
            HELLO_ACK, RUN_ACTION -> 1
            BUTTON, ZOOM, SET_VALUE -> 2
            STATE -> 3
            MOVE, SCROLL -> 4
            PING, PONG -> 8
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
        val b = ByteBuffer.wrap(out, offset, MAX_FRAME_LENGTH).order(ByteOrder.LITTLE_ENDIAN)
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
        }
        return b.position() - offset
    }

    /** Decodes one frame. Anything malformed throws, and the caller drops the connection. */
    fun decode(
        type: Int,
        payload: ByteArray,
    ): Frame {
        val expected = payloadLength(type)
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
            else -> Frame.StateReport(b.u8(), b.u8(), b.u8())
        }
    }

    private val magicBytes = ProtocolConstants.MAGIC.toByteArray(Charsets.US_ASCII)

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
