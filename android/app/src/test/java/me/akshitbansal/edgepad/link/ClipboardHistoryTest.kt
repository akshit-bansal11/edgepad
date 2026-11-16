package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardContent
import me.akshitbansal.edgepad.link.blob.ClipboardSurface
import me.akshitbansal.edgepad.protocol.BlobKind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ClipboardHistoryTest {
    @Test
    fun keepsTenNewestFirst() {
        val h = ClipboardHistory()
        (1..12).forEach { h.record("t$it") }
        assertEquals(10, h.items.size)
        assertEquals("t12", h.items.first())
        assertEquals("t3", h.items.last())
    }

    @Test
    fun aRepeatMovesToTheFront() {
        val h = ClipboardHistory()
        h.record("a")
        h.record("b")
        h.record("a")
        assertEquals(listOf("a", "b"), h.items)
    }

    @Test
    fun byteCapAcceptsEightKiBAndSkipsOneMore() {
        val h = ClipboardHistory()
        h.record("x".repeat(ClipboardHistory.MAX_BYTES))
        h.record("y".repeat(ClipboardHistory.MAX_BYTES + 1))
        assertEquals(1, h.items.size)
        assertTrue(h.items.single().startsWith("x"))
    }

    @Test
    fun capCountsBytesNotCharacters() {
        val h = ClipboardHistory()
        h.record("é".repeat(ClipboardHistory.MAX_BYTES / 2 + 1))
        assertTrue(h.items.isEmpty())
    }

    @Test
    fun blankIsIgnoredAndClearEmpties() {
        val h = ClipboardHistory()
        h.record("")
        h.record("  \n")
        assertTrue(h.items.isEmpty())
        h.record("a")
        h.clear()
        assertTrue(h.items.isEmpty())
    }

    @Test
    fun recordingSurfaceNotesTextOnlyAndDelegates() {
        val written = ArrayList<ClipboardContent>()
        val inner =
            object : ClipboardSurface {
                override fun read(): ClipboardContent? = null

                override fun write(content: ClipboardContent) {
                    written.add(content)
                }
            }
        val h = ClipboardHistory()
        val surface = RecordingSurface(inner, h)
        surface.write(ClipboardContent(BlobKind.CLIP_TEXT, "hello".toByteArray()))
        surface.write(ClipboardContent(BlobKind.CLIP_IMAGE, byteArrayOf(1, 2, 3)))
        assertEquals(listOf("hello"), h.items)
        assertEquals(2, written.size)
    }
}
