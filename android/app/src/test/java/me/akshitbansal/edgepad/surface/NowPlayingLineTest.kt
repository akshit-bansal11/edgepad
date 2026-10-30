package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Test

class NowPlayingLineTest {
    @Test
    fun withoutAScrubDialTheTimeFollowsTheApp() {
        assertEquals(SubLine.APP_AND_TIME, subLine("Spotify", 120, false))
    }

    @Test
    fun withoutAnAppOrAScrubDialItIsTheTimeAlone() {
        assertEquals(SubLine.TIME, subLine("", 120, false))
    }

    @Test
    fun withNoDurationItIsTheApp() {
        assertEquals(SubLine.APP, subLine("Spotify", 0, false))
    }

    @Test
    fun aScrubDialSuppressesTheTime() {
        assertEquals(SubLine.APP, subLine("Spotify", 120, true))
        assertEquals(SubLine.APP, subLine("", 120, true))
    }
}
