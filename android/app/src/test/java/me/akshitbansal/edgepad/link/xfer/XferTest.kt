package me.akshitbansal.edgepad.link.xfer

import me.akshitbansal.edgepad.link.Transport
import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

/** The phone's receiver and sender wired back to back in memory, and the receiver alone for what a loopback hides. */
class XferTest {
    @get:Rule
    val folder = TemporaryFolder()

    private val replies = ArrayList<Frame.XferStatus>()
    private val arrived = ArrayList<String>()
    private val wire = ArrayList<Frame>()

    private val parts by lazy { folder.newFolder("parts") }
    private val dest by lazy { folder.newFolder("dest") }
    private val store by lazy { FakeStore(parts, dest) }

    @Test
    fun aFileCrossesWholeAndIsPublishedUnderItsName() {
        val data = bytes(1000)
        val (sender, _) = loopback()
        assertEquals(XferCode.SAVED, sender.send({ data.inputStream() }, "a.bin", data.size.toLong(), 1))
        assertArrayEquals(data, File(dest, "a.bin").readBytes())
        assertEquals(listOf("a.bin"), arrived)
        assertEquals(0, parts.listFiles()!!.size)
    }

    @Test
    fun aClashIsRenamedAndNothingIsOverwritten() {
        File(dest, "a.bin").writeBytes(byteArrayOf(9))
        val data = bytes(10)
        val (sender, _) = loopback()
        assertEquals(XferCode.SAVED, sender.send({ data.inputStream() }, "a.bin", 10, 1))
        assertArrayEquals(byteArrayOf(9), File(dest, "a.bin").readBytes())
        assertArrayEquals(data, File(dest, "a (1).bin").readBytes())
    }

    @Test
    fun aBrokenLinkResumesWhereThePartStopped() {
        val data = bytes(2000)
        val first = XferReceiver(store, { replies += it }, runner = { it() })
        first.take(Frame.XferBegin(1, 2000uL, "r.bin"))
        first.take(Frame.XferChunk(1, 0uL, data.copyOfRange(0, 700)))
        first.close()
        replies.clear()
        val (sender, _) = loopback()
        assertEquals(XferCode.SAVED, sender.send({ data.inputStream() }, "r.bin", 2000, 2))
        assertEquals(700uL, replies.first { it.status == XferCode.READY.value }.offset)
        assertArrayEquals(data, File(dest, "r.bin").readBytes())
    }

    @Test
    fun aWrongHashDeletesThePartAndSaysSo() {
        val receiver = receiver()
        receiver.take(Frame.XferBegin(1, 5uL, "h.bin"))
        receiver.take(Frame.XferChunk(1, 0uL, "hello".toByteArray()))
        receiver.take(Frame.XferEnd(1, ByteArray(32)))
        assertEquals(XferCode.BAD_HASH.value, replies.last().status)
        assertEquals(0, parts.listFiles()!!.size)
        assertFalse(File(dest, "h.bin").exists())
        assertFalse(receiver.busy)
    }

    @Test
    fun aSecondBeginIsBusyAndTheFirstGoesOn() {
        val receiver = receiver()
        receiver.take(Frame.XferBegin(1, 5uL, "one.bin"))
        receiver.take(Frame.XferBegin(2, 5uL, "two.bin"))
        assertEquals(XferCode.BUSY.value, replies.last().status)
        assertEquals(2, replies.last().id)
        assertTrue(receiver.busy)
    }

    @Test
    fun unsafeNamesAndTooBigFilesAreRejectedBeforeAnythingIsCreated() {
        val receiver = receiver()
        receiver.take(Frame.XferBegin(1, 5uL, "CON.txt"))
        assertEquals(XferCode.REJECTED.value, replies.last().status)
        receiver.take(Frame.XferBegin(2, (XferLimits.MAX_BYTES + 1).toULong(), "big.bin"))
        assertEquals(XferCode.REJECTED.value, replies.last().status)
        store.free = 100
        receiver.take(Frame.XferBegin(3, 5uL, "full.bin"))
        assertEquals(XferCode.REJECTED.value, replies.last().status)
        assertEquals(0, parts.listFiles()!!.size)
        assertFalse(receiver.busy)
    }

    @Test
    fun aPathInTheNameNeverEscapesTheFolder() {
        val data = bytes(10)
        val (sender, _) = loopback()
        assertEquals(XferCode.SAVED, sender.send({ data.inputStream() }, "../../etc/passwd", 10, 1))
        assertEquals(listOf("passwd"), dest.list()!!.toList())
    }

    @Test
    fun aChunkOutOfOrderIsAnsweredWithTheBytesHeld() {
        val receiver = receiver()
        receiver.take(Frame.XferBegin(1, 10uL, "o.bin"))
        receiver.take(Frame.XferChunk(1, 0uL, bytes(4)))
        receiver.take(Frame.XferChunk(1, 8uL, bytes(2)))
        assertEquals(XferCode.PROGRESS.value, replies.last().status)
        assertEquals(4uL, replies.last().offset)
    }

