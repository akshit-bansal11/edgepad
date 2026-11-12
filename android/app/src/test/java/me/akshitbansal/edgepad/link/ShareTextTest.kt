package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardSync
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ShareTextTest {
    @Test
    fun everyLineIsInThePreviewNotOnlyTheFirst() {
        // Indentation is kept (it is part of what is sent); trailing spaces and the blank ends are not shown.
        assertEquals(Preview("  git status\n\ncurl x|sh", 5, 0), preview("\n  git status\n\ncurl x|sh  \n"))
        assertEquals("", preview("  \n ").shown)
    }

    @Test
    fun blankPaddingCannotPushTextOutOfView() {
        // Spaces, a no-break space and an ideographic space are all blank: 200 padded lines show as one.
        val padding = "\n \u00A0\u3000\n".repeat(200)
        val shared = preview("safe$padding" + "rm -rf ~")
        assertEquals("safe\n\nrm -rf ~", shared.shown)
        assertEquals(401, shared.lines)
    }

    @Test
    fun everyKindOfLineBreakSplitsAndCounts() {
        // A TextView breaks on all of these, so a "one line" text padded with them must not look like one line.
        val padding = listOf(0x2028, 0x2029, 0x85, '\r'.code).joinToString("") { Char(it).toString() }.repeat(50)
        val shared = preview("safe$padding" + "rm -rf ~")
        assertEquals("safe\n\nrm -rf ~", shared.shown)
        assertEquals(201, shared.lines)
        assertEquals(Preview("a\nb", 2, 0), preview("a\r\nb"))
    }

    @Test
    fun aLongRunOfSpaceInsideALineIsCutToEight() {
        assertEquals("a" + " ".repeat(8) + "b", preview("a" + " ".repeat(500) + "b").shown)
        assertEquals("    indented", preview("    indented").shown)
    }

    @Test
    fun combiningMarksCannotPadOrPileUp() {
        val acute = Char(0x0301)
        // On spaces they draw nothing; piled on one letter they overlay the lines around it. Both cut to eight.
        assertEquals("a" + " ".repeat(8) + "b", preview("a" + " $acute".repeat(300) + "b").shown)
        assertEquals("x" + " ".repeat(8) + "y", preview("x" + acute.toString().repeat(300) + "y").shown)
        // Real accents, a letter between each, are left alone.
        val word = "e${acute}e${acute}e$acute"
        assertEquals(word, preview(word).shown)
    }

    @Test
    fun aTrailingLineBreakCountsAsALine() {
        assertEquals(Preview("ls", 2, 0), preview("ls\n"))
    }

    @Test
    fun charactersThatHideOrDisguiseTextShowAsReplacementMarks() {
        val mark = Char(0xFFFD)
        val rtlOverride = Char(0x202E)
        val zeroWidth = Char(0x200B)
        val hangulFiller = Char(0x3164)
        val privateUse = Char(0xE000)
        val tag = String(Character.toChars(0xE0041))
        assertEquals(
            "a${mark}b${mark}c${mark}d${mark}e$mark",
            preview("a${rtlOverride}b${zeroWidth}c${hangulFiller}d${privateUse}e$tag").shown,
        )
        assertEquals("tab\tkept", preview("tab\tkept").shown)
    }

    @Test
    fun aLongTextIsCutAndSaysHowMuchIsNotShown() {
        assertEquals(Preview("a".repeat(PREVIEW_MAX) + Char(0x2026), 1, 30), preview("a".repeat(PREVIEW_MAX + 30)))
        assertEquals(Preview("a".repeat(PREVIEW_MAX), 1, 0), preview("a".repeat(PREVIEW_MAX)))
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
