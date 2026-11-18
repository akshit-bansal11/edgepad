package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardContent
import me.akshitbansal.edgepad.link.blob.ClipboardSurface
import me.akshitbansal.edgepad.protocol.BlobKind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.concurrent.thread

class ClipboardHistoryTest {
    private fun ClipboardHistory.texts() = items.map { it.text }

    @Test
    fun keepsTenNewestFirst() {
        val h = ClipboardHistory()
        (1..12).forEach { h.record("t$it", ClipSide.LAPTOP) }
        assertEquals(10, h.items.size)
        assertEquals("t12", h.items.first().text)
        assertEquals("t3", h.items.last().text)
    }

    @Test
    fun aRepeatMovesToTheFrontAndTakesTheNewSide() {
        val h = ClipboardHistory()
        h.record("a", ClipSide.LAPTOP)
        h.record("b", ClipSide.LAPTOP)
        h.record("a", ClipSide.PHONE)
        assertEquals(listOf("a", "b"), h.texts())
        assertEquals(ClipSide.PHONE, h.items.first().side)
    }

    @Test
    fun timeComesFromTheClock() {
        var now = 1_000L
        val h = ClipboardHistory { now }
        h.record("a", ClipSide.LAPTOP)
        now = 5_000L
        h.record("b", ClipSide.PHONE)
        assertEquals(listOf(5_000L, 1_000L), h.items.map { it.at })
        now = 9_000L
        h.record("a", ClipSide.PHONE)
        assertEquals(listOf("a", "b"), h.texts())
        assertEquals(9_000L, h.items.first().at)
    }

    @Test
    fun byteCapAcceptsEightKiBAndSkipsOneMore() {
        val h = ClipboardHistory()
        h.record("x".repeat(ClipboardHistory.MAX_BYTES), ClipSide.LAPTOP)
        h.record("y".repeat(ClipboardHistory.MAX_BYTES + 1), ClipSide.LAPTOP)
        assertEquals(1, h.items.size)
        assertTrue(h.items.single().text.startsWith("x"))
    }

    @Test
    fun capCountsBytesNotCharacters() {
        val h = ClipboardHistory()
        h.record("é".repeat(ClipboardHistory.MAX_BYTES / 2 + 1), ClipSide.LAPTOP)
        assertTrue(h.items.isEmpty())
    }

    @Test
    fun blankIsIgnoredAndClearEmpties() {
        val h = ClipboardHistory()
        h.record("", ClipSide.LAPTOP)
        h.record("  \n", ClipSide.LAPTOP)
        assertTrue(h.items.isEmpty())
        h.record("a", ClipSide.LAPTOP)
        h.clear()
        assertTrue(h.items.isEmpty())
    }

    @Test
    fun twoThreadsRecordingLeaveTenItems() {
        val h = ClipboardHistory()
        val workers =
            listOf("a", "b").map { tag ->
                thread { repeat(500) { h.record("$tag$it", ClipSide.PHONE) } }
            }
        workers.forEach { it.join() }
        assertEquals(ClipboardHistory.MAX_ITEMS, h.items.size)
    }

    private class Fake(
        private val next: ClipboardContent?,
    ) : ClipboardSurface {
        val written = ArrayList<ClipboardContent>()

        override fun read(): ClipboardContent? = next

        override fun write(content: ClipboardContent) {
            written.add(content)
        }
    }

    @Test
    fun recordingSurfaceNotesLaptopTextOnlyAndDelegates() {
        val inner = Fake(null)
        val h = ClipboardHistory()
        val surface = RecordingSurface(inner, h)
        surface.write(ClipboardContent(BlobKind.CLIP_TEXT, "hello".toByteArray()))
        surface.write(ClipboardContent(BlobKind.CLIP_IMAGE, byteArrayOf(1, 2, 3)))
        assertEquals(listOf("hello"), h.texts())
        assertEquals(ClipSide.LAPTOP, h.items.single().side)
        assertEquals(2, inner.written.size)
    }

    @Test
    fun readNotesThePhonesOwnTextAndReturnsItUntouched() {
        val content = ClipboardContent(BlobKind.CLIP_TEXT, "mine".toByteArray())
        val h = ClipboardHistory()
        assertSame(content, RecordingSurface(Fake(content), h).read())
        assertEquals(ClipSide.PHONE, h.items.single().side)
    }

    @Test
    fun readSkipsSensitiveAndImages() {
        val h = ClipboardHistory()
        val secret = ClipboardContent(BlobKind.CLIP_TEXT, "pw".toByteArray(), sensitive = true)
        assertSame(secret, RecordingSurface(Fake(secret), h).read())
        RecordingSurface(Fake(ClipboardContent(BlobKind.CLIP_IMAGE, byteArrayOf(1))), h).read()
        assertTrue(h.items.isEmpty())
    }
}
