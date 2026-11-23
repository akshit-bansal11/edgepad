package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class HubPagesTest {
    private fun mask(screen: DashMask.Screen?) = DashMask.of(true, false, false, false, false, screen)

    @Test
    fun eachStatsPageOfALaptopSetsItsOwnBit() {
        assertEquals(DashMask.SYSTEM_SCREEN, mask(HubPages.system(HubPages.SYSTEM, false)))
        assertEquals(DashMask.NETWORK_SCREEN, mask(HubPages.system(HubPages.NETWORK, false)))
    }

    @Test
    fun thePhoneAsTheDeviceSetsNoBitOnEitherPage() {
        assertNull(HubPages.system(HubPages.SYSTEM, true))
        assertNull(HubPages.system(HubPages.NETWORK, true))
        assertEquals(0, mask(HubPages.system(HubPages.SYSTEM, true)))
        assertEquals(0, mask(HubPages.system(HubPages.NETWORK, true)))
    }

    @Test
    fun eachDeveloperPageSetsItsOwnBit() {
        assertEquals(DashMask.AI, mask(HubPages.developer(0)))
        assertEquals(DashMask.GITHUB_SCREEN, mask(HubPages.developer(HubPages.GIT)))
    }
}
