package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Test

class DashMaskTest {
    private fun mask(
        active: Boolean = true,
        onSurface: Boolean = false,
        stats: Boolean = false,
        weather: Boolean = false,
        ai: Boolean = false,
        screen: DashMask.Screen? = null,
    ) = DashMask.of(active, onSurface, stats, weather, ai, screen)

    @Test
    fun theBitsAreTheWireOnes() {
        assertEquals(1, DashMask.STATS_WIDGET)
        assertEquals(2, DashMask.SYSTEM_SCREEN)
        assertEquals(4, DashMask.WEATHER_WIDGET)
        assertEquals(8, DashMask.AI)
        assertEquals(16, DashMask.GITHUB_SCREEN)
    }

    @Test
    fun surfaceWidgetsCountOnlyWhileTheSurfaceShows() {
        assertEquals(0, mask(onSurface = true))
        assertEquals(1, mask(onSurface = true, stats = true))
        assertEquals(4, mask(onSurface = true, weather = true))
        assertEquals(8, mask(onSurface = true, ai = true))
        assertEquals(13, mask(onSurface = true, stats = true, weather = true, ai = true))
        assertEquals(0, mask(onSurface = false, stats = true, weather = true, ai = true))
    }

    @Test
    fun eachScreenWatchesForItself() {
        assertEquals(2, mask(screen = DashMask.Screen.SYSTEM))
        assertEquals(8, mask(screen = DashMask.Screen.AI))
        assertEquals(16, mask(screen = DashMask.Screen.GITHUB))
    }

    @Test
    fun widgetsStayOffOnAScreenAndBitsDoNotDouble() {
        assertEquals(2, mask(stats = true, screen = DashMask.Screen.SYSTEM))
        assertEquals(8, mask(onSurface = true, ai = true, screen = DashMask.Screen.AI))
    }

    @Test
    fun pausedOrUnlinkedIsZero() {
        assertEquals(0, mask(active = false, onSurface = true, stats = true, weather = true, ai = true))
        for (screen in DashMask.Screen.entries) assertEquals(0, mask(active = false, screen = screen))
    }
}
