package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Test

class LinkHooksTest {
    @Test
    fun aShareGoingHoldsTheLinks() {
        assertEquals(LingerAction.WAIT, lingerAction(1))
        assertEquals(LingerAction.WAIT, lingerAction(2))
    }

    @Test
    fun noShareLetsTheLingerEndThem() {
        assertEquals(LingerAction.CLOSE, lingerAction(0))
    }
}
