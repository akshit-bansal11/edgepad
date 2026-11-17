package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class StripFitTest {
    @Test
    fun theScaleStaysBetweenTheFloorAndOne() {
        assertEquals(1f, StripFit.scale(1.4f, 0.7f), 0f)
        assertEquals(0.8f, StripFit.scale(0.8f, 0.7f), 0f)
        assertEquals(0.7f, StripFit.scale(0.5f, 0.7f), 0f)
    }

    @Test
    fun iconsGoOnlyWhenTheTextWouldGoUnderTheFloor() {
        assertFalse(StripFit.dropsIcons(0.8f, 0.7f))
        assertFalse(StripFit.dropsIcons(0.7f, 0.7f))
        assertTrue(StripFit.dropsIcons(0.6f, 0.7f))
    }
}
