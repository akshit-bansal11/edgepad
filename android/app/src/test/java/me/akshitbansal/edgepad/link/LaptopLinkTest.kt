package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.TextKind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class LaptopLinkTest {
    @Test
    fun aFailureBeforeConnectReturnedIsUnreachable() {
        assertEquals(LaptopLink.Cause.UNREACHABLE, classifyFailure(connectReturned = false, handshakeDone = false))
    }

    @Test
    fun aFailureAfterConnectButBeforeTheHandshakeIsRefused() {
        assertEquals(LaptopLink.Cause.REFUSED, classifyFailure(connectReturned = true, handshakeDone = false))
    }

    @Test
    fun aFailureAfterTheHandshakeIsLost() {
        assertEquals(LaptopLink.Cause.LOST, classifyFailure(connectReturned = true, handshakeDone = true))
    }

    @Test
    fun aLockOutClosesAsPinLockedWithTheSeconds() {
        val closure = closureFor(Frame.PinRequest(attemptsLeft = 0, lockedSeconds = 300))
        assertEquals(LaptopLink.Cause.PIN_LOCKED, closure?.cause)
        assertEquals("300", closure?.detail)
    }

    @Test
    fun aQuestionLeavesTheLinkOpen() {
        assertNull(closureFor(Frame.PinRequest(attemptsLeft = 5, lockedSeconds = 0)))
        assertNull(closureFor(Frame.PinRequest(attemptsLeft = 2, lockedSeconds = 0)))
    }

    @Test
    fun aMacroListWithNamesAsksForTheIcons() {
        assertTrue(wantsIcons(Frame.Text(TextKind.MACROS.id, "Chrome/Notes")))
    }

    @Test
    fun anEmptyMacroListAsksForNothing() {
        assertFalse(wantsIcons(Frame.Text(TextKind.MACROS.id, "")))
    }

    @Test
    fun snapUpRoundsToTheNextSizeTheLaptopOffers() {
        assertEquals(128, snapUp(0))
        assertEquals(128, snapUp(128))
        assertEquals(256, snapUp(129))
        assertEquals(256, snapUp(256))
        assertEquals(384, snapUp(300))
        assertEquals(512, snapUp(512))
        assertEquals(512, snapUp(9999))
    }

    @Test
    fun otherFramesAskForNothing() {
        assertFalse(wantsIcons(Frame.Text(TextKind.NOW_PLAYING.id, "Chrome")))
        assertFalse(wantsIcons(Frame.Text(TextKind.MACRO_ICON.id, "0/0/1/AAAA")))
    }
}
