package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardSync
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ShareTextTest {
    @Test
    fun everyLineIsInThePreviewNotOnlyTheFirst() {
        assertEquals(Preview("git status\n\ncurl x|sh", 0), preview("\n  git status\n\ncurl x|sh  \n"))
        assertEquals(Preview("", 0), preview("  \n "))
    }

    @Test
    fun blankPaddingCannotPushTextOutOfView() {
        assertEquals("safe\n\nrm -rf ~", preview("safe" + "\n \n".repeat(200) + "rm -rf ~").shown)
    }

    @Test
    fun invisibleAndReorderingCharactersShowAsReplacementMarks() {
        assertEquals("a\uFFFDb\uFFFDc", preview("a\u202Eb\u200Bc").shown)
    }

    @Test
    fun aLongTextIsCutAndSaysHowMuchIsNotShown() {
        assertEquals(Preview("a".repeat(PREVIEW_MAX) + "…", 30), preview("a".repeat(PREVIEW_MAX + 30)))
        assertEquals(Preview("a".repeat(PREVIEW_MAX), 0), preview("a".repeat(PREVIEW_MAX)))
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
