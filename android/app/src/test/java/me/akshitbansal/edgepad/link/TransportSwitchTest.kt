package me.akshitbansal.edgepad.link

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class TransportSwitchTest {
    @Test
    fun aTapWithNothingWorkingSelects() {
        assertTrue(selectsOnTap(tapped = true, connected = false, switchesConnected = false))
    }

    @Test
    fun aSwitchOfTransportSelectsEvenWhileConnected() {
        assertTrue(selectsOnTap(tapped = true, connected = true, switchesConnected = true))
    }

    @Test
    fun anotherLaptopWaitsOnItsRow() {
        assertFalse(selectsOnTap(tapped = true, connected = true, switchesConnected = false))
    }

    @Test
    fun noTapNeverSelects() {
        assertFalse(selectsOnTap(tapped = false, connected = false, switchesConnected = true))
    }
}
