package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
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
}
