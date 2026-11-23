package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.link.LimitEntry
import me.akshitbansal.edgepad.link.LimitWindow
import me.akshitbansal.edgepad.link.SpendEntry
import me.akshitbansal.edgepad.protocol.AgentToken
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AiLayoutTest {
    @Test
    fun oneToFourToolsMakeOneToFourColumns() {
        assertEquals(1, AiLayout.columns(0))
        for (n in 1..4) assertEquals(n, AiLayout.columns(n))
        assertEquals(4, AiLayout.columns(9))
    }

    @Test
    fun sidewaysColumnsShareTheWidthEqually() {
        // 800 dp less 2 x 16 of page margin, less the gaps, over the columns.
        assertEquals(768f, AiLayout.columnWidthDp(800f, 1, true), 0.01f)
        assertEquals(379f, AiLayout.columnWidthDp(800f, 2, true), 0.01f)
        assertEquals(249.33f, AiLayout.columnWidthDp(800f, 3, true), 0.01f)
        assertEquals(184.5f, AiLayout.columnWidthDp(800f, 4, true), 0.01f)
    }

    @Test
    fun uprightEveryColumnTakesTheFullWidth() {
        for (n in 1..4) assertEquals(328f, AiLayout.columnWidthDp(360f, n, false), 0.01f)
    }

    @Test
    fun ringsStayBetweenFortyFourAndEightyEight() {
        for (width in 140..520 step 20) {
            for (rings in 1..AiLayout.MAX_RINGS) {
                val side = AiLayout.ringDp(width.toFloat(), rings)
                assertTrue("w $width x $rings = $side", side in 44f..88f)
            }
        }
    }

    @Test
    fun threeRingsFitTheNarrowestSidewaysColumn() {
        val width = AiLayout.columnWidthDp(800f, 4, true)
        val side = AiLayout.ringDp(width, 3)
        assertTrue(side * 3 <= width - 2 * AiLayout.CARD_PAD_DP)
        assertFalse(AiLayout.showsReset(side))
    }

    @Test
    fun twoSidewaysColumnsGetBigRingsWithResetLines() {
        val side = AiLayout.ringDp(AiLayout.columnWidthDp(800f, 2, true), 3)
        assertEquals(88f, side, 0.01f)
        assertTrue(AiLayout.showsReset(side))
    }

    @Test
    fun uprightRingsTopOutAtEightyEight() {
        assertEquals(88f, AiLayout.ringDp(AiLayout.columnWidthDp(360f, 4, false), 3), 0.01f)
    }

    @Test
    fun fourRingsWrapToTwoRowsOfTwoInANarrowColumnAndStayOnOneRowInAWideOne() {
        val narrow = AiLayout.columnWidthDp(800f, 4, true)
        assertEquals(2, AiLayout.ringsPerRow(narrow, 4))
        assertEquals(3, AiLayout.ringsPerRow(narrow, 3))
        assertEquals(4, AiLayout.ringsPerRow(AiLayout.columnWidthDp(800f, 1, true), 4))
        assertEquals(4, AiLayout.ringsPerRow(AiLayout.columnWidthDp(360f, 1, false), 4))
        assertEquals(1, AiLayout.ringsPerRow(narrow, 0))
        // Two rows of two in the narrow column are as big as a ring may be.
        assertEquals(78.25f, AiLayout.ringDp(narrow, 2), 0.01f)
    }

    @Test
    fun spendIsFourAcrossWhenItFitsAndTwoByTwoWhenItDoesNot() {
        assertEquals(4, AiLayout.spendColumns(AiLayout.columnWidthDp(800f, 1, true)))
        assertEquals(4, AiLayout.spendColumns(AiLayout.columnWidthDp(800f, 2, true)))
        assertEquals(4, AiLayout.spendColumns(AiLayout.columnWidthDp(360f, 2, false)))
        assertEquals(2, AiLayout.spendColumns(AiLayout.columnWidthDp(800f, 3, true)))
        assertEquals(2, AiLayout.spendColumns(AiLayout.columnWidthDp(800f, 4, true)))
        assertEquals(2, AiLayout.spendColumns(AiLayout.columnWidthDp(320f, 1, false)))
    }

    @Test
    fun theCardKeepsTheCreditsAndDropsTheContext() {
        fun limit(window: LimitWindow) = LimitEntry(AgentToken.CLAUDE, window, 10, -1, 1)
        val five = limit(LimitWindow.Minutes(300))
        val week = limit(LimitWindow.Minutes(10_080))
        val fable = limit(LimitWindow.Named("seven_day_fable"))
        val opus = limit(LimitWindow.Named("seven_day_opus"))
        val credits = limit(LimitWindow.Named("spend_limit"))
        val context = limit(LimitWindow.Context)
        assertEquals(listOf(five, week, fable, credits), AiLayout.pick(listOf(credits, context, fable, week, five)))
        assertEquals(listOf(five, week, opus, credits), AiLayout.pick(listOf(opus, credits, fable, week, five)))
        assertEquals(listOf(five, week), AiLayout.pick(listOf(week, five, context)))
        assertEquals(emptyList<LimitEntry>(), AiLayout.pick(listOf(context)))
    }

    @Test
    fun spendCellsShowDashesForWhatIsUnknownAndDollarsOnlyWherePriced() {
        val entry = SpendEntry(AgentToken.CLAUDE, 5, 10, 20, 30, 40, 50, 60, false, -1, -1)
        val priced = AiLayout.spendCells(entry, true)
        assertEquals(
            listOf(SpendWindow.TODAY, SpendWindow.DAYS7, SpendWindow.DAYS30, SpendWindow.ALL),
            priced.map { it.window },
        )
        assertEquals(listOf<Long?>(10, 30, 50, null), priced.map { it.tokens })
        assertEquals(listOf<Long?>(20, 40, 60, null), priced.map { it.cents })
        assertEquals(listOf<Long?>(null, null, null, null), AiLayout.spendCells(entry, false).map { it.cents })
        assertEquals(listOf<Long?>(null, null, null, null), AiLayout.spendCells(null, true).map { it.tokens })
        assertEquals(90L, AiLayout.spendCells(entry.copy(tokAll = 90, centsAll = 9), true).last().tokens)
    }

    @Test
    fun logosShrinkFromThreeColumns() {
        assertEquals(28f, AiLayout.logoDp(1), 0f)
        assertEquals(28f, AiLayout.logoDp(2), 0f)
        assertEquals(24f, AiLayout.logoDp(3), 0f)
        assertEquals(24f, AiLayout.logoDp(4), 0f)
    }
}

class TileContrastTest {
    @Test
    fun lettersAreWhiteOnDarkTilesAndDarkOnLightOnes() {
        assertEquals(0xFFFFFFFF.toInt(), TileContrast.letter(0xFF2B2B2B.toInt()))
        assertEquals(0xFF111111.toInt(), TileContrast.letter(0xFFF2F2F2.toInt()))
    }

    @Test
    fun aTileNearTheGroundNeedsAnOutline() {
        assertTrue(TileContrast.needsOutline(0xFF2B2B2B.toInt(), 0xFF1C1C1E.toInt()))
        assertFalse(TileContrast.needsOutline(0xFFD97757.toInt(), 0xFF1C1C1E.toInt()))
    }
}
