package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class AirMathTest {
    private fun axis(
        rate: Float,
        dt: Float = 0.01f,
        carry: Float = 0f,
    ) = AirMath.axis(rate, dt, 1f, 0.05f, carry)

    @Test
    fun insideTheDeadZoneIsZero() {
        assertEquals(0f, axis(0.04f, carry = 0.9f), 0f)
        assertEquals(0f, axis(-0.04f), 0f)
    }

    @Test
    fun zeroOrNegativeDtIsZero() {
        assertEquals(0f, axis(1f, dt = 0f), 0f)
        assertEquals(0f, axis(1f, dt = -0.01f), 0f)
    }

    @Test
    fun signFollowsTheRate() {
        assertTrue(axis(1f) > 0f)
        assertTrue(axis(-1f) < 0f)
        assertEquals(axis(1f), -axis(-1f), 1e-4f)
    }

    @Test
    fun theRemainderCarriesAcrossCalls() {
        // 0.1 rad/s for 10 ms is 0.6 px: nothing whole at first, one pixel by the second event.
        val first = axis(0.1f)
        assertEquals(0, AirMath.whole(first))
        val second = axis(0.1f, carry = first - AirMath.whole(first))
        assertEquals(1, AirMath.whole(second))
    }

    @Test
    fun truncatesTowardZeroForBothSigns() {
        assertEquals(2, AirMath.whole(2.9f))
        assertEquals(-2, AirMath.whole(-2.9f))
    }

    @Test
    fun wholeIsClampedToTheCodecRange() {
        assertEquals(AirMath.MAX_STEP, AirMath.whole(1e9f))
        assertEquals(-AirMath.MAX_STEP, AirMath.whole(-1e9f))
    }

    private val out = FloatArray(2)

    @Test
    fun turningAboutVerticalMovesRightInAnyGrip() {
        // Flat, screen up: vertical is the phone's z. A clockwise turn from above is a negative z rate.
        AirMath.rates(0f, 0f, -1f, 0f, 0f, 9.8f, 0, out)
        assertEquals(1f, out[0], 1e-4f)
        // Upright, screen facing the user: vertical is the phone's y.
        AirMath.rates(0f, -1f, 0f, 0f, 9.8f, 0f, 0, out)
        assertEquals(1f, out[0], 1e-4f)
        // Held like a remote at 45 degrees: the same turn reads the same.
        val h = 9.8f / kotlin.math.sqrt(2f)
        AirMath.rates(0f, -0.7071f, -0.7071f, 0f, h, h, 0, out)
        assertEquals(1f, out[0], 1e-3f)
    }

    @Test
    fun tippingTheTopUpMovesThePointerUpInEveryRotation() {
        AirMath.rates(1f, 0f, 0f, 0f, 0f, 9.8f, 0, out)
        assertTrue(out[1] < 0f)
        AirMath.rates(0f, -1f, 0f, 0f, 0f, 9.8f, 1, out)
        assertTrue(out[1] < 0f)
        AirMath.rates(-1f, 0f, 0f, 0f, 0f, 9.8f, 2, out)
        assertTrue(out[1] < 0f)
        AirMath.rates(0f, 1f, 0f, 0f, 0f, 9.8f, 3, out)
        assertTrue(out[1] < 0f)
    }

    @Test
    fun noGravityReadingMeansNoTurn() {
        AirMath.rates(0f, 0f, 1f, 0f, 0f, 0f, 0, out)
        assertEquals(0f, out[0], 0f)
    }
}
