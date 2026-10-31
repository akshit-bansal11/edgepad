package me.akshitbansal.edgepad.link.blob

import me.akshitbansal.edgepad.link.Transport
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.FrameCodec
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException
import java.nio.ByteBuffer

/**
 * The blob lane and the clipboard's rules, on a fake clock and a fake clipboard, so nothing here sleeps. The same cases
 * as the shared core's BlobTests. Everything a laptop sends is untrusted: each refusal must leave the receiver able to
 * take the next blob.
 */
class BlobTest {
    private val a = "copied on the laptop".toByteArray()
    private val b = "copied on the phone".toByteArray()

    private val wire = ArrayList<Frame>()
    private val received = ArrayList<Pair<Int, ByteArray>>()
    private val pending = ArrayList<() -> Unit>()
    private val surface = FakeClipboard()
    private var now = 1_000_000L
    private var lastInput = 0L
    private var toggle = true
    private val receiver = BlobReceiver(::takesClipboardKinds, ::keep)
    private val sync = syncOver(sender())

    @Test
    fun aBlobCrossesWhole() {
        for (length in listOf(0, 1, 255, 256, 1024 * 1024)) {
            wire.clear()
            received.clear()
            val data = payload(length)

            assertTrue(sender(network = true).send(BlobKind.CLIP_TEXT, "clip", data))

            val chunks = (length + FrameCodec.MAX_CHUNK_BYTES - 1) / FrameCodec.MAX_CHUNK_BYTES
            assertEquals(chunks, wire.count { it is Frame.BlobChunk })
            wire.forEach { assertTrue(receiver.take(it)) }
            assertEquals(BlobKind.CLIP_TEXT.id, received.single().first)
            assertArrayEquals(data, received.single().second)
        }
    }

    @Test
    fun aWrongHashIsRefused() {
        val data = payload(10)

        assertTrue(receiver.take(Frame.BlobBegin(0, 10, "")))
        assertTrue(receiver.take(Frame.BlobChunk(0, data)))
        assertFalse(receiver.take(Frame.BlobEnd(ByteArray(SHA256_BYTES))))

        assertStillTakesABlob()
    }

    @Test
    fun aChunkOutOfOrderDropsTheBlobAndItsTail() {
        assertTrue(receiver.take(Frame.BlobBegin(0, 20, "")))
        assertFalse(receiver.take(Frame.BlobChunk(1, payload(10))))
        assertTrue(receiver.take(Frame.BlobChunk(0, payload(10))))
        assertTrue(receiver.take(Frame.BlobEnd(sha256(payload(20)))))

        assertStillTakesABlob()
    }

    @Test
    fun theSequenceWrapsAt256() {
        val data = payload(300 * FrameCodec.MAX_CHUNK_BYTES)

        assertTrue(sender(network = true).send(BlobKind.CLIP_TEXT, "", data))

        wire.forEach { assertTrue(receiver.take(it)) }
        assertArrayEquals(data, received.single().second)
    }

    @Test
    fun fewerBytesThanThePromisedSizeAreRefused() {
        val data = payload(10)

        assertTrue(receiver.take(Frame.BlobBegin(0, 11, "")))
        assertTrue(receiver.take(Frame.BlobChunk(0, data)))
        assertFalse(receiver.take(Frame.BlobEnd(sha256(data))))

        assertStillTakesABlob()
    }

    @Test
    fun moreBytesThanThePromisedSizeAreRefused() {
        assertTrue(receiver.take(Frame.BlobBegin(0, 9, "")))
        assertFalse(receiver.take(Frame.BlobChunk(0, payload(10))))
        assertTrue(receiver.take(Frame.BlobEnd(sha256(payload(10)))))

        assertStillTakesABlob()
    }

    @Test
    fun anEmptyChunkIsRefused() {
        assertTrue(receiver.take(Frame.BlobBegin(0, 1, "")))
        assertFalse(receiver.take(Frame.BlobChunk(0, ByteArray(0))))

        assertStillTakesABlob()
    }

    @Test
    fun aSizeOverTheCapIsRefusedBeforeAByteIsKept() {
        for (size in listOf(ClipboardSync.MAX_TEXT_BYTES + 1, 0xFFFFFFFFL)) {
            assertFalse(receiver.take(Frame.BlobBegin(0, size, "")))
            assertTrue(receiver.take(Frame.BlobChunk(0, payload(10))))
            assertTrue(receiver.take(Frame.BlobEnd(ByteArray(SHA256_BYTES))))
        }

        assertStillTakesABlob()
    }

