package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.PI
import kotlin.math.sqrt

/** A 100 by 200 screen with 10-unit rounded corners. */
class PerimeterTest {
    private val path = Perimeter(100f, 200f, 10f)
    private val out = FloatArray(4)
    private val arc = (PI / 2 * 10).toFloat()

    private fun assertPoint(
        x: Float,
        y: Float,
        nx: Float,
        ny: Float,
    ) {
        assertEquals("x", x, out[0], 1e-3f)
        assertEquals("y", y, out[1], 1e-3f)
        assertEquals("nx", nx, out[2], 1e-3f)
        assertEquals("ny", ny, out[3], 1e-3f)
    }

    @Test
    fun theLengthIsTheRoundedRectangle() {
        assertEquals(2 * 80f + 2 * 180f + 4 * arc, path.length, 1e-3f)
    }

    @Test
    fun edgeMiddlesSitOnTheEdgesFacingIn() {
        path.point(path.lengthAt(0.5f), out)
        assertPoint(50f, 0f, 0f, 1f)
        path.point(path.lengthAt(1.5f), out)
        assertPoint(100f, 100f, -1f, 0f)
        path.point(path.lengthAt(2.5f), out)
        assertPoint(50f, 200f, 0f, -1f)
        path.point(path.lengthAt(3.5f), out)
        assertPoint(0f, 100f, 1f, 0f)
    }

    @Test
    fun aCornerIsHalfwayRoundItsArc() {
        val d = (10 - 10 / sqrt(2.0)).toFloat()
        val n = (1 / sqrt(2.0)).toFloat()
        path.point(path.lengthAt(0f), out)
        assertPoint(d, d, n, n)
        path.point(path.lengthAt(2f), out)
        assertPoint(100 - d, 200 - d, -n, -n)
    }

    @Test
    fun projectFindsTheNearestEdge() {
        val hit = FloatArray(2)
        path.project(50f, 5f, hit)
        assertEquals(path.lengthAt(0.5f), hit[0], 1e-3f)
        assertEquals(5f, hit[1], 1e-3f)
        path.project(95f, 100f, hit)
        assertEquals(path.lengthAt(1.5f), hit[0], 1e-3f)
        assertEquals(5f, hit[1], 1e-3f)
    }

    @Test
    fun projectNearACornerLandsOnItsArc() {
        val hit = FloatArray(2)
        path.project(5f, 5f, hit)
        assertEquals(path.lengthAt(0f), hit[0], 1e-3f)
        assertEquals((10 - 5 * sqrt(2.0)).toFloat(), hit[1], 1e-3f)
    }

    @Test
    fun deltaTakesTheShortWayRoundThePathsStart() {
        assertEquals(4f, path.delta(path.length - 2f, 2f), 1e-3f)
        assertEquals(-4f, path.delta(2f, path.length - 2f), 1e-3f)
    }

    @Test
    fun aTinyPlaceholderDoesNotThrow() {
        val tiny = Perimeter(1f, 1f, 1f)
        assertEquals(1f, tiny.radius, 1e-3f)
        tiny.point(0f, out)
    }

    @Test
    fun slotsSitEitherSideOfTheirCornerAndMirrorEachOther() {
        for (c in 0 until Perimeter.CORNERS) {
            val mid = path.lengthAt(c.toFloat())
            val before = path.delta(mid, path.lengthAtSlot(2 * c, 5f))
            val after = path.delta(mid, path.lengthAtSlot(2 * c + 1, 5f))
            assertTrue("corner $c before", before < 0f)
            assertTrue("corner $c after", after > 0f)
            assertEquals("corner $c symmetric", -before, after, 1e-3f)
            assertEquals(arc / 2 + 5f, after, 1e-3f)
        }
    }

    @Test
    fun slotZeroWrapsAcrossThePathStart() {
        // Corner 0's mid is at -arc / 2, so its first slot lies before the start and wraps to the end.
        val first = path.lengthAtSlot(0, 5f)
        assertEquals(path.length - arc - 5f, first, 1e-3f)
        assertEquals(arc + 10f, path.delta(first, path.lengthAtSlot(1, 5f)), 1e-3f)
    }

    @Test
    fun eightSlotCentresAreDistinctAndClockwise() {
        val centres = FloatArray(Perimeter.SLOTS) { path.lengthAtSlot(it, 5f) }
        var lap = 0f
        for (i in 0 until Perimeter.SLOTS) {
            val step = path.wrap(centres[(i + 1) % Perimeter.SLOTS] - centres[i])
            assertTrue("slot $i to the next", step > 0f)
            lap += step
        }
        assertEquals(path.length, lap, 1e-2f)
    }
}
