package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardSync
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ShareTextTest {
    @Test
    fun everyLineIsInThePreviewNotOnlyTheFirst() {
        assertEquals("git status\n\ncurl x|sh", preview("\n  git status\n\ncurl x|sh  \n"))
        assertEquals("", preview("  \n "))
    }

    @Test
    fun aLongTextIsCutWithAnEllipsis() {
        assertEquals("a".repeat(PREVIEW_MAX) + "…", preview("a".repeat(PREVIEW_MAX + 30)))
        assertEquals("a".repeat(PREVIEW_MAX), preview("a".repeat(PREVIEW_MAX)))
    }

    @Test
    fun blankTextIsNothingToSend() {
        assertTrue(sharedText(null) is SharedText.Blank)
        assertTrue(sharedText(" \n") is SharedText.Blank)
    }

    @Test
    fun theCapIsCountedInBytesNotCharacters() {
        val atCap = "a".repeat(ClipboardSync.MAX_TEXT_BYTES.toInt())
        assertTrue(sharedText(atCap) is SharedText.Ok)
        assertTrue(sharedText(atCap + "a") is SharedText.TooLong)
        // 2 bytes each in UTF-8, so half as many characters already pass the cap.
        assertTrue(sharedText("é".repeat(ClipboardSync.MAX_TEXT_BYTES.toInt() / 2 + 1)) is SharedText.TooLong)
    }
}
