package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class HintFadeTest {
    @Test
    fun aHintShowsUntilItsGestureIsUsedOnceThenStaysGone() {
        val saved = mutableListOf<Int>()
        val fade = HintFade(0) { saved.add(it) }
        assertTrue(fade.visible(1))
        assertTrue(fade.used(1))
        assertFalse(fade.visible(1))
        assertTrue(fade.visible(0))
        assertFalse(fade.used(1))
        assertEquals(listOf(0b10), saved)
    }

    @Test
    fun aStoredMaskKeepsItsHintsGone() {
        val fade = HintFade(1 shl HintFade.HOLD) {}
        assertFalse(fade.visible(HintFade.HOLD))
        assertTrue(fade.visible(0))
    }

    @Test
    fun noGestureIsNothingToLearn() {
        assertFalse(HintFade(0) {}.used(-1))
    }
}
