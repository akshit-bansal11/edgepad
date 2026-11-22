package me.akshitbansal.edgepad.link.xfer

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class XferProgressTest {
    private var now = 1_000L
    private val posted = mutableListOf<() -> Unit>()
    private val heard = mutableListOf<XferSnapshot>()

    private fun progress(listening: Boolean = true): XferProgress {
        val p = XferProgress(clock = { now }, post = { posted += it })
        if (listening) p.addListener { heard += it }
        return p
    }

    private fun deliver() {
        posted.forEach { it() }
        posted.clear()
    }

    @Test
    fun everyCodeMapsToAFailureOrNone() {
        val expected =
            mapOf(
                XferCode.READY to XferFailure.NOT_SENT,
                XferCode.PROGRESS to XferFailure.NOT_SENT,
                XferCode.SAVED to null,
                XferCode.BAD_HASH to XferFailure.DAMAGED,
                XferCode.REJECTED to XferFailure.REFUSED,
                XferCode.WRITE_FAILED to XferFailure.WRITE_FAILED,
                XferCode.CANCELLED to XferFailure.CANCELLED,
                XferCode.BUSY to XferFailure.BUSY,
                XferCode.TIMED_OUT to XferFailure.TIMED_OUT,
                XferCode.SESSION_ENDED to XferFailure.LINK_LOST,
                XferCode.PAUSED to XferFailure.NOT_SENT,
                XferCode.RESUME to XferFailure.NOT_SENT,
            )
        assertEquals(XferCode.entries.size, expected.size)
        for ((code, failure) in expected) assertEquals(code.name, failure, failureOf(code))
    }

    @Test
    fun onlyAFaultOfOneFileLetsTheBatchGoOn() {
        val goesOn =
            setOf(
                XferFailure.REFUSED,
                XferFailure.DAMAGED,
                XferFailure.WRITE_FAILED,
                XferFailure.UNREADABLE,
                XferFailure.TOO_BIG,
            )
        for (f in XferFailure.entries) assertEquals(f.name, f in goesOn, continuesAfter(f))
    }

    @Test
    fun bytesPublishAtMostOncePerQuarterSecond() {
        val p = progress()
        p.beginBatch(sending = true, total = 1)
        p.startFile("a.bin", 1_000_000)
        posted.clear()
        now += 300
        repeat(1000) { i ->
            p.bytes(i * 100L)
            if (i % 10 == 0) now += 1
        }
        assertEquals(1, posted.size)
        p.fileDone("a.bin", null)
        assertEquals(2, posted.size)
    }

    @Test
    fun stateChangesPublishAtOnce() {
        val p = progress()
        p.beginBatch(sending = true, total = 2)
        p.startFile("a", 10)
        p.fileDone("a", null)
        deliver()
        assertEquals(3, heard.size)
        assertEquals(1, heard.last().done)
    }

    @Test
    fun percentMath() {
        val base = XferSnapshot.IDLE
        assertEquals(100, base.copy(size = 0, sent = 0).percent)
        assertEquals(33, base.copy(size = 3, sent = 1).percent)
        assertEquals(100, base.copy(size = 3, sent = 3).percent)
    }

    @Test
    fun speedMeterReadsASteadyRate() {
        val meter = SpeedMeter { now }
        var total = 0L
        meter.add(total)
        repeat(8) {
            now += 250
            total += 500_000
            meter.add(total)
        }
        assertEquals(2_000_000L, meter.bytesPerSecond)
    }

    @Test
    fun speedMeterIgnoresSamplesTooClose() {
        val meter = SpeedMeter { now }
        meter.add(0)
        now += 100
        meter.add(1_000_000)
        assertEquals(0L, meter.bytesPerSecond)
        now += 150
        meter.add(1_000_000)
        assertEquals(4_000_000L, meter.bytesPerSecond)
        meter.reset()
        assertEquals(0L, meter.bytesPerSecond)
    }

    @Test
    fun aHoldShowsOnARunningBatchAndEndsWithIt() {
        val p = progress()
        p.setPaused(true)
        assertFalse(p.snapshot.paused)
        p.beginBatch(sending = true, total = 1)
        p.setPaused(true)
        assertTrue(p.snapshot.paused)
        p.setPaused(false)
        assertFalse(p.snapshot.paused)
        p.setPaused(true)
        p.endBatch()
        assertFalse(p.snapshot.paused)
    }

    @Test
    fun aBatchCountsWhatArrivedAndWhatFailed() {
        val p = progress()
        p.beginBatch(sending = true, total = 3)
        for (name in listOf("a", "b", "c")) {
            p.startFile(name, 10)
            p.fileDone(name, if (name == "b") XferFailure.REFUSED else null)
        }
        p.endBatch()
        val s = p.snapshot
        assertEquals(XferPhase.FINISHED, s.phase)
        assertEquals(3, s.index)
        assertEquals(2, s.done)
        assertEquals(listOf(XferFailed("b", XferFailure.REFUSED)), s.failed)
    }

    @Test
    fun addToBatchGrowsTheTotal() {
        val p = progress()
        p.beginBatch(sending = true, total = 2)
        p.addToBatch(3)
        assertEquals(5, p.snapshot.total)
    }

    @Test
    fun aReceivingBatchStaysOfUnknownSize() {
        val p = progress()
        p.beginBatch(sending = false, total = 0)
        p.addToBatch(1)
        assertEquals(0, p.snapshot.total)
    }

    @Test
    fun requestCancelMarksTheBatch() {
        val p = progress()
        p.beginBatch(sending = true, total = 1)
        assertFalse(p.snapshot.cancelling)
        p.requestCancel()
        assertTrue(p.snapshot.cancelling)
    }

    @Test
    fun dismissReturnsToIdle() {
        val p = progress()
        p.beginBatch(sending = true, total = 1)
        p.startFile("a", 10)
        p.bytes(5)
        p.endBatch()
        p.dismiss()
        assertEquals(XferSnapshot.IDLE, p.snapshot)
    }

    @Test
    fun noListenerMeansNoPosts() {
        val p = progress(listening = false)
        p.beginBatch(sending = true, total = 1)
        p.startFile("a", 10)
        now += 1_000
        p.bytes(5)
        p.fileDone("a", null)
        p.endBatch()
        assertTrue(posted.isEmpty())
    }

    @Test
    fun eachListenerIsRemovedAloneAndAllAreTold() {
        val p = progress(listening = false)
        val share = mutableListOf<XferSnapshot>()
        val main = mutableListOf<XferSnapshot>()
        val shareHeard: (XferSnapshot) -> Unit = { share += it }
        val mainHeard: (XferSnapshot) -> Unit = { main += it }
        p.addListener(shareHeard)
        p.addListener(mainHeard)
        p.addListener(mainHeard)
        p.beginBatch(sending = true, total = 1)
        deliver()
        assertEquals(1, share.size)
        assertEquals(1, main.size)
        // The main screen stops: the share screen still sees the batch end.
        p.removeListener(mainHeard)
        p.endBatch()
        p.dismiss()
        deliver()
        assertEquals(XferPhase.IDLE, share.last().phase)
        assertEquals(1, main.size)
        p.removeListener(shareHeard)
        p.beginBatch(sending = true, total = 1)
        assertTrue(posted.isEmpty())
    }

    @Test
    fun aListenerMayRemoveItselfWhileTold() {
        val p = progress(listening = false)
        val other = mutableListOf<XferSnapshot>()
        lateinit var once: (XferSnapshot) -> Unit
        once = { p.removeListener(once) }
        p.addListener(once)
        p.addListener { other += it }
        p.beginBatch(sending = true, total = 1)
        deliver()
        p.endBatch()
        deliver()
        assertEquals(2, other.size)
    }

    @Test
    fun aReceiveWaitsForSendsAndForASendsFailuresToBeDismissed() {
        val p = progress(listening = false)
        assertTrue(receiveMayOpen(0, p))
        assertFalse(receiveMayOpen(1, p))
        p.beginBatch(sending = true, total = 1)
        p.fileDone("a", XferFailure.REFUSED)
        p.endBatch()
        assertFalse(receiveMayOpen(0, p))
        p.dismiss()
        assertTrue(receiveMayOpen(0, p))
        // A send that ended clean leaves nothing to keep.
        p.beginBatch(sending = true, total = 1)
        p.fileDone("a", null)
        p.endBatch()
        assertTrue(receiveMayOpen(0, p))
        // A new send replaces the failures, and while it runs the count already holds the receive off.
        p.beginBatch(sending = true, total = 1)
        p.fileDone("a", XferFailure.REFUSED)
        p.endBatch()
        p.beginBatch(sending = true, total = 1)
        assertTrue(receiveMayOpen(0, p))
    }

    @Test
    fun theSnapshotCarriesTheLatestBytes() {
        val p = progress()
        p.beginBatch(sending = true, total = 1)
        p.startFile("a", 10)
        p.bytes(7)
        assertEquals(7L, p.snapshot.sent)
        assertEquals(70, p.snapshot.percent)
    }
}
