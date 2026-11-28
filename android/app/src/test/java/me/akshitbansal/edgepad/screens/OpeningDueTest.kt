package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class OpeningDueTest {
    @Test
    fun dueOnlyOnceForANewVersion() {
        assertTrue(openingDue(false, "", "5.2.2"))
        assertTrue(openingDue(false, "5.2.1", "5.2.2"))
        assertFalse(openingDue(false, "5.2.2", "5.2.2"))
    }

    @Test
    fun neverOnARotation() {
        assertFalse(openingDue(true, "5.2.1", "5.2.2"))
    }
}