    @Test
    fun cancelDeletesThePartAndTellsTheLaptop() {
        val receiver = receiver()
        receiver.take(Frame.XferBegin(1, 10uL, "c.bin"))
        receiver.take(Frame.XferChunk(1, 0uL, bytes(4)))
        assertTrue(receiver.cancel())
        assertEquals(XferCode.CANCELLED.value, replies.last().status)
        assertEquals(0, parts.listFiles()!!.size)
        assertFalse(receiver.cancel())
    }

    @Test
    fun theLaptopsCancelDropsThePartWithoutAnswering() {
        val receiver = receiver()
        receiver.take(Frame.XferBegin(1, 10uL, "c.bin"))
        val before = replies.size
        assertTrue(receiver.take(Frame.XferStatus(1, XferCode.CANCELLED.value, 0uL)))
        assertEquals(before, replies.size)
        assertEquals(0, parts.listFiles()!!.size)
    }

    @Test
    fun oldPartsAreDeletedWhenTheReceiverIsMade() {
        val old = File(parts, "x.5.part").apply { writeBytes(byteArrayOf(1)) }
        old.setLastModified(1_000L)
        XferReceiver(store, { replies += it }, now = { XferLimits.PARTIAL_KEPT_MS + 5_000L })
        assertFalse(old.exists())
    }

    @Test
    fun theSenderCancelsWhenAskedAndTellsTheLaptop() {
        val sender = sender()
        var polls = 0
        val code = sender.send({ bytes(10).inputStream() }, "n.bin", 10, 1, cancelled = { ++polls > 1 })
        assertEquals(XferCode.CANCELLED, code)
        assertEquals(XferCode.CANCELLED.value, (wire.last() as Frame.XferStatus).status)
    }

    @Test
    fun theSenderGivesUpWhenNoAnswerComes() {
        var now = 0L
        val clock = {
            now += 600
            now
        }
        val sender = XferSender({ wire += it }, Transport.WIFI, { 0L }, { false }, 1_000L, clock)
        assertEquals(XferCode.TIMED_OUT, sender.send({ bytes(10).inputStream() }, "n.bin", 10, 1))
    }

    @Test
    fun theSenderStopsWhenTheLinkEnds() {
        val sender = XferSender({ wire += it }, Transport.WIFI, { 0L }, { true })
        assertEquals(XferCode.SESSION_ENDED, sender.send({ bytes(10).inputStream() }, "n.bin", 10, 1))
    }

    @Test
    fun theSenderRefusesAnUnsafeNameAndTooBigAFile() {
        val sender = sender()
        assertEquals(XferCode.REJECTED, sender.send({ bytes(1).inputStream() }, "NUL", 1, 1))
        assertEquals(XferCode.REJECTED, sender.send({ bytes(1).inputStream() }, "a", XferLimits.MAX_BYTES + 1, 1))
        assertTrue(wire.isEmpty())
    }

    @Test
    fun theSenderPausesAfterABudgetOfChunks() {
        var pauses = 0
        val (sender, _) = loopback { pauses++ >= 0 }
        val data = bytes(300 * 247)
        assertEquals(XferCode.SAVED, sender.send({ data.inputStream() }, "p.bin", data.size.toLong(), 1))
        assertTrue(pauses >= 1)
    }

    @Test
    fun theProgressGateLetsOnePercentThrough() {
        val gate = PercentGate()
        assertTrue(gate.pass(0, 1000))
        assertFalse(gate.pass(5, 1000))
        assertTrue(gate.pass(10, 1000))
        assertFalse(gate.pass(19, 1000))
        assertTrue(gate.pass(1000, 1000))
    }

    private var replyTo: XferSender? = null

    private fun receiver() = XferReceiver(store, { replies += it }, { arrived += it }, runner = { it() })

    private fun sender() = XferSender({ wire += it }, Transport.WIFI, { 0L }, { false }, pause = { true })

    // A sender and a receiver whose frames go straight to each other on the calling thread.
    private fun loopback(pause: (Int) -> Boolean = { true }): Pair<XferSender, XferReceiver> {
        val receiver =
            XferReceiver(
                store,
                { status ->
                    replies += status
                    replyTo?.take(status)
                },
                { arrived += it },
                runner = { it() },
            )
        val sender =
            XferSender(
                { frame -> receiver.take(frame) },
                Transport.WIFI,
                { 0L },
                { false },
                pause = pause,
            )
        replyTo = sender
        return sender to receiver
    }

    private fun bytes(n: Int) = ByteArray(n) { (it * 31 + 7).toByte() }

    private class FakeStore(
        val parts: File,
        val dest: File,
        var free: Long = Long.MAX_VALUE,
    ) : XferStore {
        override fun freeBytes() = free

        override fun part(
            name: String,
            size: Long,
        ) = File(parts, "$name.$size.part")

        override fun purge(cutoffMs: Long) {
            parts.listFiles()?.filter { it.lastModified() < cutoffMs }?.forEach { it.delete() }
        }

        override fun publish(
            part: File,
            name: String,
        ): String {
            val final = SafeName.unique(name) { File(dest, it).exists() }
            part.copyTo(File(dest, final))
            part.delete()
            return final
        }
    }
}
