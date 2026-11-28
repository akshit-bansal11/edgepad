package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ControlsPlanTest {
    /** A phone upright and sideways, in dp, then the popup's own chrome around the grid. */
    private val screens = listOf(360 to 780, 780 to 360, 320 to 480, 480 to 320)
    private val gap = 8
    private val padSide = 16
    private val header = 48
    private val padBottom = 16
    private val dots = 24

    private fun fit(
        cells: Int,
        screen: Pair<Int, Int>,
    ): ControlsPlan.Fit {
        val across = ControlsPlan.width(screen.first) - 2 * padSide
        val room = ControlsPlan.maxHeight(screen.second) - header - padBottom - dots
        return ControlsPlan.fit(cells, across, room, gap)
    }

    @Test
    fun everyCountFitsWithoutScrollingOnEveryScreen() {
        for (screen in screens) {
            val across = ControlsPlan.width(screen.first) - 2 * padSide
            val room = ControlsPlan.maxHeight(screen.second) - header - padBottom - dots
            for (cells in ControlsPlan.PER_PAGE) {
                val plan = fit(cells, screen)
                val height = plan.rows * plan.tileHeight + (plan.rows - 1) * gap
                val width = plan.columns * plan.tileWidth + (plan.columns - 1) * gap
                assertTrue("$cells on $screen is $height tall in $room", height <= room)
                assertTrue("$cells on $screen is $width wide in $across", width <= across)
                assertTrue(plan.columns * plan.rows >= cells)
                assertTrue("an empty row at $cells on $screen", (plan.rows - 1) * plan.columns < cells)
                assertTrue(plan.tileHeight <= plan.tileWidth)
            }
        }
    }

    @Test
    fun fewerTilesAreBiggerOnes() {
        for (screen in screens) {
            assertTrue(fit(4, screen).tileHeight > fit(12, screen).tileHeight)
            for (cells in ControlsPlan.PER_PAGE.first until ControlsPlan.PER_PAGE.last) {
                assertTrue(fit(cells, screen).tileHeight >= fit(cells + 1, screen).tileHeight)
            }
        }
    }

    @Test
    fun noTilesIsAnEmptyGrid() {
        assertEquals(0, ControlsPlan.fit(0, 300, 300, gap).rows)
    }

    @Test
    fun theIconIsTheChosenShareOfTheShorterSideUntilTheLabelNeedsTheRoom() {
        assertEquals(50, ControlsPlan.icon(120, 100, 50, 90))
        assertEquals(80, ControlsPlan.icon(100, 120, 80, 90))
        assertEquals(60, ControlsPlan.icon(100, 100, 80, 60))
        assertEquals(1, ControlsPlan.icon(100, 100, 30, 0))
    }

    @Test
    fun theIconShareRunsThirtyToEightyInFives() {
        assertEquals(30, ControlsPlan.ICON_PERCENT.first)
        assertEquals(80, ControlsPlan.ICON_PERCENT.last)
        val span = ControlsPlan.ICON_PERCENT.last - ControlsPlan.ICON_PERCENT.first
        assertEquals(0, span % ControlsPlan.ICON_PERCENT_STEP)
        assertTrue(ControlsPlan.DEFAULT_ICON_PERCENT in ControlsPlan.ICON_PERCENT)
    }

    @Test
    fun theTilesPerPageStayInTheirRange() {
        assertEquals(2, 0.coerceIn(ControlsPlan.PER_PAGE))
        assertEquals(12, 99.coerceIn(ControlsPlan.PER_PAGE))
        assertEquals(9, ControlsPlan.DEFAULT_PER_PAGE.coerceIn(ControlsPlan.PER_PAGE))
    }

    @Test
    fun fourteenTilesAtNineAPageFallOnTwoPages() {
        val pages = DeckLayout.pages(14, 9)
        assertEquals(2, pages.size)
        assertEquals(0..8, pages[0])
        assertEquals(9..13, pages[1])
    }
}
