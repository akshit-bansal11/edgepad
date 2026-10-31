package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

class LetterboxTest {
    private fun fit(
        areaW: Float,
        areaH: Float,
        screenW: Int,
        screenH: Int,
    ): Letterbox = checkNotNull(Letterbox.fit(areaW, areaH, screenW, screenH))

    @Test
    fun aWideLaptopInAPortraitAreaFillsTheWidthAndCentresVertically() {
        val box = fit(900f, 1600f, 1920, 1080)
        assertEquals(900f, box.width, 1e-3f)
        assertEquals(506.25f, box.height, 1e-3f)
        assertEquals(0f, box.left, 1e-3f)
        assertEquals((1600f - 506.25f) / 2f, box.top, 1e-3f)
    }

    @Test
    fun aWideLaptopInALandscapeAreaFillsTheHeightWhenTheAreaIsNarrower() {
        val box = fit(1600f, 900f, 1920, 1080)
        assertEquals(1600f, box.width, 1e-3f)
        assertEquals(900f, box.height, 1e-3f)
    }

    @Test
    fun aThreeByTwoLaptopInALandscapeAreaFillsTheHeight() {
        val box = fit(2000f, 900f, 3000, 2000)
        assertEquals(1350f, box.width, 1e-3f)
        assertEquals(900f, box.height, 1e-3f)
        assertEquals(325f, box.left, 1e-3f)
        assertEquals(0f, box.top, 1e-3f)
    }

    @Test
    fun aThreeByTwoLaptopInAPortraitAreaFillsTheWidth() {
        val box = fit(800f, 1800f, 3000, 2000)
        assertEquals(800f, box.width, 1e-3f)
        assertEquals(533.333f, box.height, 1e-2f)
    }

    @Test
    fun theBoxCornersAreTheWireCorners() {
        val box = fit(900f, 1600f, 1920, 1080)
        assertEquals(Pair(0, 0), box.toWire(box.left, box.top))
        assertEquals(Pair(65535, 65535), box.toWire(box.left + box.width, box.top + box.height))
    }

    @Test
    fun aTouchOutsideTheBoxClampsToTheNearestEdge() {
        val box = fit(900f, 1600f, 1920, 1080)
        assertEquals(Pair(0, 0), box.toWire(-50f, 0f))
        assertEquals(Pair(65535, 65535), box.toWire(5000f, 5000f))
        assertEquals(Pair(32767, 0), box.toWire(450f, 0f))
    }

    @Test
    fun toPixelsInvertsToWireAtTheCorners() {
        val box = fit(900f, 1600f, 1920, 1080)
        val (x, y) = box.toPixels(65535, 65535)
        assertEquals(box.left + box.width, x, 1e-2f)
        assertEquals(box.top + box.height, y, 1e-2f)
        assertEquals(Pair(box.left, box.top), box.toPixels(0, 0))
    }

    @Test
    fun anUnknownOrEmptySizeHasNoBox() {
        assertNull(Letterbox.fit(0f, 100f, 1920, 1080))
        assertNull(Letterbox.fit(100f, 100f, 0, 1080))
        assertNotNull(Letterbox.fit(100f, 100f, 1, 1))
    }
}
