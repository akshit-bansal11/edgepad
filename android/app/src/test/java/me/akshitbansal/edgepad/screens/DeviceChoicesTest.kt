package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class DeviceChoicesTest {
    private val laptops = listOf(DeviceRef("a", "Desk"), DeviceRef("b", "Travel"))

    @Test
    fun laptopsKeepTheirOrderAndThePhoneComesLast() {
        val rows = DeviceChoices.of(laptops, "a", "This phone", false)
        assertEquals(listOf("Desk", "Travel", "This phone"), rows.map { it.name })
        assertEquals(listOf(false, false, true), rows.map { it.phone })
    }

    @Test
    fun theSelectedLaptopIsTheOnlyCurrentRow() {
        val rows = DeviceChoices.of(laptops, "b", "This phone", false)
        assertEquals(listOf(false, true, false), rows.map { it.current })
    }

    @Test
    fun thePhoneIsCurrentAndNoLaptopIsWhenItIsTheDevice() {
        val rows = DeviceChoices.of(laptops, "a", "This phone", true)
        assertEquals(listOf(false, false, true), rows.map { it.current })
    }

    @Test
    fun withoutAPhoneNameOnlyLaptopsAreListed() {
        val rows = DeviceChoices.of(laptops, "a", null, false)
        assertEquals(2, rows.size)
        assertFalse(rows.any { it.phone })
        assertTrue(rows[0].current)
    }

    @Test
    fun noConnectedLaptopLeavesJustThePhone() {
        val rows = DeviceChoices.of(emptyList(), null, "This phone", true)
        assertEquals(1, rows.size)
        assertTrue(rows[0].phone && rows[0].current)
    }
}
