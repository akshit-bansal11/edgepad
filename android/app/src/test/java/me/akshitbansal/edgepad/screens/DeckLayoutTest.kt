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
