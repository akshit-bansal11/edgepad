package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Test

class DashWireCleanTest {
    @Test
    fun cleanTrimsOnlyTrailingAsciiSpacesLikeTheLaptop() {
        assertEquals("ab\u3000", DashWire.clean("ab\u3000", 10))
        assertEquals("ab", DashWire.clean("ab:::", 10))
        assertEquals("ab", DashWire.clean("ab cd", 2))
    }
}
