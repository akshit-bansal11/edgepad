package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class DeviceSwitchTest {
    @Test
    fun aStepMovesToTheNextOrPreviousLaptop() {
        assertEquals(2, DeviceSwitch.neighbour(count = 4, active = 1, direction = 1))
        assertEquals(0, DeviceSwitch.neighbour(count = 4, active = 1, direction = -1))
    }

    @Test
    fun aStepPastEitherEndStaysPutInsteadOfWrapping() {
        assertNull(DeviceSwitch.neighbour(count = 3, active = 2, direction = 1))
        assertNull(DeviceSwitch.neighbour(count = 3, active = 0, direction = -1))
    }

    @Test
    fun oneLaptopOrNoneHasNowhereToGo() {
        assertNull(DeviceSwitch.neighbour(count = 1, active = 0, direction = 1))
        assertNull(DeviceSwitch.neighbour(count = 0, active = -1, direction = 1))
    }

    @Test
    fun withNoActiveLaptopAStepLandsOnAnEnd() {
        assertEquals(0, DeviceSwitch.neighbour(count = 3, active = -1, direction = 1))
        assertEquals(2, DeviceSwitch.neighbour(count = 3, active = -1, direction = -1))
    }

    @Test
    fun aLongNameIsCutWithAnEllipsisAndAShortOneIsLeftAlone() {
        assertEquals("Work", DeviceSwitch.fit("Work"))
        assertEquals("a".repeat(DeviceSwitch.MAX_NAME), DeviceSwitch.fit("a".repeat(DeviceSwitch.MAX_NAME)))
        val fitted = DeviceSwitch.fit("a".repeat(30))
        assertEquals(DeviceSwitch.MAX_NAME, fitted.length)
        assertEquals('…', fitted.last())
    }
}
