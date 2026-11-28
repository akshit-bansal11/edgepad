package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class OpeningDueTest {
    @Test
    fun dueOnlyOnceForANewVersion() {
        assertTrue(OpeningScreen.due(false, "", "5.2.2"))
        assertTrue(OpeningScreen.due(false, "5.2.1", "5.2.2"))
        assertFalse(OpeningScreen.due(false, "5.2.2", "5.2.2"))
    }

    @Test
    fun neverOnARotation() {
        assertFalse(OpeningScreen.due(true, "5.2.1", "5.2.2"))
    }
}
