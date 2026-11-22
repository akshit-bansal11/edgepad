package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Test

class SheetRegistryTest {
    @Test
    fun dismissAllClosesEveryEntryOnce() {
        val reg = SheetRegistry()
        var closed = 0
        reg.add("battery") { closed++ }
        reg.add("clipboard") { closed++ }
        reg.dismissAll()
        assertEquals(2, closed)
        assertEquals(0, reg.size)
        reg.dismissAll()
        assertEquals(2, closed)
    }

    @Test
    fun removedEntryIsNotDismissed() {
        val reg = SheetRegistry()
        var closed = 0
        reg.add("battery") { closed++ }
        reg.remove("battery")
        reg.dismissAll()
        assertEquals(0, closed)
    }

    @Test
    fun sameNameReplacesTheEarlierEntry() {
        val reg = SheetRegistry()
        var first = 0
        var second = 0
        reg.add("controls") { first++ }
        reg.add("controls") { second++ }
        reg.dismissAll()
        assertEquals(0, first)
        assertEquals(1, second)
    }

    @Test
    fun dismissThatRemovesItselfIsSafe() {
        val reg = SheetRegistry()
        var closed = 0
        reg.add("a") {
            reg.remove("a")
            closed++
        }
        reg.add("b") { closed++ }
        reg.dismissAll()
        assertEquals(2, closed)
    }
}
