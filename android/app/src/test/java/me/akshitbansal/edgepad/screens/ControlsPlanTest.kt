package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ControlsPlanTest {
    @Test
    fun mediumIsTheSizeThePopupAlwaysHad() {
        val medium = ControlsPlan.scale(ControlsIconSize.MEDIUM)
        assertEquals(128f, medium.tileWidth, 0f)
        assertEquals(112f, medium.tileHeight, 0f)
        assertEquals(40f, medium.icon, 0f)
        assertEquals(15f, medium.labelSp, 0f)
    }

    @Test
    fun smallIsUnderMediumIsUnderLarge() {
        val small = ControlsPlan.scale(ControlsIconSize.SMALL)
        val medium = ControlsPlan.scale(ControlsIconSize.MEDIUM)
        val large = ControlsPlan.scale(ControlsIconSize.LARGE)
        assertTrue(small.tileWidth < medium.tileWidth && medium.tileWidth < large.tileWidth)
        assertTrue(small.tileHeight < medium.tileHeight && medium.tileHeight < large.tileHeight)
        assertTrue(small.icon < medium.icon && medium.icon < large.icon)
        assertTrue(small.labelSp < medium.labelSp && medium.labelSp < large.labelSp)
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

    @Test
    fun thePopupTakesAboutNineTenthsOfTheWidthAndAtMostFourFifthsOfTheHeight() {
        val plan = ControlsPlan.plan(14, 9, 1000, 2000, 128, 112, 8, 16, 48, 16)
        assertEquals(920, plan.width)
        assertEquals(6, plan.columns)
        assertEquals(2, plan.rows)
        assertFalse(plan.scrolls)
        val short = ControlsPlan.plan(14, 9, 1000, 300, 128, 112, 8, 16, 48, 16)
        assertTrue(short.scrolls)
        assertEquals(240, short.height)
    }
}