    @Test
    fun aLabelWithControlCharactersIsRefused() {
        assertFalse(receiver.take(Frame.BlobBegin(0, 1, "a\nb")))

        assertStillTakesABlob()
    }

    @Test
    fun aSecondBeginAbortsTheFirst() {
        val data = payload(10)

        assertTrue(receiver.take(Frame.BlobBegin(0, 20, "")))
        assertTrue(receiver.take(Frame.BlobChunk(0, payload(5))))
        assertTrue(receiver.take(Frame.BlobBegin(1, 10, "")))
        assertTrue(receiver.take(Frame.BlobChunk(0, data)))
        assertTrue(receiver.take(Frame.BlobEnd(sha256(data))))

        assertEquals(1, received.single().first)
        assertArrayEquals(data, received.single().second)
    }

    @Test
    fun anUnknownKindIsRefusedOnceAndReadToItsEnd() {
        assertFalse(receiver.take(Frame.BlobBegin(200, 10, "")))
        assertTrue(receiver.take(Frame.BlobChunk(0, payload(10))))
        assertTrue(receiver.take(Frame.BlobEnd(sha256(payload(10)))))

        assertTrue(received.isEmpty())
        assertStillTakesABlob()
    }

    @Test
    fun framesWithNoBlobOpenAreRefused() {
        assertFalse(receiver.take(Frame.BlobChunk(0, payload(1))))
        assertFalse(receiver.take(Frame.BlobEnd(sha256(payload(1)))))

        assertStillTakesABlob()
    }

    @Test
    fun aConnectionHasAByteBudget() {
        val chunk = Frame.BlobChunk(0, ByteArray(FrameCodec.MAX_CHUNK_BYTES))
        receiver.take(Frame.BlobBegin(200, 0, ""))
        repeat((BlobReceiver.MAX_CONNECTION_BYTES / FrameCodec.MAX_CHUNK_BYTES).toInt()) { receiver.take(chunk) }

        val left = BlobReceiver.MAX_CONNECTION_BYTES % FrameCodec.MAX_CHUNK_BYTES
        assertFalse(receiver.take(Frame.BlobBegin(0, left + 1, "")))
        assertTrue(receiver.take(Frame.BlobBegin(0, left, "")))
    }

    @Test
    fun onlyBlobFramesGoToTheBlobSink() {
        assertTrue(isBlob(Frame.BlobBegin(0, 1, "")))
        assertTrue(isBlob(Frame.BlobChunk(0, payload(1))))
        assertTrue(isBlob(Frame.BlobEnd(ByteArray(SHA256_BYTES))))
        assertFalse(isBlob(Frame.FileEnd(ByteArray(SHA256_BYTES))))
    }

    @Test
    fun aQuietLinkSendsTheBudgetPerTick() {
        val links = listOf(false to BlobSender.BLUETOOTH_CHUNKS_PER_TICK, true to BlobSender.NETWORK_CHUNKS_PER_TICK)
        for ((network, budget) in links) {
            wire.clear()
            val atEachTick = ArrayList<Int>()
            val paced =
                sender(network) { ms ->
                    assertEquals(BlobSender.TICK_MS, ms)
                    atEachTick.add(chunks())
                    true
                }

            assertTrue(paced.send(BlobKind.CLIP_TEXT, "", payload((3 * budget + 1) * FrameCodec.MAX_CHUNK_BYTES)))

            assertEquals(listOf(budget, 2 * budget, 3 * budget), atEachTick)
        }
    }

    @Test
    fun inputDropsTheNextTickToTheBusyBudget() {
        val atEachTick = ArrayList<Int>()
        val paced =
            sender {
                atEachTick.add(chunks())
                lastInput = now
                true
            }

        assertTrue(paced.send(BlobKind.CLIP_TEXT, "", payload(40 * FrameCodec.MAX_CHUNK_BYTES)))

        assertEquals(BlobSender.BLUETOOTH_CHUNKS_PER_TICK, atEachTick[0])
        assertEquals(BlobSender.BLUETOOTH_CHUNKS_PER_TICK + BlobSender.BUSY_CHUNKS_PER_TICK, atEachTick[1])
    }

