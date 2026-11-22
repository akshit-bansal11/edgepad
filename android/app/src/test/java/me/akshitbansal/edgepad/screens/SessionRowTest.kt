package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SessionRowTest {
    @Test
    fun stateWordIsCappedAtFortyPercent() {
        assertEquals(80f, SessionRow.stateRoom(200f), 0.001f)
    }

    @Test
    fun stateWordShowsOnlyWhenItAllFitsAndTheTitleKeepsRoom() {
        assertTrue(SessionRow.showsState(300f, 32f, 60f, 50f, 90f))
        // Too wide for its 40 percent share.
        assertFalse(SessionRow.showsState(150f, 32f, 60f, 70f, 90f))
        // Fits its share but the title would be left under the minimum.
        assertFalse(SessionRow.showsState(180f, 32f, 60f, 50f, 90f))
    }

    @Test
    fun longStateAndLongTitleStillLeaveTheTitleRoom() {
        val width = 200f
        val state = minOf(500f, SessionRow.stateRoom(width))
        assertTrue(SessionRow.titleRoom(width, 32f, 40f, state) > 0f)
    }
}
