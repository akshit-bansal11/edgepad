package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Test

class PieceGeometryTest {
    private val out = FloatArray(PieceGeometry.TRANSPORT_RECTS)

    @Test
    fun `play disc is square around the piece centre at media size`() {
        PieceGeometry.transport(1000f, 500f, 0.5f, 0.8f, 2f, 1f, 48f, out)
        // 52dp at density 2 is a 104px disc, so a 52px half; the 48px touch half is smaller.
        assertEquals(500f - 52f, out[0], EPS)
        assertEquals(400f - 52f, out[1], EPS)
        assertEquals(500f + 52f, out[2], EPS)
        assertEquals(400f + 52f, out[3], EPS)
    }

    @Test
    fun `skips sit a gap either side and are a touch target wide`() {
        PieceGeometry.transport(1000f, 500f, 0.5f, 0.8f, 2f, 1f, 48f, out)
        val gap = 104f
        assertEquals(500f - gap - 48f, out[4], EPS)
        assertEquals(500f - gap + 48f, out[6], EPS)
        assertEquals(500f + gap - 48f, out[8], EPS)
        assertEquals(400f - 48f, out[5], EPS)
        assertEquals(400f + 48f, out[7], EPS)
    }

    @Test
    fun `media scale grows the disc and the gap together`() {
        PieceGeometry.transport(1000f, 500f, 0.5f, 0.8f, 2f, 1.5f, 48f, out)
        assertEquals(500f - 78f, out[0], EPS)
        assertEquals(500f + 156f - 48f, out[8], EPS)
    }

    @Test
    fun `a small scale never shrinks the disc under the touch target`() {
        PieceGeometry.transport(1000f, 500f, 0.5f, 0.8f, 2f, 0.5f, 48f, out)
        assertEquals(500f - 48f, out[0], EPS)
        assertEquals(500f + 48f, out[2], EPS)
        // The gap is then play + touch = 96px, past the scaled 52px.
        assertEquals(500f - 96f - 48f, out[4], EPS)
    }

    private companion object {
        const val EPS = 0.001f
    }
}
