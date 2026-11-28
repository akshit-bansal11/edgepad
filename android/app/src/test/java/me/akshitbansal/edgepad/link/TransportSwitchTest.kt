package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Test

class TransportSwitchTest {
    @Test
    fun aSwitchThatSucceedsEndsTheOldLinkQuietly() {
        assertEquals(CloseReaction.QUIET, closeReaction(ended = true, wasConnected = true, handoverPending = false))
        assertEquals(CloseReaction.QUIET, closeReaction(ended = false, wasConnected = true, handoverPending = true))
    }

    @Test
    fun aSwitchThatFailsLeavesTheOldLinkAloneAndOnlyTheNewOneFails() {
        assertEquals(CloseReaction.FAILED, closeReaction(ended = false, wasConnected = false, handoverPending = false))
    }

    @Test
    fun aRealDropOfANormalLinkStillReconnects() {
        assertEquals(
            CloseReaction.RECONNECT,
            closeReaction(ended = false, wasConnected = true, handoverPending = false),
        )
    }
}
