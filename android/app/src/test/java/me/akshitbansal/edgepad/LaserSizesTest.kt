package me.akshitbansal.edgepad

import org.junit.Assert.assertEquals
import org.junit.Test

class LaserSizesTest {
    @Test
    fun savedSizesMapToTheNearestOfThree() {
        assertEquals(10, LaserSizes.nearest(5))
        assertEquals(10, LaserSizes.nearest(12))
        assertEquals(15, LaserSizes.nearest(13))
        assertEquals(15, LaserSizes.nearest(17))
        assertEquals(20, LaserSizes.nearest(18))
        assertEquals(20, LaserSizes.nearest(100))
    }

    @Test
    fun theWireValuesMapToThemselves() {
        LaserSizes.ALL.forEach { assertEquals(it, LaserSizes.nearest(it)) }
    }

    @Test
    fun theDefaultIsMedium() {
        assertEquals(15, LaserSizes.DEFAULT)
    }

    @Test
    fun theRadiusIsTheLaptopsShareOfTheScreenHeight() {
        assertEquals(84f * 0.0075f, LaserSizes.radius(84f, 15), 0.0001f)
        assertEquals(1080f * 0.005f, LaserSizes.radius(1080f, 10), 0.001f)
    }
}
