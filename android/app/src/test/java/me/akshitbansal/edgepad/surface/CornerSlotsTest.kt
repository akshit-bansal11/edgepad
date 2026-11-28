package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class CornerSlotsTest {
    @Test
    fun twoSlotsShareEachCorner() {
        for (slot in 0 until Perimeter.SLOTS) assertEquals(slot / 2, CornerSlots.corner(slot))
    }

    @Test
    fun theInheritedHalfIsTheSideEdge() {
        assertEquals(listOf(0, 3, 4, 7), (0 until Perimeter.CORNERS).map(CornerSlots::inheritedSlot))
    }

    @Test
    fun inheritedSlotAndFallbackAgreeForAllEightSlots() {
        for (slot in 0 until Perimeter.SLOTS) {
            val corner = CornerSlots.corner(slot)
            if (CornerSlots.inheritedSlot(corner) == slot) {
                assertEquals(corner, CornerSlots.fallbackFor(slot))
            } else {
                assertNull(CornerSlots.fallbackFor(slot))
            }
        }
    }

    @Test
    fun seedKeepsTheFourCornersAndFillsTheOthersWithTheFirstUnused() {
        val all = listOf("a", "b", "c", "d", "e", "f", "g", "h")
        val seeded = CornerSlots.seed(listOf("c", null, "a", "h"), all)
        // Slots 0, 3, 4 and 7 are the corners' own; 1, 2, 5 and 6 take b, d, e and f, the first four nobody holds.
        assertEquals(listOf("c", "b", "d", null, "a", "e", "f", "h"), seeded)
    }

    @Test
    fun seedLeavesAHalfEmptyWhenTheDialsRunOut() {
        val seeded = CornerSlots.seed(listOf("a", "b", "c", "d"), listOf("a", "b", "c", "d", "e"))
        assertEquals(listOf("a", "e", null, "b", "c", null, null, "d"), seeded)
    }

    @Test
    fun seedingLeavesTheFourDialLayoutUntouched() {
        val corners = listOf("a", "b", null, "d")
        val before = corners.toList()
        CornerSlots.seed(corners, listOf("a", "b", "c", "d"))
        assertEquals(before, corners)
    }
}
