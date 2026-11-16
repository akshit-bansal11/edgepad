package me.akshitbansal.edgepad.overlay

import me.akshitbansal.edgepad.surface.Perimeter
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class DialBoundsTest {
    private val perimeter = Perimeter(1080f, 2400f, 60f)

    @Test
    fun aFlatEdgeIsAThinBandAlongIt() {
        val bounds = DialBounds.of(perimeter, perimeter.lengthAt(0.5f), 200f, 200f, 100f)
        assertTrue(bounds.left in 338..341)
        assertTrue(bounds.right in 739..742)
        assertEquals(0, bounds.top)
        assertTrue(bounds.bottom in 99..101)
    }

    @Test
    fun aCornerDialCoversBothEdgesItBendsRoundButNotTheScreen() {
        val bounds = DialBounds.of(perimeter, perimeter.lengthAt(0f), 150f, 150f, 100f)
        assertEquals(0, bounds.left)
        assertEquals(0, bounds.top)
        assertTrue(bounds.right in 150..300)
        assertTrue(bounds.bottom in 150..300)
    }

    @Test
    fun aBandIsNeverLargerThanTheScreenNorNegative() {
        for (corner in 0..3) {
            val bounds = DialBounds.of(perimeter, perimeter.lengthAt(corner.toFloat()), 5000f, 5000f, 5000f)
            assertTrue(bounds.left >= 0 && bounds.top >= 0)
            assertTrue(bounds.right <= 1080 && bounds.bottom <= 2400)
            assertTrue(bounds.width >= 1 && bounds.height >= 1)
        }
    }

    @Test
    fun aDialWithNoReachIsStillOnePixelWide() {
        val bounds = DialBounds.of(perimeter, perimeter.lengthAt(2f), 0f, 0f, 0f)
        assertTrue(bounds.width >= 1 && bounds.height >= 1)
        assertTrue(bounds.right <= 1080 && bounds.bottom <= 2400)
    }
}
