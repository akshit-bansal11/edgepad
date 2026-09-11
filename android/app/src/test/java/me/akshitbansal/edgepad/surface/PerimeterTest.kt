package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
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
    fun atOfUndoesLengthAt() {
        for (at in listOf(0f, 0.25f, 1f, 1.5f, 2.7f, 3.99f)) {
            assertEquals("at $at", at, path.atOf(path.lengthAt(at)), 1e-3f)
        }
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
}
