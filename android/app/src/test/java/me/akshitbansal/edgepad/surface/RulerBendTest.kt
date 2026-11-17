package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Test
import kotlin.math.hypot

/** A mark's start sits on an arc concentric with the screen's corner, at the corner radius less the inset. */
class RulerBendTest {
    @Test
    fun bendIsTheDisplayCornerWithAFloor() {
        assertEquals(120f, RulerGeometry.bend(120f, 3f), 0f)
        assertEquals(72f, RulerGeometry.bend(10f, 3f), 0f)
    }

    @Test
    fun markStartsAreConcentricWithTheScreenCorner() {
        val density = 3f
        val corner = 120f
        val path = Perimeter(1080f, 2400f, RulerGeometry.bend(corner, density))
        val inset = RulerGeometry.INSET_DP * density
        val pt = FloatArray(4)
        // Top-left corner's centre is (corner, corner); sample along that bend.
        val arc = path.lengthAt(0f)
        for (step in -4..4) {
            path.point(arc + step * 10f, pt)
            val x = pt[0] + pt[2] * inset
            val y = pt[1] + pt[3] * inset
            if (x < corner && y < corner) {
                assertEquals(corner - inset, hypot(x - corner, y - corner), 0.01f)
            }
        }
    }
}
