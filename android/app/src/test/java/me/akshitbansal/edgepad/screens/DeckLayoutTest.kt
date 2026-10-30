package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class DeckLayoutTest {
    @Test
    fun fiveTilesOfferFourGrids() {
        assertEquals(listOf(1 to 5, 2 to 3, 3 to 2, 5 to 1), DeckLayout.choices(5))
    }

    @Test
    fun fifteenTilesOfferSevenGrids() {
        assertEquals(
            listOf(1 to 15, 2 to 8, 3 to 5, 4 to 4, 5 to 3, 8 to 2, 15 to 1),
            DeckLayout.choices(15),
        )
    }

    @Test
    fun smallCountsAndOutOfRangeCounts() {
        assertEquals(listOf(1 to 1), DeckLayout.choices(1))
        assertEquals(listOf(1 to 2, 2 to 1), DeckLayout.choices(2))
        assertTrue(DeckLayout.choices(0).isEmpty())
        assertTrue(DeckLayout.choices(16).isEmpty())
    }

    @Test
    fun noOfferedGridHasAnEmptyRow() {
        for (count in 1..15) {
            for ((rows, columns) in DeckLayout.choices(count)) {
                assertTrue(rows * columns >= count)
                assertTrue((rows - 1) * columns < count)
            }
        }
    }

    @Test
    fun columnsForReadsTheOfferedGridsAndNothingElse() {
        assertEquals(listOf(6, 3, 2, 1), listOf(1, 2, 3, 6).map { DeckLayout.columnsFor(6, it) })
        assertNull(DeckLayout.columnsFor(6, 4))
        assertNull(DeckLayout.columnsFor(6, 5))
        assertNull(DeckLayout.columnsFor(6, 0))
    }

    @Test
    fun rowsFitOnlyAtFullTouchHeightGapsIncluded() {
        assertTrue(DeckLayout.rowsFit(rows = 1, down = 48, touch = 48, gap = 8))
        assertTrue(DeckLayout.rowsFit(rows = 3, down = 160, touch = 48, gap = 8))
        assertFalse(DeckLayout.rowsFit(rows = 3, down = 159, touch = 48, gap = 8))
        assertFalse(DeckLayout.rowsFit(rows = 6, down = 300, touch = 48, gap = 8))
    }

    @Test
    fun theCrossTakesAFullTouchTargetOnABigTile() {
        assertEquals(48, DeckLayout.crossHit(side = 200, touch = 48, floor = 24))
        assertEquals(48, DeckLayout.crossHit(side = 96, touch = 48, floor = 24))
    }

    @Test
    fun theCrossTakesHalfASmallTileAndNeverLessThanTheFloor() {
        assertEquals(30, DeckLayout.crossHit(side = 60, touch = 48, floor = 24))
        assertEquals(24, DeckLayout.crossHit(side = 48, touch = 48, floor = 24))
        assertEquals(24, DeckLayout.crossHit(side = 30, touch = 48, floor = 24))
    }

    @Test
    fun columnsForAgreesWithChoices() {
        for (count in 2..10) {
            val offered = DeckLayout.choices(count)
            for (rows in 0..count + 1) {
                assertEquals(offered.firstOrNull { it.first == rows }?.second, DeckLayout.columnsFor(count, rows))
            }
        }
    }

    @Test
    fun equalNamesGetDistinctKeys() {
        assertEquals(
            listOf("Chrome", "Code", "Chrome#2", "Chrome#3"),
            DeckLayout.keys(listOf("Chrome", "Code", "Chrome", "Chrome")),
        )
    }

    @Test
    fun storedOrderWinsAndNewMacrosFollow() {
        val current = listOf("A", "B", "C", "D")
        assertEquals(listOf(2, 0, 1, 3), DeckLayout.order(listOf("C", "A"), current))
    }

    @Test
    fun duplicateNamesKeepTheirOwnPlaces() {
        val current = listOf("Chrome", "Code", "Chrome")
        assertEquals(listOf(2, 1, 0), DeckLayout.order(listOf("Chrome#2", "Code", "Chrome"), current))
    }

    @Test
    fun aRenamedMacroIsAppendedAndStaleNamesAreIgnored() {
        val current = listOf("A", "B2", "C")
        assertEquals(listOf(2, 0, 1), DeckLayout.order(listOf("C", "Gone", "A", "B"), current))
    }

    @Test
    fun moveThenStoreThenOrderIsStable() {
        val current = listOf("A", "B", "C", "D")
        val moved = DeckLayout.move(DeckLayout.order(emptyList(), current), 3, 0)
        assertEquals(listOf(3, 0, 1, 2), moved)
        val stored = moved.map { DeckLayout.keys(current)[it] }
        assertEquals(moved, DeckLayout.order(stored, current))
    }

    @Test
    fun moveOutOfRangeChangesNothing() {
        assertEquals(listOf(0, 1), DeckLayout.move(listOf(0, 1), 0, 5))
    }

    // Two rows of two 100 px cells with 10 px gaps: slots 0 and 1 on top, 2 and 3 under them.
    private val lefts = intArrayOf(0, 110, 0, 110)
    private val tops = intArrayOf(0, 0, 110, 110)

    @Test
    fun aPointInsideACellIsThatSlot() {
        assertEquals(0, DeckLayout.slotAt(0f, 0f, lefts, tops, 100, 100))
        assertEquals(1, DeckLayout.slotAt(150f, 50f, lefts, tops, 100, 100))
        assertEquals(3, DeckLayout.slotAt(209f, 209f, lefts, tops, 100, 100))
    }

    @Test
    fun aGapOrOutsideIsNoSlot() {
        assertEquals(-1, DeckLayout.slotAt(105f, 50f, lefts, tops, 100, 100))
        assertEquals(-1, DeckLayout.slotAt(50f, 210f, lefts, tops, 100, 100))
        assertEquals(-1, DeckLayout.slotAt(-1f, 50f, lefts, tops, 100, 100))
    }

    @Test
    fun holdingATileLaterSlidesTheOnesBetweenBackOne() {
        // Tile 0 held over slot 2: tiles 1 and 2 slide back a place, tile 3 stays.
        assertEquals(listOf(2, 0, 1, 3), DeckLayout.places(4, 0, 2).toList())
    }

    @Test
    fun holdingATileEarlierSlidesTheOnesBetweenOnOne() {
        assertEquals(listOf(1, 2, 3, 0), DeckLayout.places(4, 3, 0).toList())
    }

    @Test
    fun holdingATileOverItsOwnSlotMovesNothing() {
        assertEquals(listOf(0, 1, 2), DeckLayout.places(3, 1, 1).toList())
    }

    @Test
    fun whatTheDragShowsIsWhatTheDropStores() {
        val moved = DeckLayout.move(listOf(0, 1, 2, 3, 4), 1, 3)
        val places = DeckLayout.places(5, 1, 3)
        moved.forEachIndexed { slot, tile -> assertEquals(slot, places[tile]) }
    }

    @Test
    fun pagesHoldPerPageEachAndOnlyTheLastIsShort() {
        for (perPage in 2..10) {
            assertTrue(DeckLayout.pages(0, perPage).isEmpty())
            for (count in listOf(1, perPage, perPage + 1, 15)) {
                val pages = DeckLayout.pages(count, perPage)
                assertEquals((count + perPage - 1) / perPage, pages.size)
                assertEquals((0 until count).toList(), pages.flatMap { it.toList() })
                pages.dropLast(1).forEach { assertEquals(perPage, it.count()) }
                assertTrue(pages.last().count() in 1..perPage)
            }
        }
    }

    @Test
    fun everyPageIsPlannedAsTheSameGridThatFitsPerPage() {
        // The deck plans each page for perPage cells whatever the page holds, so one grid serves them all.
        for (perPage in 2..10) {
            val grids = DeckLayout.choices(perPage)
            assertTrue(grids.isNotEmpty())
            assertTrue(grids.all { (rows, columns) -> rows * columns >= perPage })
        }
    }
}
