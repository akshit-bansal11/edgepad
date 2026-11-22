package me.akshitbansal.edgepad.link.xfer

import me.akshitbansal.edgepad.link.Transport
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.FrameCodec
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicLong
import kotlin.concurrent.thread

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
    fun aFileIdleForThirtySecondsIsDroppedKeepingItsPartAndTheLaptopIsTold() {
        var now = 1_000L
        val timers = ArrayList<Pair<Long, () -> Unit>>()
        val receiver =
            XferReceiver(
                store,
                { replies += it },
                runner = { it() },
                now = { now },
                after = { delay, work ->
                    timers += delay to work
                    {}
                },
            )
        receiver.take(Frame.XferBegin(1, 10uL, "i.bin"))
        now += 20_000
        receiver.take(Frame.XferChunk(1, 0uL, bytes(4)))
        now += 15_000
        timers.last().second()
        assertTrue(receiver.busy)
        assertEquals(15_000L, timers.last().first)
        now += 15_000
        timers.last().second()
        assertFalse(receiver.busy)
        assertEquals(XferCode.CANCELLED.value, replies.last().status)
        assertEquals(1, parts.listFiles()!!.size)
        receiver.take(Frame.XferBegin(2, 10uL, "i.bin"))
        assertEquals(4uL, replies.last().offset)
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
    fun theSenderTellsTheLaptopWhenItGivesUp() {
        var now = 0L
        val clock = {
            now += 600
            now
        }
        val sender = XferSender({ wire += it }, Transport.WIFI, { 0L }, { false }, 1_000L, clock)
        sender.send({ bytes(10).inputStream() }, "n.bin", 10, 1)
        assertEquals(XferCode.CANCELLED.value, (wire.last() as Frame.XferStatus).status)
    }

    @Test
    fun aLostChunkInTheMiddleIsSentAgainFromTheReportedOffset() = lostChunkIsResent(lostIndex = 1)

    @Test
    fun aLostLastChunkIsSentAgainWhenTheEndIsAnsweredShort() = lostChunkIsResent(lostIndex = 3)

    private fun lostChunkIsResent(lostIndex: Int) {
        val step = FrameCodec.MAX_XFER_CHUNK_BYTES
        val data = bytes(step * 4)
        lateinit var sender: XferSender
        val receiver =
            XferReceiver(
                store,
                { status ->
                    replies += status
                    sender.take(status)
                },
                { arrived += it },
                runner = { it() },
            )
        var lost = false
        sender =
            XferSender(
                { frame ->
                    val dropped = !lost && frame is Frame.XferChunk && frame.offset == (lostIndex * step).toULong()
                    if (dropped) lost = true else receiver.take(frame)
                },
                Transport.WIFI,
                { 0L },
                { false },
                answerTimeoutMs = 200L,
                pause = { true },
            )
        assertEquals(XferCode.SAVED, sender.send({ data.inputStream() }, "g.bin", data.size.toLong(), 1))
        assertArrayEquals(data, File(dest, "g.bin").readBytes())
    }

    @Test
    fun staleProgressAfterARewindDoesNotReopenTheFileAgain() {
        val step = FrameCodec.MAX_XFER_CHUNK_BYTES
        val data = bytes(step * 4)
        val seen = HashSet<ULong>()
        var opens = 0
        lateinit var sender: XferSender
        sender =
            XferSender(
                { frame ->
                    when (frame) {
                        is Frame.XferBegin -> {
                            sender.take(Frame.XferStatus(1, XferCode.READY.value, 0uL))
                        }

                        is Frame.XferChunk -> {
                            // The first pass loses the chunk at `step`; each later chunk is answered with that gap.
                            if (seen.add(frame.offset) && frame.offset > step.toULong()) {
                                sender.take(Frame.XferStatus(1, XferCode.PROGRESS.value, step.toULong()))
                            }
                        }

                        is Frame.XferEnd -> {
                            sender.take(Frame.XferStatus(1, XferCode.SAVED.value, data.size.toULong()))
                        }

                        else -> {
                            Unit
                        }
                    }
                },
                Transport.WIFI,
                { 0L },
                { false },
                answerTimeoutMs = 200L,
                pause = { true },
            )
        val result =
            sender.send(
                { data.also { opens++ }.inputStream() },
                "s.bin",
                data.size.toLong(),
                1,
                progress = { at ->
                    // The resent chunk is on the wire; two answers to the chunks before the rewind land late.
                    if (opens == 2 && at == 2L * step) {
                        repeat(2) { sender.take(Frame.XferStatus(1, XferCode.PROGRESS.value, step.toULong())) }
                    }
                },
            )
        assertEquals(XferCode.SAVED, result)
        assertEquals(2, opens)
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
    fun everyXferFrameIsRoutedNotOnlyStatuses() {
        val receiver = receiver()
        val sender = sender()
        routeXfer(Frame.XferBegin(1, 4uL, "b.bin"), sender) { receiver }
        routeXfer(Frame.XferChunk(1, 0uL, bytes(4)), sender) { receiver }
        assertTrue(receiver.busy)
        assertEquals(XferCode.READY.value, replies.first().status)
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

    @Test
    fun theLocalOnlyCodesAreOutOfTheWireRangeAndNoReceiverTakesThem() {
        assertEquals(8, XferCode.PAUSED.value)
        assertEquals(9, XferCode.RESUME.value)
        assertEquals(0xF0, XferCode.TIMED_OUT.value)
        assertEquals(0xF1, XferCode.SESSION_ENDED.value)
        assertEquals(XferCode.RESUME, XferCode.of(9))
        for (local in listOf(0xF0, 0xF1, 10)) assertNull(XferCode.of(local))
        val receiver = receiver()
        receiver.take(Frame.XferBegin(1, 10uL, "z.bin"))
        assertFalse(receiver.take(Frame.XferStatus(1, XferCode.TIMED_OUT.value, 0uL)))
        assertFalse(receiver.take(Frame.XferStatus(1, XferCode.SESSION_ENDED.value, 0uL)))
    }

    @Test
    fun aReceiverPauseKeepsThePartAndResumeAnswersReadyAtTheBytesHeld() {
        val held = ArrayList<Boolean>()
        val receiver = XferReceiver(store, { replies += it }, runner = { it() }, pausedChanged = { held += it })
        receiver.take(Frame.XferBegin(1, 10uL, "r.bin"))
        receiver.take(Frame.XferChunk(1, 0uL, bytes(4)))
        assertTrue(receiver.pause())
        assertFalse(receiver.pause())
        assertEquals(XferCode.PAUSED.value, replies.last().status)
        assertEquals(4uL, replies.last().offset)
        // The chunk in flight is still taken.
        receiver.take(Frame.XferChunk(1, 4uL, bytes(2)))
        assertTrue(receiver.resume())
        val tail = replies.takeLast(2)
        assertEquals(listOf(XferCode.RESUME.value, XferCode.READY.value), tail.map { it.status })
        assertEquals(6uL, tail.last().offset)
        assertEquals(listOf(true, false), held)
        assertFalse(receiver.resume())
    }

    @Test
    fun theLaptopsPauseSuspendsTheIdleDropAndItsResumeIsAnsweredWithReady() {
        var now = 1_000L
        val timers = ArrayList<Pair<Long, () -> Unit>>()
        val held = ArrayList<Boolean>()
        val receiver =
            XferReceiver(
                store,
                { replies += it },
                runner = { it() },
                now = { now },
                after = { delay, work ->
                    timers += delay to work
                    {}
                },
                pausedChanged = { held += it },
            )
        receiver.take(Frame.XferBegin(1, 10uL, "l.bin"))
        receiver.take(Frame.XferChunk(1, 0uL, bytes(4)))
        assertTrue(receiver.take(Frame.XferStatus(1, XferCode.PAUSED.value, 4uL)))
        assertEquals(XferLimits.PAUSE_CAP_MS, timers.last().first)
        now += 60_000
        timers.last().second()
        assertTrue(receiver.busy)
        assertTrue(receiver.held)
        val before = replies.size
        assertTrue(receiver.take(Frame.XferStatus(1, XferCode.RESUME.value, 4uL)))
        assertEquals(XferCode.READY.value, replies.last().status)
        assertEquals(4uL, replies.last().offset)
        assertEquals(before + 1, replies.size)
        assertEquals(XferLimits.IDLE_MS, timers.last().first)
        assertEquals(listOf(true, false), held)
    }

    @Test
    fun aHoldOlderThanTenMinutesEndsCancelledAndKeepsThePart() {
        var now = 1_000L
        val timers = ArrayList<Pair<Long, () -> Unit>>()
        val held = ArrayList<Boolean>()
        val receiver =
            XferReceiver(
                store,
                { replies += it },
                runner = { it() },
                now = { now },
                after = { delay, work ->
                    timers += delay to work
                    {}
                },
                pausedChanged = { held += it },
            )
        receiver.take(Frame.XferBegin(1, 10uL, "c.bin"))
        receiver.take(Frame.XferChunk(1, 0uL, bytes(4)))
        assertTrue(receiver.pause())
        now += 5 * 60_000
        timers.last().second()
        assertTrue(receiver.busy)
        assertEquals(5 * 60_000L, timers.last().first)
        now += 5 * 60_000
        timers.last().second()
        assertFalse(receiver.busy)
        assertEquals(XferCode.CANCELLED.value, replies.last().status)
        assertEquals(1, parts.listFiles()!!.size)
        assertEquals(listOf(true, false), held)
    }

    @Test
    fun aBeginForTheSameHeldFileTakesItOverFromItsOffsetAndAnyOtherIsBusy() {
        val receiver = receiver()
        receiver.take(Frame.XferBegin(1, 10uL, "h.bin"))
        receiver.take(Frame.XferChunk(1, 0uL, bytes(4)))
        receiver.pause()
        receiver.take(Frame.XferBegin(2, 11uL, "h.bin"))
        assertEquals(XferCode.BUSY.value, replies.last().status)
        assertTrue(receiver.held)
        receiver.take(Frame.XferBegin(3, 10uL, "h.bin"))
        assertEquals(XferCode.READY.value, replies.last().status)
        assertEquals(3, replies.last().id)
        assertEquals(4uL, replies.last().offset)
        assertFalse(receiver.held)
        // Not held now: a further BEGIN is BUSY.
        receiver.take(Frame.XferBegin(4, 10uL, "h.bin"))
        assertEquals(XferCode.BUSY.value, replies.last().status)
    }

    @Test
    fun theSessionEndingWhileHeldKeepsThePartAndLetsGo() {
        val held = ArrayList<Boolean>()
        val receiver = XferReceiver(store, { replies += it }, runner = { it() }, pausedChanged = { held += it })
        receiver.take(Frame.XferBegin(1, 10uL, "e.bin"))
        receiver.take(Frame.XferChunk(1, 0uL, bytes(4)))
        receiver.pause()
        receiver.close()
        assertFalse(receiver.busy)
        assertEquals(1, parts.listFiles()!!.size)
        assertEquals(listOf(true, false), held)
    }

    @Test
    fun aSenderPauseHoldsTheFileAfterTheChunkInFlightAndResumeFinishesItWithTheSameHash() {
        val data = bytes(FrameCodec.MAX_XFER_CHUNK_BYTES * 3)
        val rig = HeldRig()
        val sending = thread { rig.result = rig.sender.send({ data.inputStream() }, "p.bin", data.size.toLong(), 1) }
        waitFor { rig.receiver.held }
        Thread.sleep(100)
        assertEquals(1, rig.chunks.get())
        val told = rig.frames.filterIsInstance<Frame.XferStatus>().first { it.status == XferCode.PAUSED.value }
        assertEquals(FrameCodec.MAX_XFER_CHUNK_BYTES.toULong(), told.offset)
        assertEquals(listOf(true), rig.held)
        assertTrue(rig.sender.resume())
        sending.join(5_000)
        assertEquals(XferCode.SAVED, rig.result)
        assertArrayEquals(data, File(dest, "p.bin").readBytes())
        assertEquals(listOf(true, false), rig.held)
        assertTrue(rig.frames.any { it is Frame.XferStatus && it.status == XferCode.RESUME.value })
        assertTrue(rig.statuses().none { it >= 0xF0 })
    }

    @Test
    fun aPauseAskedBeforeAFileStartsHoldsTheNextOne() {
        val data = bytes(FrameCodec.MAX_XFER_CHUNK_BYTES * 2)
        val rig = HeldRig(pauseAtChunk = 0)
        assertFalse(rig.sender.pause())
        assertEquals(listOf(true), rig.held)
        val sending = thread { rig.result = rig.sender.send({ data.inputStream() }, "q.bin", data.size.toLong(), 1) }
        waitFor { rig.receiver.held }
        Thread.sleep(100)
        assertEquals(0, rig.chunks.get())
        assertTrue(rig.sender.resume())
        sending.join(5_000)
        assertEquals(XferCode.SAVED, rig.result)
        assertEquals(listOf(true, false), rig.held)
    }

    @Test
    fun aResumeBetweenFilesTakesBackAPendingPause() {
        val rig = HeldRig(pauseAtChunk = 0)
        rig.sender.pause()
        assertFalse(rig.sender.resume())
        assertEquals(listOf(true, false), rig.held)
    }

    @Test
    fun theLaptopsPauseHoldsTheSenderAndNoAnswerTimeoutRunsWhileHeld() {
        val data = bytes(FrameCodec.MAX_XFER_CHUNK_BYTES * 2)
        val now = AtomicLong()
        val rig = HeldRig(pauseAtChunk = 0, receiverPausesAtChunk = 1, clock = { now.get() }, answerTimeoutMs = 1_000)
        val sending = thread { rig.result = rig.sender.send({ data.inputStream() }, "t.bin", data.size.toLong(), 1) }
        // The laptop holds the file: PAUSED from it, and the clock runs far past the answer timeout.
        waitFor { rig.held.isNotEmpty() }
        now.addAndGet(60_000)
        Thread.sleep(400)
        assertTrue(sending.isAlive)
        rig.receiver.resume()
        sending.join(5_000)
        assertEquals(XferCode.SAVED, rig.result)
    }

    @Test
    fun aHoldOlderThanTenMinutesEndsTheSendCancelledWithoutTellingTheLaptop() {
        val data = bytes(FrameCodec.MAX_XFER_CHUNK_BYTES * 2)
        val now = AtomicLong()
        val rig = HeldRig(clock = { now.get() })
        val sending = thread { rig.result = rig.sender.send({ data.inputStream() }, "x.bin", data.size.toLong(), 1) }
        waitFor { rig.receiver.held }
        now.addAndGet(XferLimits.PAUSE_CAP_MS + 1)
        sending.join(5_000)
        assertEquals(XferCode.CANCELLED, rig.result)
        assertTrue(rig.frames.none { it is Frame.XferStatus && it.status == XferCode.CANCELLED.value })
        assertEquals(1, parts.listFiles()!!.size)
    }

    @Test
    fun theLinkEndingWhileHeldEndsTheSend() {
        val data = bytes(FrameCodec.MAX_XFER_CHUNK_BYTES * 2)
        val rig = HeldRig()
        val sending = thread { rig.result = rig.sender.send({ data.inputStream() }, "e.bin", data.size.toLong(), 1) }
        waitFor { rig.receiver.held }
        rig.linkEnded = true
        sending.join(5_000)
        assertEquals(XferCode.SESSION_ENDED, rig.result)
        assertEquals(listOf(true, false), rig.held)
    }

    // A sender and a receiver back to back; the sender pauses itself after the chunk numbered [pauseAtChunk], and the
    // receiver after [receiverPausesAtChunk] (0: never).
    private inner class HeldRig(
        pauseAtChunk: Int = 1,
        receiverPausesAtChunk: Int = 0,
        clock: () -> Long = { System.nanoTime() / 1_000_000 },
        answerTimeoutMs: Long = XferLimits.ANSWER_TIMEOUT_MS,
    ) {
        val frames = CopyOnWriteArrayList<Frame>()
        val held = CopyOnWriteArrayList<Boolean>()
        val chunks = AtomicInteger()

        @Volatile var linkEnded = false

        @Volatile var result: XferCode? = null

        val receiver: XferReceiver =
            XferReceiver(
                store,
                { status ->
                    frames += status
                    sender.take(status)
                },
                { arrived += it },
                runner = { it() },
            )
        val sender: XferSender =
            XferSender(
                { frame ->
                    frames += frame
                    receiver.take(frame)
                    if (frame is Frame.XferChunk) {
                        val n = chunks.incrementAndGet()
                        if (n == pauseAtChunk) sender.pause()
                        if (n == receiverPausesAtChunk) receiver.pause()
                    }
                },
                Transport.WIFI,
                { 0L },
                { linkEnded },
                answerTimeoutMs = answerTimeoutMs,
                clock = clock,
                pause = { true },
                pausedChanged = { held += it },
            )

        fun statuses() = frames.filterIsInstance<Frame.XferStatus>().map { it.status }
    }

    private fun waitFor(what: () -> Boolean) {
        val end = System.nanoTime() + 5_000_000_000L
        while (!what()) {
            check(System.nanoTime() < end) { "timed out" }
            Thread.sleep(5)
        }
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
