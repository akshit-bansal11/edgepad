package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ForegroundAppTest {
    @Test
    fun splitsOnTheFirstSlash() {
        assertEquals(ForegroundApp("a.exe", "x/y"), ForegroundApp.parse("a.exe/x/y"))
    }

    @Test
    fun emptyAndMalformedGiveNull() {
        assertNull(ForegroundApp.parse(""))
        assertNull(ForegroundApp.parse("noslash"))
        assertNull(ForegroundApp.parse("/name"))
        assertNull(ForegroundApp.parse("bad token/x"))
        assertNull(ForegroundApp.parse("a".repeat(65) + "/x"))
    }

    @Test
    fun cleansTheName() {
        assertEquals("a b c", ForegroundApp.parse("t/ a\u0001b|c ")?.name)
    }

    @Test
    fun cutsToFortyCodePointsWithoutSplittingAPair() {
        val name = "😀".repeat(41)
        assertEquals("😀".repeat(40), ForegroundApp.parse("t/$name")?.name)
    }
}
