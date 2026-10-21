package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
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
}
