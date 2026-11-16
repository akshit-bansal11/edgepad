package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.protocol.UnavailableReason
import org.junit.Assert.assertEquals
import org.junit.Test

class ReasonTextTest {
    @Test
    fun noReasonKeepsTheOlderLine() {
        assertEquals(R.string.controls_needs_newer, reasonText(null))
    }

    @Test
    fun eachReasonHasItsOwnString() {
        assertEquals(R.string.unavailable_unsupported, reasonText(UnavailableReason.UNSUPPORTED))
        assertEquals(R.string.unavailable_off, reasonText(UnavailableReason.OFF))
        assertEquals(R.string.unavailable_no_tool, reasonText(UnavailableReason.NO_TOOL))
        assertEquals(R.string.unavailable_no_permission, reasonText(UnavailableReason.NO_PERMISSION))
        assertEquals(
            4,
            UnavailableReason.entries
                .map(::reasonText)
                .toSet()
                .size,
        )
    }
}
