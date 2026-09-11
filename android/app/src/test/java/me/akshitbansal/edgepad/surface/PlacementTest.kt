package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PlacementTest {
    @Test
    fun wholeNumbersAreCornersAndTheRestAreEdges() {
        assertTrue(Placement(0f).atCorner)
        assertEquals(2, Placement(2f).corner)
        assertFalse(Placement(1.5f).atCorner)
        assertNull(Placement(1.5f).corner)
        assertEquals(1, Placement(1.5f).edge)
        assertEquals(3, Placement(3.5f).edge)
    }

    @Test
    fun ofWrapsRoundTheScreen() {
        assertEquals(0.5f, Placement.of(4.5f).at, 1e-4f)
        assertEquals(3.5f, Placement.of(-0.5f).at, 1e-4f)
    }

    @Test
    fun ofSnapsOntoANearbyCorner() {
        assertEquals(Placement(1f), Placement.of(1.04f))
        assertEquals(Placement(0f), Placement.of(3.97f))
        assertEquals(1.2f, Placement.of(1.2f).at, 1e-4f)
    }
}
