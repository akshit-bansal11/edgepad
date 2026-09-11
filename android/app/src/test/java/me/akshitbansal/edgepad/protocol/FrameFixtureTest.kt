package me.akshitbansal.edgepad.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.io.StreamCorruptedException

/** Runs protocol/frames.txt — the same golden frames the Windows suite runs. */
class FrameFixtureTest {
    private val lines =
        fixture()
            .readLines()
            .filter { it.isNotBlank() && !it.startsWith("#") }

    @Test
    fun fixtureIsNotEmpty() {
        // Guards the loops below from passing vacuously if the file ever comes back empty.
        assertTrue("expected golden frames, found ${lines.size}", lines.size >= 10)
    }

    @Test
    fun encodesToTheGoldenBytes() {
        for (line in lines) {
            val (frame, bytes) = parse(line)
            assertArrayEquals(line, bytes, FrameCodec.encode(frame))
        }
    }

    @Test
    fun decodesTheGoldenBytes() {
        for (line in lines) {
            val (frame, bytes) = parse(line)
            assertEquals(line, frame, FrameCodec.decode(bytes[0].toInt() and 0xFF, bytes.copyOfRange(1, bytes.size)))
        }
    }

    @Test(expected = StreamCorruptedException::class)
    fun rejectsAnUnknownType() {
        FrameCodec.decode(0x7f, ByteArray(0))
    }

    @Test(expected = StreamCorruptedException::class)
    fun rejectsHelloWithoutTheMagic() {
        FrameCodec.decode(0x01, byteArrayOf(0, 0, 0, 0, 1))
    }

    @Test(expected = StreamCorruptedException::class)
    fun rejectsAButtonFlagOtherThanZeroOrOne() {
        FrameCodec.decode(0x11, byteArrayOf(0, 2))
    }

    @Test(expected = StreamCorruptedException::class)
    fun rejectsAShortPayload() {
        FrameCodec.decode(0x10, byteArrayOf(1, 2))
    }

    @Test(expected = StreamCorruptedException::class)
    fun rejectsTextWhoseLengthDoesNotMatchItsHeader() {
        FrameCodec.decode(0x41, byteArrayOf(0, 3, 0x41))
    }

    @Test
    fun longTextIsCutToTheLimitWithoutSplittingACharacter() {
        val bytes = FrameCodec.encode(Frame.Text(0, "a".repeat(254) + "\u00e9"))
        assertEquals(1 + FrameCodec.TEXT_HEADER_LENGTH + 254, bytes.size)
        assertEquals(
            Frame.Text(0, "a".repeat(254)),
            FrameCodec.decode(bytes[0].toInt(), bytes.copyOfRange(1, bytes.size)),
        )
    }

    private fun parse(line: String): Pair<Frame, ByteArray> {
        val (left, right) = line.split("=")
        val fields = left.trim().split(" ").filter { it.isNotEmpty() }
        val bytes =
            right
                .trim()
                .split(" ")
                .map { it.toInt(16).toByte() }
                .toByteArray()

        fun field(i: Int): Long = fields[i].toLong()

        fun int(i: Int): Int = field(i).toInt()

        val frame =
            when (fields[0]) {
                "HELLO" -> Frame.Hello(int(1))
                "HELLO_ACK" -> Frame.HelloAck(int(1))
                "MOVE" -> Frame.Move(int(1), int(2))
                "BUTTON" -> Frame.PointerButton(int(1), int(2) == 1)
                "SCROLL" -> Frame.Scroll(int(1), int(2))
                "ZOOM" -> Frame.Zoom(int(1))
                "ACTION" -> Frame.RunAction(int(1))
                "SET" -> Frame.SetValue(int(1), int(2))
                "PING" -> Frame.Ping(field(1))
                "PONG" -> Frame.Pong(field(1))
                "STATE" -> Frame.StateReport(int(1), int(2), int(3))
                "TEXT" -> Frame.Text(int(1), fields.getOrNull(2) ?: "")
                "KEY" -> Frame.Key(int(1), int(2) == 1)
                else -> error("Unknown fixture frame ${fields[0]}")
            }
        return frame to bytes
    }

    private fun fixture(): File {
        var dir: File? = File("").absoluteFile
        while (dir != null) {
            val candidate = File(dir, "protocol/frames.txt")
            if (candidate.isFile) return candidate
            dir = dir.parentFile
        }
        error("protocol/frames.txt was not found above ${File("").absolutePath}")
    }
}
