package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Test

class OpenAppsTest {
    @Test
    fun linesBecomeAppsInOrderAndRepeatedTokensKeepTheFirst() {
        val apps = parseOpenApps("code.exe\tCode\nspotify.exe\tSpotify\ncode.exe\tCode again")

        assertEquals(listOf(ForegroundApp("code.exe", "Code"), ForegroundApp("spotify.exe", "Spotify")), apps)
    }

    @Test
    fun aLineWithoutATabOrWithABadTokenIsDropped() {
        assertEquals(listOf(ForegroundApp("a.exe", "A")), parseOpenApps("notab\nbad token\tX\n\na.exe\tA"))
        assertEquals(emptyList<ForegroundApp>(), parseOpenApps(""))
    }

    @Test
    fun aNameKeepsItsSlashesAndIsCleanedLikeTheAppInFront() {
        assertEquals(listOf(ForegroundApp("a.exe", "A/B C")), parseOpenApps("a.exe\tA/B\u0001C"))
    }

    @Test
    fun atMostFortyAppsAreKept() {
        val text = (0 until 60).joinToString("\n") { "app$it.exe\tApp $it" }

        assertEquals(40, parseOpenApps(text).size)
    }
}
