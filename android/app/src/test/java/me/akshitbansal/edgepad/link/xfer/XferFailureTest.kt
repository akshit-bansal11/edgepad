package me.akshitbansal.edgepad.link.xfer

import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.security.MessageDigest

/** Every way a file stops arriving unsaved ends in one `failed` call, so its notification can end. */
class XferFailureTest {
    @get:Rule
    val folder = TemporaryFolder()

    private val replies = ArrayList<Frame.XferStatus>()
    private val failures = ArrayList<Pair<String, XferCode>>()

    private fun receiver(store: XferStore) =
        XferReceiver(store, { replies += it }, failed = { name, code -> failures += name to code }, runner = { it() })

    private fun store(thrown: RuntimeException? = null) = ThrowingStore(folder.newFolder("parts"), thrown)

    private fun codes() = replies.map { it.status }

    @Test
    fun aPublishThatThrowsAnswersWriteFailedAndFreesTheReceiver() {
        val data = ByteArray(8) { it.toByte() }
        for (thrown in listOf(IllegalStateException(), IllegalArgumentException(), SecurityException())) {
            replies.clear()
            failures.clear()
            val receiver = receiver(store(thrown))
            receiver.take(Frame.XferBegin(1, 8uL, "a.bin"))
            receiver.take(Frame.XferChunk(1, 0uL, data))
            receiver.take(Frame.XferEnd(1, MessageDigest.getInstance("SHA-256").digest(data)))
            assertEquals(XferCode.WRITE_FAILED.value, replies.last().status)
            assertEquals(listOf("a.bin" to XferCode.WRITE_FAILED), failures)
            assertFalse(receiver.busy)
        }
    }

    @Test
    fun theLaptopsCancelEndsTheFileAsCancelled() {
        val receiver = receiver(store())
        receiver.take(Frame.XferBegin(1, 8uL, "a.bin"))
        receiver.take(Frame.XferStatus(1, XferCode.CANCELLED.value, 0uL))
        assertEquals(listOf("a.bin" to XferCode.CANCELLED), failures)
        assertFalse(receiver.busy)
    }

    @Test
    fun theUsersStopAndABadHashAndALostSessionEachEndTheFile() {
        val a = receiver(store())
        a.take(Frame.XferBegin(1, 8uL, "stop.bin"))
        assertTrue(a.cancel())
        val b = receiver(store())
        b.take(Frame.XferBegin(2, 4uL, "bad.bin"))
        b.take(Frame.XferChunk(2, 0uL, ByteArray(4)))
        b.take(Frame.XferEnd(2, ByteArray(32)))
        val c = receiver(store())
        c.take(Frame.XferBegin(3, 8uL, "gone.bin"))
        c.close()
        assertEquals(
            listOf(
                "stop.bin" to XferCode.CANCELLED,
                "bad.bin" to XferCode.BAD_HASH,
                "gone.bin" to XferCode.SESSION_ENDED,
            ),
            failures,
        )
    }

    @Test
    fun anOversizedChunkIsRejectedAndEndsTheFile() {
        val receiver = receiver(store())
        receiver.take(Frame.XferBegin(1, 2uL, "a.bin"))
        receiver.take(Frame.XferChunk(1, 0uL, ByteArray(3)))
        assertTrue(codes().contains(XferCode.REJECTED.value))
        assertEquals(listOf("a.bin" to XferCode.REJECTED), failures)
    }

    private class ThrowingStore(
        val parts: File,
        val thrown: RuntimeException?,
    ) : XferStore {
        override fun freeBytes() = Long.MAX_VALUE

        override fun part(
            name: String,
            size: Long,
        ) = File(parts, "$name.$size.part")

        override fun purge(cutoffMs: Long) = Unit

        override fun publish(
            part: File,
            name: String,
        ): String? = thrown?.let { throw it } ?: name
    }
}