    @Test
    fun inputThatHasGoneQuietRestoresTheBudget() {
        val atEachTick = ArrayList<Int>()
        lastInput = now
        val paced =
            sender {
                atEachTick.add(chunks())
                now += BlobSender.INPUT_QUIET_MS
                true
            }

        assertTrue(paced.send(BlobKind.CLIP_TEXT, "", payload(40 * FrameCodec.MAX_CHUNK_BYTES)))

        assertEquals(BlobSender.BUSY_CHUNKS_PER_TICK, atEachTick[0])
        assertEquals(BlobSender.BUSY_CHUNKS_PER_TICK + BlobSender.BLUETOOTH_CHUNKS_PER_TICK, atEachTick[1])
    }

    @Test
    fun aWriterThatFellBehindHoldsTheNextChunk() {
        var queued = BlobSender.MAX_BACKLOG
        var pauses = 0
        val paced =
            BlobSender(
                { wire.add(it) },
                Transport.BLUETOOTH,
                { lastInput },
                { false },
                { now },
                {
                    assertEquals(BlobSender.BLUETOOTH_CHUNKS_PER_TICK, chunks())
                    if (++pauses == 3) queued = 0
                    true
                },
                { queued },
            )

        val length = (BlobSender.BLUETOOTH_CHUNKS_PER_TICK + 1) * FrameCodec.MAX_CHUNK_BYTES
        assertTrue(paced.send(BlobKind.CLIP_TEXT, "", payload(length)))

        assertEquals(3, pauses)
        assertEquals(1, wire.count { it is Frame.BlobEnd })
    }

    @Test
    fun aWriterThatNeverCatchesUpCanBeAbandoned() {
        var pauses = 0
        val paced =
            BlobSender(
                { wire.add(it) },
                Transport.BLUETOOTH,
                { lastInput },
                { false },
                { now },
                {
                    pauses++
                    true
                },
                { BlobSender.MAX_BACKLOG },
            )

        assertFalse(paced.send(BlobKind.CLIP_TEXT, "", payload(100 * FrameCodec.MAX_CHUNK_BYTES)) { pauses >= 2 })

        assertEquals(0, wire.count { it is Frame.BlobEnd })
    }

    @Test
    fun anEndedLinkStopsATransferWithNoEnd() {
        val paced = BlobSender({ wire.add(it) }, Transport.BLUETOOTH, { lastInput }, { true }, { now })

        assertFalse(paced.send(BlobKind.CLIP_TEXT, "", payload(100 * FrameCodec.MAX_CHUNK_BYTES)))

        assertEquals(0, wire.count { it is Frame.BlobEnd })
        assertEquals(BlobSender.BLUETOOTH_CHUNKS_PER_TICK, chunks())
    }

    @Test
    fun anAbandonedTransferStopsWithNoEnd() {
        assertFalse(sender().send(BlobKind.CLIP_TEXT, "", payload(10)) { true })

        assertTrue(wire.single() is Frame.BlobBegin)
    }

    @Test
    fun aBlobFromTheLaptopIsNotSentBack() {
        sync.setShared(1)

        remote(a)
        settle()
        assertArrayEquals(a, surface.writes.single().data)

        now += ClipboardSync.WRITE_QUIET_MS
        sync.localChanged()
        settle()
        assertTrue(wire.isEmpty())
    }

    @Test
    fun aLocalCopyGoesAndTheOldContentCopiedAgainGoesToo() {
        sync.setShared(1)
        remote(a)
        settle()
        now += ClipboardSync.WRITE_QUIET_MS

        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, b)
        sync.localChanged()
        settle()
        assertEquals(b.size.toLong(), wire.filterIsInstance<Frame.BlobBegin>().single().size)

