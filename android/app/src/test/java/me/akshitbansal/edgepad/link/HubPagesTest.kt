package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class HubPagesTest {
    private fun mask(screen: DashMask.Screen?) = DashMask.of(true, false, false, false, false, screen)

    @Test
    fun eachSystemPageSetsItsOwnBitAndThePhonePageSetsNone() {
        assertEquals(DashMask.SYSTEM_SCREEN, mask(HubPages.system(HubPages.LAPTOP)))
        assertNull(HubPages.system(HubPages.PHONE))
        assertEquals(0, mask(HubPages.system(HubPages.PHONE)))
        assertEquals(DashMask.NETWORK_SCREEN, mask(HubPages.system(HubPages.NETWORK)))
    }

    @Test
    fun eachDeveloperPageSetsItsOwnBit() {
        assertEquals(DashMask.AI, mask(HubPages.developer(0)))
        assertEquals(DashMask.GITHUB_SCREEN, mask(HubPages.developer(HubPages.GIT)))
    }

    @Test
    fun theLaptopPageReadsNothingOfThePhone() {
        assertFalse(HubPages.readsPhone(HubPages.LAPTOP))
        assertTrue(HubPages.readsPhone(HubPages.PHONE))
        assertTrue(HubPages.readsPhone(HubPages.NETWORK))
    }
}
