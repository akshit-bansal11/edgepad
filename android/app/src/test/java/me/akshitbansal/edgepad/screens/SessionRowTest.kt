package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SessionRowTest {
    @Test
    fun stateWordIsCappedAtFortyPercent() {
        assertEquals(80f, SessionRow.stateRoom(200f), 0.001f)
    }

    @Test
    fun longStateAndLongTitleStillLeaveTheTitleRoom() {
        val width = 200f
        val state = minOf(500f, SessionRow.stateRoom(width))
        assertTrue(SessionRow.titleRoom(width, 32f, 40f, state) > 0f)
    }
}
