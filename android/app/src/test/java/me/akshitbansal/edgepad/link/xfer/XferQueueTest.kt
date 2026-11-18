package me.akshitbansal.edgepad.link.xfer

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.atomic.AtomicInteger

class XferQueueTest {
    private val progress = XferProgress(clock = { 0L }, post = { })
    private val sharing = AtomicInteger()
    private val sentOrder = mutableListOf<String>()
    private var answer: (String) -> XferCode = { XferCode.SAVED }
    private var unreadable = emptySet<String>()
    private val results = mutableListOf<XferBatchResult>()

    private val batcher =
        XferBatcher<String>(
            progress = progress,
            sharing = sharing,
            describe = { item -> if (item in unreadable) throw SecurityException("gone") else item to 10L },
            label = { it },
            sendOne = { item, _, _ ->
                sentOrder += item
                answer(item)
            },
            finished = { results += it },
        )

    @Test
    fun allSaved() {
        val result = batcher.run(listOf("a", "b", "c"))!!
        assertEquals(3, result.sent)
        assertTrue(result.allSent)
        assertEquals(listOf("a", "b", "c"), sentOrder)
        assertFalse(batcher.busy)
        assertEquals(XferPhase.FINISHED, progress.snapshot.phase)
        assertEquals(listOf(result), results)
    }

    @Test
    fun aFailureOfOneFileMovesOn() {
        answer = { if (it == "b") XferCode.REJECTED else XferCode.SAVED }
        unreadable = setOf("c")
        val result = batcher.run(listOf("a", "b", "c", "d"))!!
        assertEquals(2, result.sent)
        assertEquals(
            listOf(XferFailed("b", XferFailure.REFUSED), XferFailed("c", XferFailure.UNREADABLE)),
            result.failed,
        )
        assertEquals(listOf("a", "b", "d"), sentOrder)
        assertEquals(listOf("b", "c"), batcher.failed)
    }

    @Test
    fun aLostLinkStopsAndTheRestAreNotSent() {
        answer = { if (it == "b") XferCode.SESSION_ENDED else XferCode.SAVED }
        val result = batcher.run(listOf("a", "b", "c", "d"))!!
        assertEquals(listOf("a", "b"), sentOrder)
        assertEquals(
            listOf(
                XferFailed("b", XferFailure.LINK_LOST),
                XferFailed("c", XferFailure.NOT_SENT),
                XferFailed("d", XferFailure.NOT_SENT),
            ),
            result.failed,
        )
        assertEquals(listOf("b", "c", "d"), batcher.failed)
    }

    @Test
    fun aTimeOutStopsTheBatch() {
        answer = { XferCode.TIMED_OUT }
        val result = batcher.run(listOf("a", "b"))!!
        assertEquals(listOf("a"), sentOrder)
        assertEquals(XferFailure.NOT_SENT, result.failed.last().why)
    }

    @Test
    fun aCancelBetweenFilesStopsTheRest() {
        answer = {
            if (it == "a") batcher.cancel()
            XferCode.SAVED
        }
        val result = batcher.run(listOf("a", "b", "c"))!!
        assertEquals(1, result.sent)
        assertEquals(listOf("a"), sentOrder)
        assertEquals(listOf(XferFailure.NOT_SENT, XferFailure.NOT_SENT), result.failed.map { it.why })
    }

    @Test
    fun theCancelSurvivesTheNextFileStarting() {
        val seen = mutableListOf<Boolean>()
        answer = {
            seen += batcher.cancelled()
            if (it == "a") batcher.cancel()
            XferCode.SAVED
        }
        batcher.run(listOf("a", "b"))
        assertEquals(listOf(false), seen)
        assertTrue(batcher.cancelled())
        // Only a new batch clears it.
        answer = { XferCode.SAVED }
        batcher.run(listOf("x"))
        assertFalse(batcher.cancelled())
    }

    @Test
    fun aSecondRunWhileOneRunsIsBusy() {
        var inner: XferBatchResult? = XferBatchResult(0, emptyList())
        answer = {
            if (it == "a") inner = batcher.run(listOf("z"))
            XferCode.SAVED
        }
        batcher.run(listOf("a"))
        assertNull(inner)
    }

    @Test
    fun enqueueWhileRunningAppendsInOrder() {
        val started = mutableListOf<() -> Unit>()
        answer = {
            if (it == "a") batcher.enqueue(listOf("c", "d")) { work -> started += work }
            XferCode.SAVED
        }
        batcher.enqueue(listOf("a", "b")) { work -> started += work }
        assertEquals(1, started.size)
        assertEquals(1, sharing.get())
        started.removeAt(0)()
        assertTrue(started.isEmpty())
        assertEquals(listOf("a", "b", "c", "d"), sentOrder)
        assertEquals(4, results.single().sent)
        assertEquals(0, sharing.get())
    }

    @Test
    fun enqueueGrowsTheTotal() {
        answer = {
            if (it == "a") batcher.enqueue(listOf("c")) { }
            assertEquals(3, progress.snapshot.total)
            XferCode.SAVED
        }
        batcher.run(listOf("a", "b"))
        assertEquals(3, progress.snapshot.total)
    }

    @Test
    fun theSharingCountIsBalancedWhenTheLoopThrows() {
        val throwing =
            XferBatcher<String>(
                progress = progress,
                sharing = sharing,
                describe = { it to 1L },
                label = { it },
                sendOne = { _, _, _ -> XferCode.SAVED },
                finished = { error("boom") },
            )
        try {
            throwing.enqueue(listOf("a")) { work -> work() }
        } catch (e: IllegalStateException) {
            // Expected: the finished hook threw.
        }
        assertEquals(0, sharing.get())
        assertFalse(throwing.busy)
    }

    @Test
    fun anOversizedFileIsTooBigAndNotSent() {
        val big =
            XferBatcher<String>(
                progress = progress,
                sharing = sharing,
                describe = { it to XferLimits.MAX_BYTES + 1 },
                label = { it },
                sendOne = { item, _, _ ->
                    sentOrder += item
                    XferCode.SAVED
                },
            )
        val result = big.run(listOf("a", "b"))!!
        assertTrue(sentOrder.isEmpty())
        assertEquals(listOf(XferFailure.TOO_BIG, XferFailure.TOO_BIG), result.failed.map { it.why })
    }

    @Test
    fun singleFileCodesStayTheSame() {
        assertEquals(XferCode.REJECTED, codeOf(XferFailure.REFUSED))
        assertEquals(XferCode.REJECTED, codeOf(XferFailure.UNREADABLE))
        assertEquals(XferCode.REJECTED, codeOf(XferFailure.TOO_BIG))
        assertEquals(XferCode.BAD_HASH, codeOf(XferFailure.DAMAGED))
        assertEquals(XferCode.SESSION_ENDED, codeOf(XferFailure.LINK_LOST))
        assertEquals(XferCode.BUSY, codeOf(XferFailure.BUSY))
        for (code in XferCode.entries) {
            val failure = failureOf(code) ?: continue
            if (failure != XferFailure.NOT_SENT) assertEquals(code, codeOf(failure))
        }
    }
}
