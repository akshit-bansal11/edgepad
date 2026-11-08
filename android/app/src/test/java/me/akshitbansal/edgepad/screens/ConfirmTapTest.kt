package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ConfirmTapTest {
    @Test
    fun theFirstTapOnlyArmsAndTheSecondRuns() {
        val guard = ConfirmTap()
        assertFalse(guard.tap())
        assertTrue(guard.armed)
        assertTrue(guard.tap())
        assertFalse(guard.armed)
    }

    @Test
    fun afterRunningItNeedsArmingAgain() {
        val guard = ConfirmTap()
        guard.tap()
        guard.tap()
        assertFalse(guard.tap())
    }
}
