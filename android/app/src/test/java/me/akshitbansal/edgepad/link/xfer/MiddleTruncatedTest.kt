package me.akshitbansal.edgepad.link.xfer

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class MiddleTruncatedTest {
    @Test
    fun aShortNameIsLeftAlone() {
        assertEquals("report.pdf", middleTruncated("report.pdf"))
        assertEquals("x".repeat(28), middleTruncated("x".repeat(28)))
    }

    @Test
    fun aLongNameKeepsItsStartAndItsExtension() {
        val cut = middleTruncated("quarterly-report-final-v2.pdf")
        assertEquals("quarterly-report-fina…v2.pdf", cut)
        assertEquals(28, cut.length)
    }

    @Test
    fun aLongNameWithoutAnExtensionKeepsBothEnds() {
        val cut = middleTruncated("a".repeat(20) + "b".repeat(20))
        assertEquals(28, cut.length)
        assertTrue(cut.startsWith("aaaa") && cut.endsWith("bbbb") && '…' in cut)
    }

    @Test
    fun aVeryLongExtensionIsNotTreatedAsOne() {
        val cut = middleTruncated("name." + "e".repeat(40))
        assertEquals(28, cut.length)
    }

    @Test
    fun aSmallLimitStillCuts() {
        assertEquals("a…f", middleTruncated("abcdef", max = 3))
    }

    @Test
    fun aSurrogatePairIsNeverSplit() {
        val emoji = "😀"
        val cut = middleTruncated(emoji.repeat(30) + ".txt")
        assertEquals(cut.count { it.isHighSurrogate() }, cut.count { it.isLowSurrogate() })
    }
}
