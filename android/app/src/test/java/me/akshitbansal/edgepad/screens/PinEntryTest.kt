package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.protocol.Pin
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PinEntryTest {
    private fun typed(vararg digits: Int): PinEntry = PinEntry().apply { digits.forEach { press(it) } }

    @Test
    fun sixDigitsCompleteThePin() {
        val entry = typed(1, 2, 3, 4, 5, 6)
        assertTrue(entry.isComplete)
        assertEquals(Pin("123456"), entry.pin())
    }

    @Test
    fun fewerThanSixAreNotComplete() {
        val entry = typed(1, 2, 3, 4, 5)
        assertFalse(entry.isComplete)
        assertEquals(5, entry.length)
    }

    @Test
    fun aSeventhDigitIsIgnored() {
        val entry = typed(1, 2, 3, 4, 5, 6, 7)
        assertEquals(6, entry.length)
        assertEquals(Pin("123456"), entry.pin())
    }

    @Test
    fun deleteRemovesTheLastDigit() {
        val entry = typed(1, 2, 3, 4, 5, 6)
        entry.delete()
        assertFalse(entry.isComplete)
        entry.press(0)
        assertEquals(Pin("123450"), entry.pin())
    }

    @Test
    fun deleteOnNothingStaysEmpty() {
        val entry = PinEntry()
        entry.delete()
        assertEquals(0, entry.length)
    }

    @Test
    fun clearEmptiesTheEntry() {
        val entry = typed(9, 9, 9, 9, 9, 9)
        entry.clear()
        assertEquals(0, entry.length)
        assertFalse(entry.isComplete)
    }

    @Test(expected = IllegalStateException::class)
    fun anIncompletePinCannotBeTaken() {
        typed(1, 2, 3).pin()
    }

    @Test(expected = IllegalArgumentException::class)
    fun aValueAboveNineIsNotADigit() {
        PinEntry().press(10)
    }
}
