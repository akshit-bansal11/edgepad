package me.akshitbansal.edgepad.link.blob

import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class MediaIconSinkTest {
    private val got = ArrayList<Pair<String, Int>>()
    private val sink = MediaIconSink { label, data -> got.add(label to data.size) }

    private fun send(
        kind: BlobKind,
        label: String,
        size: Int,
    ) {
        val data = ByteArray(size) { 7 }
        sink.take(Frame.BlobBegin(kind.id, size.toLong(), label))
        sink.take(Frame.BlobChunk(0, data))
        sink.take(Frame.BlobEnd(sha256(data)))
    }

    @Test
    fun anIconArrivesWithItsLabel() {
        send(BlobKind.MEDIA_ICON, "Spotify", 100)
        assertEquals(listOf("Spotify" to 100), got)
    }

    @Test
    fun otherKindsAndOversizeAreDropped() {
        send(BlobKind.APP_ICON, "x", 100)
        send(BlobKind.MEDIA_ICON, "big", MediaIconSink.MAX_BYTES + 1)
        assertTrue(got.isEmpty())
    }
}
