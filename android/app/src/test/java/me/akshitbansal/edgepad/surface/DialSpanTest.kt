package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Test

class DialSpanTest {
    private val after = FloatArray(2)
    private val before = FloatArray(2)

    @Test
    fun aLoneDialKeepsItsFullReach() {
        DialSpan.compute(floatArrayOf(100f), 50f, 1000f, FloatArray(0), 10f, after, before)
        assertEquals(50f, after[0], 0f)
        assertEquals(50f, before[0], 0f)
    }

    @Test
    fun twoDialsMeetAtTheMidpointLessTheGap() {
        // 60 apart along the path, reach 50 each: they would overlap by 40 without the cut.
        DialSpan.compute(floatArrayOf(100f, 160f), 50f, 1000f, FloatArray(0), 10f, after, before)
        assertEquals(25f, after[0], 0f)
        assertEquals(25f, before[1], 0f)
        // The other ends are not touched.
        assertEquals(50f, before[0], 0f)
        assertEquals(50f, after[1], 0f)
    }

    @Test
    fun theCutWorksAcrossTheStartOfThePath() {
        DialSpan.compute(floatArrayOf(990f, 30f), 50f, 1000f, FloatArray(0), 0f, after, before)
        assertEquals(20f, after[0], 0f)
        assertEquals(20f, before[1], 0f)
    }

    @Test
    fun aKeepOutRangeStopsTheRulerAtItsEdge() {
        // The range 130..170 lies ahead of the first dial and behind the second.
        DialSpan.compute(floatArrayOf(100f, 200f), 50f, 1000f, floatArrayOf(130f, 170f), 0f, after, before)
        assertEquals(30f, after[0], 0f)
        assertEquals(30f, before[1], 0f)
    }

    @Test
    fun eightSlotsRoundThePathStopShortOfEachOther() {
        // What stops a dial reaching into its neighbour now that a slot is only half an edge from the next:
        // eight of them 125 apart, each asking for 200, so without the cut every one would run through both.
        val centres = FloatArray(Perimeter.SLOTS) { it * 125f }
        val reachAfter = FloatArray(centres.size)
        val reachBefore = FloatArray(centres.size)
        DialSpan.compute(centres, 200f, 1000f, FloatArray(0), 10f, reachAfter, reachBefore)
        for (i in centres.indices) {
            val next = (i + 1) % centres.size
            assertEquals(57.5f, reachAfter[i], 1e-3f)
            assertEquals(57.5f, reachBefore[i], 1e-3f)
            // The two reaches that face each other leave the whole gap between their ends.
            assertEquals(125f - 10f, reachAfter[i] + reachBefore[next], 1e-3f)
        }
    }

    @Test
    fun reachNeverGoesNegative() {
        DialSpan.compute(floatArrayOf(100f, 104f), 50f, 1000f, FloatArray(0), 20f, after, before)
        assertEquals(0f, after[0], 0f)
        assertEquals(0f, before[1], 0f)
    }
}
