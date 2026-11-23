package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.PI

class DialSlideTest {
    private val path = Perimeter(1080f, 2400f, 90f)
    private val hit = FloatArray(2)
    private val arc = (PI / 2 * 90).toFloat()

    private fun start(
        x: Float,
        y: Float,
    ): Float {
        DialSlide.travel(path, x, y, hit, 0f)
        return hit[0]
    }

    @Test
    fun `a flat edge moves by the finger's distance`() {
        val last = start(200f, 0f)
        assertEquals(100f, DialSlide.travel(path, 300f, 0f, hit, last), 0.01f)
        assertEquals(-50f, DialSlide.travel(path, 150f, 0f, hit, last), 0.01f)
    }

    @Test
    fun `a finger off the edge is measured along it`() {
        val last = start(200f, 0f)
        assertEquals(100f, DialSlide.travel(path, 300f, 40f, hit, last), 0.01f)
    }

    @Test
    fun `a corner bend moves by the arc length`() {
        val last = start(990f, 0f)
        assertEquals(arc, DialSlide.travel(path, 1080f, 90f, hit, last), 0.01f)
    }

    @Test
    fun `travel leaves the new position in hit`() {
        val last = start(200f, 0f)
        DialSlide.travel(path, 300f, 0f, hit, last)
        assertEquals(last + 100f, hit[0], 0.01f)
    }

    @Test
    fun `a move across the start of the path is not a lap`() {
        val last = start(100f, 0f)
        val moved = DialSlide.travel(path, 80f, 0f, hit, last)
        assertTrue(moved < 0f && moved > -arc)
    }

    @Test
    fun `a jump is longer than 64 dp`() {
        assertFalse(DialSlide.isJump(64f * 3, 3f))
        assertTrue(DialSlide.isJump(64f * 3 + 1f, 3f))
        assertTrue(DialSlide.isJump(-(64f * 3 + 1f), 3f))
    }
}
