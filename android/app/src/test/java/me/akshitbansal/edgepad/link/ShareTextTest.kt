package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardSync
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ShareTextTest {
    @Test
    fun theFirstLineWithSomethingOnItIsThePreview() {
        assertEquals("hello", previewLine("\n  \n  hello  \nsecond"))
        assertEquals("", previewLine("  \n "))
    }

    @Test
    fun aLongLineIsCutWithAnEllipsis() {
        val preview = previewLine("a".repeat(PREVIEW_MAX + 30))
        assertEquals("a".repeat(PREVIEW_MAX) + "…", preview)
        assertEquals("a".repeat(PREVIEW_MAX), previewLine("a".repeat(PREVIEW_MAX)))
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
