package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The two halves of a split corner never draw their label and number over each other, nor over the other
 * corners' text, at the smallest supported screen (360 by 640 dp), upright and sideways, and at the shortest and
 * tallest dial. Dimensions are dp and the painter's own constants: a 6 dp gap past the marks, 2 dp between the
 * halves, an 11 sp label, a 4 dp gap and the 32 sp held value,
 * with a 96 dp wide worst-case number ("240 Hz").
 */
class SplitLabelTest {
    private class Box(
        val left: Float,
        val top: Float,
        val right: Float,
        val bottom: Float,
    ) {
        fun meets(other: Box): Boolean =
            left < other.right && other.left < right && top < other.bottom && other.top < bottom
    }

    private val pt = FloatArray(4)
    private val out = FloatArray(2)

    private fun boxes(
        width: Float,
        height: Float,
        dialHeight: Float,
        halfLength: Float,
    ): List<Box> {
        val path = Perimeter(width, height, BEND)
        val depth = RulerGeometry.reach(dialHeight) + 6f
        return (0 until Perimeter.SLOTS).map { slot ->
            val centre = path.lengthAtSlot(slot, halfLength / 2f)
            SplitLabel.anchor(path, centre, depth, BLOCK, 2f, pt, out)
            val x = out[0]
            val y = out[1]
            when {
                !SplitLabel.isSideEdge(pt[2], pt[3]) -> Box(x - WIDE / 2, y - BLOCK / 2, x + WIDE / 2, y + BLOCK / 2)
                pt[2] > 0 -> Box(x, y - BLOCK / 2, x + WIDE, y + BLOCK / 2)
                else -> Box(x - WIDE, y - BLOCK / 2, x, y + BLOCK / 2)
            }
        }
    }

    @Test
    fun noTwoLabelsOverlapAtTheSmallestScreen() {
        for ((w, h) in listOf(360f to 640f, 640f to 360f)) {
            for (dialHeight in listOf(0.4f, 0.6f, 1.0f, 1.8f)) {
                for (halfLength in listOf(60f, 100f, 120f)) {
                    val all = boxes(w, h, dialHeight, halfLength)
                    for (a in all.indices) {
                        for (b in a + 1 until all.size) {
                            assertTrue(
                                "slots $a and $b at $w x $h, height $dialHeight, length $halfLength",
                                !all[a].meets(all[b]),
                            )
                        }
                    }
                }
            }
        }
    }

    @Test
    fun aSideEdgeLabelStaysOnScreen() {
        for (box in boxes(360f, 640f, 1.8f, 100f)) {
            assertTrue(box.left >= 0f && box.right <= 360f && box.top >= 0f && box.bottom <= 640f)
        }
    }

    private companion object {
        const val BEND = 24f
        const val BLOCK = 11f + 4f + 32f
        const val WIDE = 96f
    }
}
