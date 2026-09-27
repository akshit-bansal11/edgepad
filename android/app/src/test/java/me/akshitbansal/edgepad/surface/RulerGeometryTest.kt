package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test
import kotlin.math.abs

/**
 * No two marks of a corner ruler may cross. Marks are laid out exactly as [RulerPainter] lays them out, in dp
 * on a 400 by 800 dp screen: a dot for each minor notch, a pill for every fifth, and the indicator, at every
 * corner and at a range of slide positions, so notches pass by the indicator and round the bend.
 */
class RulerGeometryTest {
    @Test
    fun noTwoMarksCrossAtAnyHeightArmedOrNotOnAnyDisplayRounding() {
        for (tenths in MIN_TENTHS..MAX_TENTHS) {
            val height = tenths / TENTHS
            for (display in MIN_DISPLAY..MAX_DISPLAY step DISPLAY_STEP) {
                val path = Perimeter(WIDTH, HEIGHT, RulerGeometry.bend(height, display.toFloat()))
                for (armed in listOf(false, true)) {
                    assertNull(
                        "height $height, display rounding $display dp, armed $armed",
                        firstCrossing(path, height, armed),
                    )
                }
            }
        }
    }

    /** The bug the bend fixes: on the display's own 24 dp rounding a tall armed dial folds over itself. */
    @Test
    fun theDisplaysRoundingAloneLetsATallDialCross() {
        assertNotNull(firstCrossing(Perimeter(WIDTH, HEIGHT, MIN_DISPLAY.toFloat()), MAX_TENTHS / TENTHS, true))
    }

    /** The first pair of crossing marks as "s1 × s2", or null when none cross. */
    private fun firstCrossing(
        path: Perimeter,
        height: Float,
        armed: Boolean,
    ): String? {
        val pt = FloatArray(4)
        for (corner in 0 until Perimeter.CORNERS) {
            val centre = path.lengthAt(corner.toFloat())
            var slid = 0f
            while (slid < Dial.NOTCH_DP) {
                val marks = ArrayList<Mark>()
                path.point(centre, pt)
                marks.add(Mark(0f, pt, RulerGeometry.INSET_DP, RulerGeometry.indicatorEnd(height, armed)))
                for (n in -NOTCHES..NOTCHES) {
                    val s = slid + n * Dial.NOTCH_DP
                    path.point(centre + s, pt)
                    marks.add(
                        if (n % Dial.MAJOR_EVERY == 0) {
                            Mark(s, pt, RulerGeometry.INSET_DP, RulerGeometry.majorEnd(height, armed))
                        } else {
                            Mark(
                                s,
                                pt,
                                RulerGeometry.INSET_DP,
                                RulerGeometry.INSET_DP + RulerGeometry.dotDiameter(height),
                            )
                        },
                    )
                }
                for (i in marks.indices) {
                    for (j in i + 1 until marks.size) {
                        // A notch right under the indicator lies along it: overlap, drawn ducked, not a crossing.
                        if (abs(marks[i].s - marks[j].s) < SAME_PLACE) continue
                        if (marks[i].crosses(marks[j])) return "${marks[i].s} × ${marks[j].s}"
                    }
                }
                slid += SLIDE_STEP
            }
        }
        return null
    }

    /** A mark from [from] to [to] dp in along the normal at the point in [pt], [s] along the path from its corner. */
    private class Mark(
        val s: Float,
        pt: FloatArray,
        from: Float,
        to: Float,
    ) {
        val x1 = (pt[0] + pt[2] * from).toDouble()
        val y1 = (pt[1] + pt[3] * from).toDouble()
        val x2 = (pt[0] + pt[2] * to).toDouble()
        val y2 = (pt[1] + pt[3] * to).toDouble()

        /** Whether the two segments cross properly, each one's ends on opposite sides of the other. */
        fun crosses(o: Mark): Boolean =
            side(o.x1, o.y1, o.x2, o.y2, x1, y1) * side(o.x1, o.y1, o.x2, o.y2, x2, y2) < 0 &&
                side(x1, y1, x2, y2, o.x1, o.y1) * side(x1, y1, x2, y2, o.x2, o.y2) < 0

        private fun side(
            ax: Double,
            ay: Double,
            bx: Double,
            by: Double,
            px: Double,
            py: Double,
        ): Double = (bx - ax) * (py - ay) - (by - ay) * (px - ax)
    }

    private companion object {
        const val WIDTH = 400f
        const val HEIGHT = 800f

        /** Settings bounds the dial height to 0.4–1.8. */
        const val MIN_TENTHS = 4
        const val MAX_TENTHS = 18
        const val TENTHS = 10f

        /** Display roundings seen on phones, in dp; 24 is the surface's floor. */
        const val MIN_DISPLAY = 24
        const val MAX_DISPLAY = 44
        const val DISPLAY_STEP = 2

        /** Enough notches either side to run well past the widest bend onto both straight edges. */
        const val NOTCHES = 20
        const val SLIDE_STEP = 1f
        const val SAME_PLACE = 0.5f
    }
}
