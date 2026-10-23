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
}
