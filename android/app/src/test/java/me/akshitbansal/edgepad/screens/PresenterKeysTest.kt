package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Test

class PresenterKeysTest {
    @Test
    fun nextIsPageDown() = assertEquals(0x22, slideKey(1))

    @Test
    fun previousIsPageUp() = assertEquals(0x21, slideKey(-1))

    @Test
    fun anyPositiveStepIsNext() = assertEquals(0x22, slideKey(5))

    @Test
    fun anyNegativeStepIsPrevious() = assertEquals(0x21, slideKey(-3))
}
