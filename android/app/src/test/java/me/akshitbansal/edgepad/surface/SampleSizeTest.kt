package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Test

class SampleSizeTest {
    @Test
    fun anImageNoLargerThanTheScreenIsDecodedWhole() {
        assertEquals(1, sampleSize(longest = 2400, target = 2400))
        assertEquals(1, sampleSize(longest = 1000, target = 2400))
    }

    @Test
    fun aPhotoIsHalvedUntilOneMoreHalvingWouldTakeItUnderTheScreen() {
        // A 4000 px side against a 1080 px screen: 2000 fits, and 1000 would be smaller than the screen.
        assertEquals(2, sampleSize(longest = 4000, target = 1080))
        assertEquals(4, sampleSize(longest = 9600, target = 2400))
        assertEquals(8, sampleSize(longest = 30000, target = 2400))
    }

    @Test
    fun noScreenSizeMeansNoSampling() {
        assertEquals(1, sampleSize(longest = 4000, target = 0))
    }
}
