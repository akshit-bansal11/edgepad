package me.akshitbansal.edgepad

import org.junit.Assert.assertEquals
import org.junit.Test

class ShareFilesTest {
    @Test
    fun nothingSharedIsEmpty() {
        assertEquals(emptyList<String>(), limitShared<String>(null))
        assertEquals(emptyList<String>(), limitShared(emptyList<String>()))
    }

    @Test
    fun duplicatesAreDroppedKeepingTheFirst() {
        assertEquals(listOf("a", "b", "c"), limitShared(listOf("a", "b", "a", "c", "b")))
    }

    @Test
    fun capKeepsTheOrder() {
        val many = List(250) { "f$it" }
        assertEquals(many.take(100), limitShared(many))
        assertEquals(listOf("f0", "f1"), limitShared(many, max = 2))
    }
}
