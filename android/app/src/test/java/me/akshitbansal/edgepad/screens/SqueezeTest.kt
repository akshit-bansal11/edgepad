package me.akshitbansal.edgepad.screens

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
}
