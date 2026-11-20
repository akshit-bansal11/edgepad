package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.link.StatsRing
import me.akshitbansal.edgepad.link.StatsSample
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** A card is drawn only for what the laptop reports, and the System widget's box is one card tall. */
class StatMetricTest {
    private fun sample(
        gpu: Int,
        vram: Int,
    ) = StatsSample(10, 20, gpu, vram, 0, 0, 0, 0, 0, 0, 0)

    @Test
    fun aGraphicsCardIsReportedOnlyOnceASampleCarriesIt() {
        val ring = StatsRing()
        assertTrue(StatMetric.CPU.reported(ring))
        assertTrue(StatMetric.RAM.reported(ring))
        assertFalse(StatMetric.GPU.reported(ring))
        ring.push(sample(-1, -1))
        assertFalse(StatMetric.GPU.reported(ring))
        assertFalse(StatMetric.VRAM.reported(ring))
        ring.push(sample(0, 5))
        assertTrue(StatMetric.GPU.reported(ring))
        assertTrue(StatMetric.VRAM.reported(ring))
    }

    @Test
    fun theSystemBoxIsOneSquareCardTall() {
        val (width, height) = WidgetPainter.boxSize(SurfaceWidget.SYSTEM, 360f, 780f, 1f)
        assertEquals((width - 3 * 6f) / 4, height, 0.001f)
    }
}
