package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.hypot

class StrokesTest {
    private fun Strokes.drag(vararg points: Int) {
        pen(WirePoint(points[0], points[1]), Strokes.STATE_DOWN)
        for (i in 2 until points.size - 2 step 2) pen(WirePoint(points[i], points[i + 1]), Strokes.STATE_MOVE)
        pen(WirePoint(points[points.size - 2], points[points.size - 1]), Strokes.STATE_UP)
    }

    @Test
    fun aFreehandStrokeKeepsEveryPoint() {
        val strokes = Strokes()
        strokes.drag(0, 0, 1, 1, 2, 2, 3, 3)
        assertEquals(4, strokes.strokes.single().points.size)
        assertNull(strokes.active)
    }

    @Test
    fun aShapeKeepsOnlyTheDownPointAndTheLatestOne() {
        val strokes = Strokes()
        strokes.select(PenTool.RECTANGLE)
        strokes.drag(10, 10, 20, 20, 30, 30, 40, 50)
        assertEquals(listOf(WirePoint(10, 10), WirePoint(40, 50)), strokes.strokes.single().points)
    }

    @Test
    fun aMoveOrUpWithNoFingerDownIsIgnored() {
        val strokes = Strokes()
        assertFalse(strokes.pen(WirePoint(1, 1), Strokes.STATE_MOVE))
        assertFalse(strokes.pen(WirePoint(1, 1), Strokes.STATE_UP))
        assertFalse(strokes.pen(WirePoint(1, 1), 7))
        assertTrue(strokes.strokes.isEmpty())
    }

    @Test
    fun aSecondDownCommitsTheStrokeWhoseUpWasLost() {
        val strokes = Strokes()
        strokes.pen(WirePoint(0, 0), Strokes.STATE_DOWN)
        strokes.pen(WirePoint(5, 5), Strokes.STATE_DOWN)
        assertEquals(1, strokes.strokes.size)
        assertNotNull(strokes.active)
    }

    @Test
    fun undoTakesBackTheLastFinishedStrokeOnly() {
        val strokes = Strokes()
        strokes.drag(0, 0, 1, 1)
        strokes.drag(2, 2, 3, 3)
        assertTrue(strokes.undo())
        assertEquals(WirePoint(0, 0), strokes.strokes.single().points.first())
        assertTrue(strokes.undo())
        assertFalse(strokes.undo())
    }

    @Test
    fun clearEmptiesTheBoardAndTheStrokeInProgress() {
        val strokes = Strokes()
        strokes.drag(0, 0, 1, 1)
        strokes.pen(WirePoint(2, 2), Strokes.STATE_DOWN)
        strokes.clear()
        assertTrue(strokes.strokes.isEmpty())
        assertNull(strokes.active)
    }

    @Test
    fun signModeTakesOnlyThePen() {
        val strokes = Strokes()
        strokes.select(PenTool.ARROW)
        strokes.signing = true
        assertEquals(PenTool.PEN, strokes.tool)
        assertFalse(strokes.select(PenTool.LINE))
        assertTrue(strokes.select(PenTool.PEN))
    }

    @Test
    fun aColourOutsideThePaletteIsRefused() {
        val strokes = Strokes()
        assertTrue(strokes.selectColour(7))
        assertFalse(strokes.selectColour(8))
        assertFalse(strokes.selectColour(-1))
        assertEquals(7, strokes.colour)
    }

    @Test
    fun aStrokeRecordsTheToolAndColourItStartedWith() {
        val strokes = Strokes()
        strokes.select(PenTool.HIGHLIGHTER)
        strokes.selectColour(4)
        strokes.drag(0, 0, 9, 9)
        strokes.select(PenTool.PEN)
        assertEquals(PenTool.HIGHLIGHTER, strokes.strokes.single().tool)
        assertEquals(4, strokes.strokes.single().colour)
    }

    @Test
    fun theHighlighterIsFiveTimesThePenWidth() {
        assertEquals(5f, Strokes.widthShare(PenTool.HIGHLIGHTER) / Strokes.widthShare(PenTool.PEN), 1e-3f)
    }

    @Test
    fun anArrowHeadHasWingsFourPenWidthsLongAtTheTip() {
        val wings = checkNotNull(Strokes.arrowWings(0f, 0f, 100f, 0f, 2f))
        // Both wings lie behind the tip (x below 100), mirrored about the shaft, 8 units from the tip.
        assertTrue(wings[0] < 100f && wings[2] < 100f)
        assertEquals(wings[1], -wings[3], 1e-3f)
        assertEquals(8f, hypot(wings[0] - 100f, wings[1]), 1e-3f)
    }

    @Test
    fun anArrowWithNoLengthHasNoHead() {
        assertNull(Strokes.arrowWings(5f, 5f, 5f, 5f, 2f))
    }
}
