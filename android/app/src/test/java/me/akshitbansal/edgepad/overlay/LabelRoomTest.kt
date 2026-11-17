package me.akshitbansal.edgepad.overlay

import org.junit.Assert.assertEquals
import org.junit.Test

class LabelRoomTest {
    @Test
    fun theRoomGrowsWithTheFontScaleUpToTwice() {
        assertEquals(96f, labelRoomDp(1f), 0f)
        assertEquals(144f, labelRoomDp(1.5f), 0f)
        assertEquals(192f, labelRoomDp(2f), 0f)
        assertEquals(192f, labelRoomDp(3f), 0f)
    }

    @Test
    fun aSmallerFontScaleKeepsTheDefaultRoom() {
        assertEquals(96f, labelRoomDp(0.85f), 0f)
    }
}
