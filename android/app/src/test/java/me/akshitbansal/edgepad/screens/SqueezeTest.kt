package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SqueezeTest {
    @Test
    fun stacksOnlyWhenTheRowIsTooNarrow() {
        assertFalse(Squeeze.stacks(300, 328))
        assertFalse(Squeeze.stacks(328, 328))
        assertTrue(Squeeze.stacks(329, 328))
    }

    @Test
    fun segmentsShrinkToShareTheRoom() {
        assertEquals(100, Squeeze.segmentWidth(100, 3, 400))
        assertEquals(90, Squeeze.segmentWidth(100, 3, 270))
        assertEquals(1, Squeeze.segmentWidth(100, 3, 0))
    }
}
