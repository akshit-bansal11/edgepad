package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PinScreenTest {
    @Test
    fun anOlderLinksCountIsNotAWrongAnswer() {
        assertFalse(PinScreen.showsWrong(4, sentOnLink = false))
    }

    @Test
    fun fewerLeftAfterThisLinksAnswerIsAWrongAnswer() {
        assertTrue(PinScreen.showsWrong(4, sentOnLink = true))
    }

    @Test
    fun allFiveLeftIsNeverAWrongAnswer() {
        assertFalse(PinScreen.showsWrong(5, sentOnLink = true))
    }
}
