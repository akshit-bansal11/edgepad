package me.akshitbansal.edgepad.gamepad

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.hypot

/** The stick and trigger arithmetic the play surface runs on every touch event, at density 1. */
class PadAxisTest {
    @Test
    fun aThumbAtRestIsNothing() {
        assertEquals(0, PadAxis.stick(0f, 0f, TRAVEL, DEAD))
    }

    @Test
    fun insideTheDeadZoneIsNothing() {
        assertEquals(0, PadAxis.stick(10f, 10f, TRAVEL, DEAD))
    }

    @Test
    fun theRimIsFullDeflectionEitherWay() {
        assertEquals(PadAxis.MAX, PadAxis.stick(TRAVEL, TRAVEL, TRAVEL, DEAD))
        assertEquals(-PadAxis.MAX, PadAxis.stick(-TRAVEL, TRAVEL, TRAVEL, DEAD))
    }

    @Test
    fun pastTheRimIsStillOnlyFull() {
        // The thumb is clamped to the rim before it gets here, but a magnitude over 1 must not wrap the
        // axis round into the opposite direction if one ever does arrive.
        assertEquals(PadAxis.MAX, PadAxis.stick(200f, 200f, TRAVEL, DEAD))
    }

    @Test
    fun theDeadZoneComesOutOfTheTravelRatherThanOffTheTop() {
        // Half way from the edge of the dead zone to the rim is half deflection: 15 + 85/2 of 100 travel.
        assertEquals(16_384, PadAxis.stick(57.5f, 57.5f, TRAVEL, DEAD))
    }

    @Test
    fun aDiagonalStaysADiagonal() {
        // Scaling the magnitude rather than each axis is what keeps these two equal; scaling them one at a
        // time would push a 45 degree push out to the corner of the square and past full on both.
        val offset = 70f
        val distance = hypot(offset, offset)
        val x = PadAxis.stick(offset, distance, TRAVEL, DEAD)
        val y = PadAxis.stick(offset, distance, TRAVEL, DEAD)
        assertEquals(x, y)
        assertTrue("$x should be short of full", x < PadAxis.MAX)
    }

    @Test
    fun aTriggerRunsFromTheTopEdgeToTheBottom() {
        assertEquals(0, PadAxis.trigger(-HALF_HEIGHT, HALF_HEIGHT))
        assertEquals(128, PadAxis.trigger(0f, HALF_HEIGHT))
        assertEquals(PadAxis.TRIGGER_MAX, PadAxis.trigger(HALF_HEIGHT, HALF_HEIGHT))
    }

    @Test
    fun aFingerPastEitherEdgeOfATriggerDoesNotWrap() {
        // The hit area is wider than the control, so a finger on the slack around it lands outside.
        assertEquals(0, PadAxis.trigger(-100f, HALF_HEIGHT))
        assertEquals(PadAxis.TRIGGER_MAX, PadAxis.trigger(100f, HALF_HEIGHT))
    }

    private companion object {
        const val TRAVEL = 100f
        const val DEAD = 15f
        const val HALF_HEIGHT = 20f
    }
}
