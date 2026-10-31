package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.PenColor
import me.akshitbansal.edgepad.protocol.TextKind
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
        val stroke = strokes.strokes.single()
        assertEquals(4, stroke.points.size)
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
        val left = strokes.strokes.single()
        assertEquals(WirePoint(0, 0), left.points.first())
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
        assertEquals(PenColor.PURPLE.rgb, strokes.rgb)
    }

    @Test
    fun aStrokeRecordsTheToolAndLookItStartedWith() {
        val strokes = Strokes()
        strokes.select(PenTool.HIGHLIGHTER)
        strokes.selectColour(4)
        strokes.selectWidth(60)
        strokes.selectOpacity(40)
        strokes.drag(0, 0, 9, 9)
        strokes.select(PenTool.PEN)
        strokes.selectRgb(0x123456)
        strokes.selectWidth(10)
        strokes.selectOpacity(90)
        val stroke = strokes.strokes.single()
        assertEquals(PenTool.HIGHLIGHTER, stroke.tool)
        assertEquals(PenColor.YELLOW.rgb, stroke.rgb)
        assertEquals(60, stroke.width)
        assertEquals(40, stroke.opacity)
    }

    @Test
    fun anyRgbThatFitsInSixDigitsIsTaken() {
        val strokes = Strokes()
        assertTrue(strokes.selectRgb(0x123456))
        assertEquals(0x123456, strokes.rgb)
        assertTrue(strokes.selectRgb(0xFFFFFF))
        assertFalse(strokes.selectRgb(0x1000000))
        assertFalse(strokes.selectRgb(-1))
        assertEquals(0xFFFFFF, strokes.rgb)
    }

    @Test
    fun theWidthTakesOneToOneHundredAndTheLaptopTreatsUnderFiveAsFive() {
        val strokes = Strokes()
        assertEquals(20, strokes.width)
        assertFalse(strokes.selectWidth(0))
        assertFalse(strokes.selectWidth(101))
        assertEquals(20, strokes.width)
        assertTrue(strokes.selectWidth(100))
        assertEquals(100, strokes.width)
        assertTrue(strokes.selectWidth(1))
        assertEquals(Strokes.MIN_WIDTH, strokes.width)
    }

    @Test
    fun theOpacityTakesTenToOneHundred() {
        val strokes = Strokes()
        assertEquals(100, strokes.opacity)
        assertFalse(strokes.selectOpacity(9))
        assertFalse(strokes.selectOpacity(101))
        assertEquals(100, strokes.opacity)
        assertTrue(strokes.selectOpacity(10))
        assertEquals(10, strokes.opacity)
    }

    @Test
    fun aRememberedLookOutsideTheRangesIsBroughtInside() {
        val strokes = Strokes(rgb = 0x7FFFFFFF, width = 500, opacity = 0)
        assertEquals(0xFFFFFF, strokes.rgb)
        assertEquals(Strokes.MAX_WIDTH, strokes.width)
        assertEquals(Strokes.MIN_OPACITY, strokes.opacity)
    }

    @Test
    fun widthStepsAreLinearAndTwentyIsFourTenthsOfAPercent() {
        assertEquals(0.004f, Strokes.penShare(20), 1e-6f)
        assertEquals(0.001f, Strokes.penShare(5), 1e-6f)
        assertEquals(0.02f, Strokes.penShare(100), 1e-6f)
        assertEquals(0.001f, Strokes.penShare(1), 1e-6f)
        assertEquals(0.002f, Strokes.penShare(10), 1e-6f)
    }

    @Test
    fun theHighlighterKeepsFiveTimesTheWidthTheLaptopGivesIt() {
        assertEquals(0.02f, Strokes.widthShare(PenTool.HIGHLIGHTER), 1e-6f)
        assertEquals(0.1f, Strokes.widthShare(PenTool.HIGHLIGHTER, 100), 1e-6f)
        assertEquals(0.004f, Strokes.widthShare(PenTool.LINE), 1e-6f)
    }

    @Test
    fun alphaIsTheOpacityAndTheHighlighterMultipliesItsOwn() {
        assertEquals(255, Strokes.alpha(PenTool.PEN, 100))
        assertEquals(128, Strokes.alpha(PenTool.ARROW, 50))
        assertEquals(26, Strokes.alpha(PenTool.RECTANGLE, 10))
        assertEquals(89, Strokes.alpha(PenTool.HIGHLIGHTER, 100))
        assertEquals(45, Strokes.alpha(PenTool.HIGHLIGHTER, 50))
        assertEquals(9, Strokes.alpha(PenTool.HIGHLIGHTER, 10))
    }

    @Test
    fun aStockColourIsSentAsItsIndexAndAnyOtherAsSixHexDigits() {
        assertEquals(Frame.SetValue(ControlId.PEN_COLOR.id, 6), Strokes.colourFrame(PenColor.BLUE.rgb))
        assertEquals(Frame.SetValue(ControlId.PEN_COLOR.id, 0), Strokes.colourFrame(0x000000))
        assertEquals(Frame.Text(TextKind.PEN_RGB.id, "123456"), Strokes.colourFrame(0x123456))
        // Padded to six digits and upper case, which is all the laptop accepts.
        assertEquals(Frame.Text(TextKind.PEN_RGB.id, "00ABCD"), Strokes.colourFrame(0x00ABCD))
        assertEquals(Frame.Text(TextKind.PEN_RGB.id, "0000FE"), Strokes.colourFrame(0xFE))
    }

    @Test
    fun everyRgbTheWireCarriesIsExactlySixHexDigits() {
        val six = Regex("[0-9A-F]{6}")
        for (rgb in listOf(0x000001, 0x0A0B0C, 0xFFFFFF, 0x800000, 0x00FF00)) {
            val frame = Strokes.colourFrame(rgb)
            if (frame is Frame.Text) assertTrue(frame.text, six.matches(frame.text))
        }
    }

    @Test
    fun theStyleGoesOutAsColourWidthAndOpacity() {
        val strokes = Strokes(rgb = 0x123456, width = 35, opacity = 60)
        assertEquals(
            listOf(
                Frame.Text(TextKind.PEN_RGB.id, "123456"),
                Frame.SetValue(ControlId.PEN_WIDTH.id, 35),
                Frame.SetValue(ControlId.PEN_OPACITY.id, 60),
            ),
            strokes.styleFrames(),
        )
        // The defaults are what a laptop uses when it is told nothing, so a phone on them says the same thing.
        assertEquals(
            listOf(
                Frame.SetValue(ControlId.PEN_COLOR.id, 0),
                Frame.SetValue(ControlId.PEN_WIDTH.id, 20),
                Frame.SetValue(ControlId.PEN_OPACITY.id, 100),
            ),
            Strokes().styleFrames(),
        )
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
