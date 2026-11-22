package me.akshitbansal.edgepad.screens

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
    fun logosShrinkFromThreeColumns() {
        assertEquals(28f, AiLayout.logoDp(1), 0f)
        assertEquals(28f, AiLayout.logoDp(2), 0f)
        assertEquals(24f, AiLayout.logoDp(3), 0f)
        assertEquals(24f, AiLayout.logoDp(4), 0f)
    }
}
