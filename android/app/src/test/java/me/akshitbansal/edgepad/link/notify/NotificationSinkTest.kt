package me.akshitbansal.edgepad.link.notify

import me.akshitbansal.edgepad.link.blob.sha256
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class NotificationSinkTest {
    private val got = ArrayList<List<String>>()
    private var on = true
    private val sink =
        NotificationSink({ on }, NotifyLimiter()) { id, app, title, body -> got.add(listOf(id, app, title, body)) }

    private fun send(
        text: String,
        kind: BlobKind = BlobKind.NOTIFICATION,
    ) {
        val data = text.toByteArray(Charsets.UTF_8)
        sink.take(Frame.BlobBegin(kind.id, data.size.toLong(), "n1"))
        sink.take(Frame.BlobChunk(0, data))
        sink.take(Frame.BlobEnd(sha256(data)))
    }

    @Test
    fun aWellFormedNotificationIsPosted() {
        send("Mail\nHello\nSee you")
        assertEquals(listOf(listOf("n1", "Mail", "Hello", "See you")), got)
    }

    @Test
    fun malformedOrOverCapIsDropped() {
        send("Mail\nonly one newline")
        send("a\nb\nc\nd")
        send("Mail\n${"x".repeat(NotificationSink.MAX_TITLE + 1)}\nbody")
        send("Mail\nt\n${"x".repeat(NotificationSink.MAX_BODY + 1)}")
        send("x".repeat(NotificationSink.MAX_BYTES + 1))
        send("Mail\nt\nb", BlobKind.APP_ICON)
        assertTrue(got.isEmpty())
    }

    @Test
    fun theOffSwitchDropsEverything() {
        on = false
        send("Mail\nHello\nSee you")
        assertTrue(got.isEmpty())
    }
}