        wire.clear()
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, a)
        sync.localChanged()
        settle()
        assertEquals(a.size.toLong(), wire.filterIsInstance<Frame.BlobBegin>().single().size)
    }

    @Test
    fun theSameLocalCopyTwiceGoesOnce() {
        sync.setShared(1)
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, b)

        sync.localChanged()
        settle()
        sync.localChanged()
        settle()

        assertEquals(1, begins())
    }

    @Test
    fun anEchoWithDifferentBytesRightAfterAWriteIsIgnored() {
        // A platform can hand an image back re-encoded, so the hash cannot be the only thing that catches it.
        sync.setShared(1)
        remote(a)
        settle()

        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, b)
        sync.localChanged()
        settle()

        assertTrue(wire.isEmpty())
    }

    @Test
    fun aBurstOfChangesIsOneSend() {
        sync.setShared(1)
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, b)

        sync.localChanged()
        sync.localChanged()
        sync.localChanged()
        settle()

        assertEquals(1, begins())
    }

    @Test
    fun sensitiveContentIsNeverSent() {
        sync.setShared(1)
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, b, sensitive = true)

        sync.localChanged()
        settle()

        assertTrue(wire.isEmpty())
    }

    @Test
    fun nothingMovesUntilSharingIsTurnedOn() {
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, b)

        sync.localChanged()
        settle()
        assertFalse(sync.take(Frame.BlobBegin(0, 1, "")))

        assertTrue(wire.isEmpty())
        assertTrue(surface.writes.isEmpty())
    }

    @Test
    fun turningItOffStopsBothDirections() {
        sync.setShared(1)
        assertTrue(sync.take(Frame.BlobBegin(0, a.size.toLong(), "")))
        assertTrue(sync.setShared(0))
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, b)

        assertFalse(sync.take(Frame.BlobChunk(0, a)))
        assertFalse(sync.take(Frame.BlobEnd(sha256(a))))
        sync.localChanged()
        settle()

        assertTrue(wire.isEmpty())
        assertTrue(surface.writes.isEmpty())
    }

    @Test
    fun theOwnersToggleOutranksEverything() {
        toggle = false
        assertFalse(sync.setShared(1))

        toggle = true
        assertTrue(sync.setShared(1))
        toggle = false
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, b)
        sync.localChanged()
        settle()

        assertTrue(wire.isEmpty())
        assertFalse(sync.take(Frame.BlobBegin(0, 1, "")))
    }

    @Test
    fun aValueThatIsNeitherOnNorOffIsRefused() {
        assertFalse(sync.setShared(2))

        sync.localChanged()
        assertTrue(pending.isEmpty())
    }

    @Test
    fun turningItOnSendsNothingUntilTheNextCopy() {
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, b)

        sync.setShared(1)
        settle()

        assertTrue(wire.isEmpty())
    }

    @Test
    fun aCopyOverTheCapStaysHere() {
        sync.setShared(1)
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, ByteArray(ClipboardSync.MAX_TEXT_BYTES.toInt() + 1))

        sync.localChanged()
        settle()

        assertTrue(wire.isEmpty())
    }

    @Test
    fun textThatIsNotUtf8IsDropped() {
        sync.setShared(1)

        assertFalse(remoteLastFrame(BlobKind.CLIP_TEXT, byteArrayOf(-61, 0x28)))

        assertTrue(surface.writes.isEmpty())
    }

    @Test
    fun anEmptyBlobIsDropped() {
        sync.setShared(1)

        assertFalse(remoteLastFrame(BlobKind.CLIP_TEXT, ByteArray(0)))

        assertTrue(surface.writes.isEmpty())
    }

    @Test
    fun aKindTheClipboardDoesNotTakeIsDropped() {
        sync.setShared(1)

        assertFalse(sync.take(Frame.BlobBegin(BlobKind.APP_LIST.id, 1, "")))
    }

    @Test
    fun aPngIsWritten() {
        val png = png(7680, 4320)
        sync.setShared(1)

        assertTrue(remoteLastFrame(BlobKind.CLIP_IMAGE, png))
        settle()

        assertEquals(BlobKind.CLIP_IMAGE, surface.writes.single().kind)
        assertArrayEquals(png, surface.writes.single().data)
    }

    @Test
    fun aPngThatWouldDecodeToTooMuchIsDropped() {
        sync.setShared(1)

        for ((width, height) in listOf(-1 to -1, 100_000 to 100_000, 0 to 100, 100 to 0)) {
            assertFalse(remoteLastFrame(BlobKind.CLIP_IMAGE, png(width, height)))
        }

        assertTrue(surface.writes.isEmpty())
    }

    @Test
    fun anImageThatIsNotAPngIsDropped() {
        sync.setShared(1)

        assertFalse(remoteLastFrame(BlobKind.CLIP_IMAGE, payload(100)))

        assertTrue(surface.writes.isEmpty())
    }

    @Test
    fun aClipboardThatThrowsEndsNothing() {
        sync.setShared(1)
        surface.throws = true

        remote(a)
        settle()
        now += ClipboardSync.WRITE_QUIET_MS
        sync.localChanged()
        settle()

        assertTrue(wire.isEmpty())
    }

    @Test
    fun aSendThatFailedIsTriedAgainOnTheNextCopy() {
        var fail = true
        val failing =
            syncOver(
                BlobSender(
                    { frame ->
                        if (fail) throw IOException("the link went")
                        wire.add(frame)
                    },
                    Transport.BLUETOOTH,
                    { lastInput },
                    { false },
                    { now },
                    { true },
                ),
            )
        failing.setShared(1)
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, b)

        failing.localChanged()
        settle()
        fail = false
        failing.localChanged()
        settle()

        assertEquals(1, wire.count { it is Frame.BlobEnd })
    }

    @Test
    fun turningItOffPartWayStopsTheTransfer() {
        var stopping: ClipboardSync? = null
        val partWay =
            syncOver(
                sender {
                    stopping?.setShared(0)
                    true
                },
            )
        stopping = partWay
        partWay.setShared(1)
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, payload(100 * FrameCodec.MAX_CHUNK_BYTES))

        partWay.localChanged()
        settle()

        assertEquals(BlobSender.BLUETOOTH_CHUNKS_PER_TICK, chunks())
        assertEquals(0, wire.count { it is Frame.BlobEnd })
    }

    @Test
    fun theSameContentGoesAgainOnceSharingWasTurnedOffAndOn() {
        // The laptop may have been unselected since, so what this link remembers sending proves nothing.
        sync.setShared(1)
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, b)
        sync.localChanged()
        settle()

        sync.setShared(0)
        sync.setShared(1)
        sync.localChanged()
        settle()

        assertEquals(2, begins())
    }

    private fun payload(length: Int): ByteArray = ByteArray(length) { (it * 31).toByte() }

    private fun takesClipboardKinds(kind: Int): Long? =
        if (kind <= BlobKind.CLIP_IMAGE.id) ClipboardSync.MAX_TEXT_BYTES else null

    private fun png(
        width: Int,
        height: Int,
    ): ByteArray =
        ByteBuffer
            .allocate(24)
            .put(byteArrayOf(-119, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52))
            .putInt(width)
            .putInt(height)
            .array()

    private fun sender(
        network: Boolean = false,
        pause: (Int) -> Boolean = { true },
    ): BlobSender =
        BlobSender(
            { wire.add(it) },
            if (network) Transport.WIFI else Transport.BLUETOOTH,
            { lastInput },
            { false },
            { now },
            pause,
        )

    private fun syncOver(sender: BlobSender): ClipboardSync =
        ClipboardSync(surface, sender, { toggle }, { false }, { now }, { _, work -> pending.add(work) }, {})

    private fun keep(
        kind: Int,
        data: ByteArray,
        hash: ByteArray,
    ): Boolean {
        assertArrayEquals(sha256(data), hash)
        received.add(kind to data)
        return true
    }

    private fun chunks(): Int = wire.count { it is Frame.BlobChunk }

    private fun begins(): Int = wire.count { it is Frame.BlobBegin }

    private fun assertStillTakesABlob() {
        assertTrue(received.isEmpty())
        val data = payload(300)

        assertTrue(sender().send(BlobKind.CLIP_IMAGE, "", data))

        wire.forEach { assertTrue(receiver.take(it)) }
        assertArrayEquals(data, received.single().second)
    }

    // Time for a debounce to pass, then everything the sync put off, as its scheduler would run it.
    private fun settle() {
        now += ClipboardSync.DEBOUNCE_MS
        val due = pending.toList()
        pending.clear()
        due.forEach { it() }
    }

    private fun remote(data: ByteArray) = assertTrue(remoteLastFrame(BlobKind.CLIP_TEXT, data))

    // Plays a blob into the sync as a laptop would, and answers what it said to the BLOB_END.
    private fun remoteLastFrame(
        kind: BlobKind,
        data: ByteArray,
    ): Boolean {
        val inbound = ArrayList<Frame>()
        BlobSender({ inbound.add(it) }, Transport.WIFI, { 0L }, { false }, { now }, { true }).send(kind, "", data)
        return inbound.map(sync::take).last()
    }

    /** A clipboard in memory, which notes what it was asked to write. */
    private class FakeClipboard : ClipboardSurface {
        var current: ClipboardContent? = null
        val writes = ArrayList<ClipboardContent>()
        var throws = false

        override fun read(): ClipboardContent? {
            if (throws) throw IOException("the clipboard is locked")
            return current
        }

        override fun write(content: ClipboardContent) {
            if (throws) throw IOException("the clipboard is locked")
            writes.add(content)
            current = content
        }
    }

    private companion object {
        const val SHA256_BYTES = 32
    }
}
