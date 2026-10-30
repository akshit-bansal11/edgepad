package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.CaptureTarget
import me.akshitbansal.edgepad.protocol.FileStatus
import me.akshitbansal.edgepad.protocol.Frame
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.util.concurrent.atomic.AtomicInteger

/** Permits belong to the link they were granted on: no laptop can spend another's, and none is lost by moving on. */
class CaptureInboxTest {
    private val ours = Any()
    private val theirs = Any()
    private val opened = AtomicInteger()
    private val discarded = AtomicInteger()
    private val answers = ArrayList<Frame>()
    private val begin = Frame.FileBegin(0, 10, "a.png")

    private val pending =
        object : FileInbox.Pending {
            override fun write(bytes: ByteArray) = Unit

            override fun publish(): String = "Pictures"

            override fun discard() {
                discarded.incrementAndGet()
            }
        }

    private val store =
        FileInbox.Store { _, _ ->
            opened.incrementAndGet()
            pending
        }

    @Before
    fun install() {
        CaptureInbox.poster = { it() }
        CaptureInbox.store = store
        CaptureInbox.target = CaptureTarget.PHONE
        CaptureInbox.owner = ours
        CaptureInbox.permit(ActionId.CAPTURE_SCREEN)
    }

    @After
    fun uninstall() {
        CaptureInbox.abort(ours)
        CaptureInbox.abort(theirs)
        CaptureInbox.owner = null
        CaptureInbox.target = null
        CaptureInbox.store = null
        CaptureInbox.last = null
    }

    @Test
    fun theOwnersFileSpendsThePermit() {
        assertTrue(CaptureInbox.take(begin, ours, answers::add))
        assertEquals(1, opened.get())
        assertTrue(answers.isEmpty())
    }

    @Test
    fun aFileFromAnotherLinkIsRejectedAndThePermitStays() {
        assertTrue(CaptureInbox.take(begin, theirs, answers::add))
        assertEquals(listOf(FileStatus.REJECTED.frame()), answers)
        assertEquals(0, opened.get())
        CaptureInbox.take(begin, ours, answers::add)
        assertEquals(1, opened.get())
    }

    @Test
    fun anotherLinkClosingLeavesThePermitAndTheFile() {
        CaptureInbox.abort(theirs)
        CaptureInbox.take(begin, ours, answers::add)
        assertEquals(1, opened.get())
        CaptureInbox.abort(theirs)
        assertEquals(0, discarded.get())
    }

    @Test
    fun theOwnerClosingTakesThePermitAndTheFile() {
        CaptureInbox.take(begin, ours, answers::add)
        CaptureInbox.abort(ours)
        assertEquals(1, discarded.get())
        answers.clear()
        CaptureInbox.take(begin, ours, answers::add)
        assertEquals(listOf(FileStatus.REJECTED.frame()), answers)
    }

    @Test
    fun aNewOwnerCannotSpendTheOldOnesPermit() {
        CaptureInbox.owner = theirs
        CaptureInbox.take(begin, theirs, answers::add)
        assertEquals(listOf(FileStatus.REJECTED.frame()), answers)
        assertEquals(0, opened.get())
    }

    @Test
    fun aPermitOutlivesTheSelectionMovingOn() {
        CaptureInbox.owner = theirs
        CaptureInbox.take(begin, ours, answers::add)
        assertEquals(1, opened.get())
        assertTrue(answers.isEmpty())
    }

    @Test
    fun aFileOnItsWaySurvivesTheCaptureScreenClosingOnANewOwner() {
        CaptureInbox.take(begin, ours, answers::add)
        CaptureInbox.owner = null
        CaptureInbox.cancel()
        assertEquals(0, discarded.get())
    }

    @Test
    fun aPermitGrantedOnTheNewOwnerIsItsOwn() {
        CaptureInbox.owner = theirs
        CaptureInbox.permit(ActionId.CAPTURE_SCREEN)
        CaptureInbox.take(begin, theirs, answers::add)
        CaptureInbox.take(begin, ours, answers::add)
        assertEquals(2, opened.get())
        assertTrue(answers.isEmpty())
    }

    @Test
    fun anotherFrameIsLeftForTheLink() {
        assertFalse(CaptureInbox.take(Frame.Ping(1), theirs, answers::add))
    }
}
