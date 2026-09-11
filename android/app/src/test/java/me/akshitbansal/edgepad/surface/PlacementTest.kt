package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PlacementTest {
    @Test
    fun theEndsOfAnEdgeAreCornersAndTheMiddleIsNot() {
        assertTrue(Placement(Edge.TOP, 0f).atCorner)
        assertTrue(Placement(Edge.TOP, 1f).atCorner)
        assertFalse(Placement(Edge.TOP, 0.1f).atCorner)
        assertFalse(Placement(Edge.LEFT, 0.5f).atCorner)
    }

    @Test
    fun aCornerIsAQuarterTurnAndAnEdgeAHalf() {
        assertEquals(45f, Placement(Edge.BOTTOM, 1f).halfSpanDeg)
        assertEquals(90f, Placement(Edge.BOTTOM, 0.5f).halfSpanDeg)
    }

    @Test
    fun theIndicatorPointsIntoTheScreen() {
        assertEquals(90f, Placement(Edge.TOP, 0.5f).indicatorDeg)
        assertEquals(180f, Placement(Edge.RIGHT, 0.5f).indicatorDeg)
        assertEquals(270f, Placement(Edge.BOTTOM, 0.5f).indicatorDeg)
        assertEquals(0f, Placement(Edge.LEFT, 0.5f).indicatorDeg)
        assertEquals(45f, Placement(Edge.TOP, 0f).indicatorDeg)
        assertEquals(135f, Placement(Edge.TOP, 1f).indicatorDeg)
        assertEquals(225f, Placement(Edge.BOTTOM, 1f).indicatorDeg)
        assertEquals(315f, Placement(Edge.BOTTOM, 0f).indicatorDeg)
        assertEquals(135f, Placement(Edge.RIGHT, 0f).indicatorDeg)
        assertEquals(315f, Placement(Edge.LEFT, 1f).indicatorDeg)
    }

    @Test
    fun theCentreSitsOnTheEdge() {
        assertEquals(50f, Placement(Edge.TOP, 0.5f).centreX(100f))
        assertEquals(0f, Placement(Edge.TOP, 0.5f).centreY(200f))
        assertEquals(100f, Placement(Edge.RIGHT, 0.25f).centreX(100f))
        assertEquals(50f, Placement(Edge.RIGHT, 0.25f).centreY(200f))
        assertEquals(200f, Placement(Edge.BOTTOM, 0f).centreY(200f))
    }
}
