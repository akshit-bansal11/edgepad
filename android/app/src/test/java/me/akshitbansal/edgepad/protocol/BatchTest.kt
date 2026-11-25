package me.akshitbansal.edgepad.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.DataInputStream
import java.io.EOFException
import java.io.StreamCorruptedException

/** The writer packs many frames into one buffer; a reader must take them apart again, TEXT included. */
class BatchTest {
    @Test
    fun aMixedBatchRoundTripsFrameByFrame() {
        val frames =
            listOf(
                Frame.Move(5, -3),
                Frame.Text(TextKind.TYPE.id, "h\u00e9llo"),
                Frame.SetValue(0, 55),
                Frame.Ping(1234567890123),
                Frame.Text(TextKind.NOW_PLAYING.id, ""),
            )
        val buffer = ByteArray(FrameCodec.MAX_FRAME_LENGTH * frames.size)
        var length = 0
        for (frame in frames) length += FrameCodec.encode(frame, buffer, length)

        val decoded = ArrayList<Frame>()
        var at = 0
        while (at < length) {
            val type = buffer[at].toInt() and 0xFF
            var payload = FrameCodec.payloadLength(type)
            if (payload ==
                FrameCodec.LENGTH_PREFIXED
            ) {
                payload = FrameCodec.TEXT_HEADER_LENGTH + (buffer[at + 2].toInt() and 0xFF)
            }
            decoded.add(FrameCodec.decode(type, buffer.copyOfRange(at + 1, at + 1 + payload)))
            at += 1 + payload
        }
        assertEquals(frames, decoded)
        assertEquals(length, at)
    }

    @Test
    fun readTakesTheSameBatchOffAStream() {
        val frames =
            listOf(
                Frame.Move(5, -3),
                Frame.Text(TextKind.TYPE.id, "h\u00e9llo"),
                Frame.Ping(1234567890123),
            )
        val bytes = frames.fold(ByteArray(0)) { all, frame -> all + FrameCodec.encode(frame) }
        val input = DataInputStream(ByteArrayInputStream(bytes))

        assertEquals(frames, frames.map { FrameCodec.read(input, "closed") })
        val end = assertThrows(EOFException::class.java) { FrameCodec.read(input, "closed") }
        assertEquals("closed", end.message)
    }

    @Test
    fun readRefusesAnUnknownType() {
        val input = DataInputStream(ByteArrayInputStream(byteArrayOf(0xFF.toByte())))

        assertThrows(StreamCorruptedException::class.java) { FrameCodec.read(input, "closed") }
    }
}
