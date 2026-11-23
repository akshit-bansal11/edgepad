package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ClipboardSheetTest {
    @Test
    fun keepsLinesAndTrims() {
        assertEquals("a\nb\nc", clipPreview("  a  \n b\n c \n"))
    }

    @Test
    fun dropsBlankLineRuns() {
        assertEquals("a\nb", clipPreview("a\n\n\r\n   \nb"))
    }

    @Test
    fun collapsesSpacesAndTabs() {
        assertEquals("a b c", clipPreview("a   b\t\tc"))
    }

    @Test
    fun cutsAtTwoHundredFortyWithAnEllipsis() {
        val out = clipPreview("x".repeat(500))
        assertEquals(241, out.length)
        assertTrue(out.endsWith("…"))
    }

    @Test
    fun shortTextHasNoEllipsis() {
        assertEquals("x".repeat(240), clipPreview("x".repeat(240)))
    }

    @Test
    fun neverSplitsASurrogatePair() {
        val out = clipPreview("x".repeat(239) + "😀" + "y".repeat(10))
        assertEquals("x".repeat(239) + "…", out)
    }

    @Test
    fun justNowIsUnderAMinute() {
        assertTrue(isJustNow(59_999))
        assertFalse(isJustNow(60_000))
        assertTrue(isJustNow(-5))
    }
}
