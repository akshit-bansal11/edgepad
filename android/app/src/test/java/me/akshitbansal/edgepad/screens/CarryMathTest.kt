package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.screens.CarryMath.Box
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class CarryMathTest {
    private val delta = 0.001f

    @Test
    fun aCornerBoxScalesAndMovesThePageOntoIt() {
        val s = CarryMath.start(Box(0f, 0f, 100f, 200f), 1000f, 2000f)
        assertEquals(0.1f, s.scaleX, delta)
        assertEquals(0.1f, s.scaleY, delta)
        assertEquals(-450f, s.tx, delta)
        assertEquals(-900f, s.ty, delta)
    }

    @Test
    fun aBoxOutsideTheScreenIsClampedIntoIt() {
        val s = CarryMath.start(Box(-50f, -50f, 100f, 200f), 1000f, 2000f)
        assertEquals(0.1f, s.scaleX, delta)
        assertEquals(-450f, s.tx, delta)
        assertEquals(-900f, s.ty, delta)
    }

    @Test
    fun aZeroSizeBoxScalesToAPointAboutItsCentre() {
        val s = CarryMath.start(Box(600f, 1200f, 600f, 1200f), 1000f, 2000f)
        assertEquals(0.2f, s.scaleX, delta)
        assertEquals(0.2f, s.scaleY, delta)
        assertEquals(100f, s.tx, delta)
        assertEquals(200f, s.ty, delta)
    }

    @Test
    fun originsKeepTheLastEightNewestFirst() {
        val origins = CarryMath.Origins(8)
        for (i in 1..9) origins.push(i, Box(i.toFloat(), 0f, 0f, 0f))
        for (i in 9 downTo 2) assertEquals(i.toFloat(), origins.pop()?.l)
        assertNull(origins.pop())
    }

    @Test
    fun aMarkIsFreshForSixHundredMilliseconds() {
        assertTrue(CarryMath.fresh(1000L, 1600L))
        assertFalse(CarryMath.fresh(1000L, 1601L))
    }

    @Test
    fun onlyAChangeBetweenTwoScreensCarries() {
        assertFalse(CarryMath.shouldCarry(null, 1))
        assertFalse(CarryMath.shouldCarry(2, 2))
        assertTrue(CarryMath.shouldCarry(1, 2))
    }
}
