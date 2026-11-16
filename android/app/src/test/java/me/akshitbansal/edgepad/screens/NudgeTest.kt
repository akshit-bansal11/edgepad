package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Test

class NudgeTest {
    @Test
    fun aStepMovesOneGridCell() {
        // 96 px is a grid point at density 1 (12 px cells), far from the centre of 1000.
        assertEquals(0.108f, Nudge.step(0.096f, 1000, 1, 1f), 1e-4f)
        assertEquals(0.084f, Nudge.step(0.096f, 1000, -1, 1f), 1e-4f)
    }

    @Test
    fun aCellScalesWithDensity() {
        assertEquals(0.12f, Nudge.step(0.096f, 1000, 1, 2f), 1e-4f)
    }

    @Test
    fun itStaysInsideTheCanvas() {
        assertEquals(0f, Nudge.step(0f, 1000, -1, 1f), 0f)
        assertEquals(1f, Nudge.step(1f, 1000, 1, 1f), 0f)
    }

    @Test
    fun aStepNearTheMiddleSnapsToIt() {
        assertEquals(0.5f, Nudge.step(0.492f, 1000, 1, 1f), 0f)
    }

    @Test
    fun anUnmeasuredCanvasLeavesThePieceWhereItIs() {
        assertEquals(0.3f, Nudge.step(0.3f, 0, 1, 1f), 0f)
    }
}
