package me.akshitbansal.edgepad.link.blob

import me.akshitbansal.edgepad.link.PermAsk
import me.akshitbansal.edgepad.protocol.AgentToken
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class PermSinkTest {
    private val got = ArrayList<PermAsk>()
    private val sink = PermSink { got.add(it) }

    private fun send(
        kind: BlobKind,
        label: String,
        body: ByteArray,
    ) {
        sink.take(Frame.BlobBegin(kind.id, body.size.toLong(), label))
        body.toList().chunked(255).forEachIndexed { seq, part -> sink.take(Frame.BlobChunk(seq, part.toByteArray())) }
        sink.take(Frame.BlobEnd(sha256(body)))
    }

    private fun body(text: String) = "claude\nBash\n$text".toByteArray()

    @Test
    fun anAskArrivesDecoded() {
        send(BlobKind.PERM_ASK, "a1b2c3d4e5f6/1", body("ls -la"))
        assertEquals(PermAsk("a1b2c3d4e5f6", true, AgentToken.CLAUDE, "Bash", "ls -la"), got.single())
    }

    @Test
    fun aTextOverTheCapComesAsIncomplete() {
        send(BlobKind.PERM_ASK, "a1b2c3d4e5f6/0", body(""))
        assertEquals(false, got.single().complete)
    }

    @Test
    fun aBadLabelOrBodyIsDropped() {
        send(BlobKind.PERM_ASK, "nope", body("x"))
        send(BlobKind.PERM_ASK, "a1b2c3d4e5f6/1", "robot\nBash\nx".toByteArray())
        assertTrue(got.isEmpty())
    }

    @Test
    fun overTheCapAndOtherKindsAreDropped() {
        send(BlobKind.PERM_ASK, "a1b2c3d4e5f6/1", ByteArray(PermSink.MAX_BYTES + 1) { 'a'.code.toByte() })
        send(BlobKind.GITHUB, "a1b2c3d4e5f6/1", body("x"))
        assertTrue(got.isEmpty())
    }

    @Test
    fun hostileTextStaysLiteralAndBidiIsShown() {
        send(BlobKind.PERM_ASK, "a1b2c3d4e5f6/1", body("<b>x</b> \u202e y"))
        assertEquals("<b>x</b> <U+202E> y", got.single().text)
    }
}
