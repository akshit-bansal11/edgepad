package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
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
    fun gridsWhoseCellsAreUnderATouchTargetAreNotOffered() {
        // 1000 x 600 px, 10 px gaps, 100 px minimum cell. Five tiles: 1 x 5 cells are 192 x 600 wide, fine;
        // 5 x 1 cells are 1000 x 112 tall, fine; 15 in 1 x 15 would be 62 px wide, which is not.
        assertEquals(DeckLayout.choices(5), DeckLayout.fitting(5, 1000, 600, 10, 100))
        assertEquals(
            listOf(2 to 8, 3 to 5, 4 to 4, 5 to 3),
            DeckLayout.fitting(15, 1000, 600, 10, 100),
        )
    }

    @Test
    fun aRoomTooSmallForAnyCellOffersNothing() {
        assertTrue(DeckLayout.fitting(5, 90, 90, 10, 100).isEmpty())
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
}
