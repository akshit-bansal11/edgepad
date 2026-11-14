package me.akshitbansal.edgepad

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class FitTextTest {
    // Every character is 0.5 of the size wide.
    private val measure = { s: String, size: Float -> s.length * size * 0.5f }

    @Test
    fun fitsAtFullSize() {
        assertEquals(Fitted(10f, "abcd"), fitText("abcd", 20f, 10f, 0.7f, measure))
    }

    @Test
    fun shrinksToFit() {
        // 8 chars at 10 is 40; room 32 needs 8.
        assertEquals(Fitted(8f, "abcdefgh"), fitText("abcdefgh", 32f, 10f, 0.7f, measure))
    }

    @Test
    fun stopsAtTheFloorThenEllipsizes() {
        val fitted = fitText("abcdefghijkl", 24f, 10f, 0.7f, measure)
        assertEquals(7f, fitted.size, 0f)
        assertTrue(fitted.text.endsWith("…"))
        assertTrue(measure(fitted.text, fitted.size) <= 24f)
    }

    @Test
    fun nothingFitsGivesEmpty() {
        assertEquals("", fitText("abc", 1f, 10f, 0.7f, measure).text)
    }
}
