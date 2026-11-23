package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Test

class ClockTest {
    @Test
    fun formatsLikeAPlayer() {
        assertEquals("0:00", Clock.format(0))
        assertEquals("1:24", Clock.format(84))
        assertEquals("59:59", Clock.format(3599))
        assertEquals("1:02:05", Clock.format(3725))
    }

    @Test
    fun aNegativeTimeIsZero() {
        assertEquals("0:00", Clock.format(-3))
    }

    @Test
    fun theAppNameGetsWhatTheTimesLeave() {
        assertEquals(40f, appRoom(120f, 80f), 0f)
        assertEquals(0f, appRoom(120f, 135f), 0f)
    }

    @Test
    fun theBoxGrowsOnlyByWhatALargerFontAdds() {
        assertEquals(0f, subLineGrowth(13f, 13f), 0f)
        assertEquals(0f, subLineGrowth(11f, 13f), 0f)
        assertEquals(13f, subLineGrowth(26f, 13f), 0f)
    }
}
