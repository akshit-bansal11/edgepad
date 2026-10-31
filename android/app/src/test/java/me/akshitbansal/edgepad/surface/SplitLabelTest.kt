package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The eight split halves' labels and numbers never draw over each other or the top buttons, nor leave the screen,
 * at every dial length and height the settings allow, on 360 by 640 and 360 by 800 dp phones, upright and
 * sideways, at the normal and the held size; at the default length and height each sits at its own ruler's far
 * end. Dimensions are dp and the painter's own constants: a 5 dp inset past the marks, an 8 dp gap, a 10 sp label,
 * 2 dp, an 18 sp number held at 22, each 3.2 em wide at worst. The rulers are trimmed as ControlSurface trims
 * them, and the buttons are laid out as it lays them out. The media is not held to: the labels sit where the
 * owner's sketch puts them, and both media pieces can be moved in Media layout.
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
    private val out = FloatArray(3)

    private fun buttonOffset(
        w: Float,
        h: Float,
    ): Float = if (w > h) 56f else 48f

    /** Half the buttons' keep-out, reach plus half a touch target plus a margin, as ControlSurface has it. */
    private fun zone(
        w: Float,
        h: Float,
    ): Float = buttonOffset(w, h) + TOUCH / 2 + M

    private fun buttons(
        w: Float,
        h: Float,
    ): List<Box> =
        listOf(-1f, 0f, 1f).map {
            val cx = w / 2 + it * buttonOffset(w, h)
            Box(cx - TOUCH / 2, TOP - TOUCH / 2, cx + TOUCH / 2, TOP + TOUCH / 2)
        }

    /** One half's centre, far end and anchor, for each of the eight slots. */
    private class Placed(
        val centre: Float,
        val end: Float,
        val x: Float,
        val top: Float,
        val align: Float,
        val box: Box,
    )

    private fun units(
        w: Float,
        h: Float,
        dialHeight: Float,
        value: Float,
        half: Float = HALF,
    ): List<Placed> {
        val path = Perimeter(w, h, BEND)
        val centres = FloatArray(Perimeter.SLOTS) { path.lengthAtSlot(it, half / 2f) }
        val topCentre = path.lengthAt(0.5f)
        val zone = zone(w, h)
        val after = FloatArray(Perimeter.SLOTS)
        val before = FloatArray(Perimeter.SLOTS)
        val keepOut = floatArrayOf(topCentre - zone, topCentre + zone)
        DialSpan.compute(centres, half, path.length, keepOut, 16f, after, before)
        val depth = RulerGeometry.reach(dialHeight) + 5f
        val drawn = LABEL + GAP + value
        val wide = value * EMS
        val clear = floatArrayOf(w / 2 - zone, w / 2 + zone)
        return centres.indices.map { slot ->
            val end = SplitLabel.farEnd(path, centres[slot], after[slot], before[slot])
            SplitLabel.anchor(path, centres[slot], end, depth, BLOCK, HELD * EMS, S, clear, pt, out)
            val x = out[0]
            val top = out[1]
            val box = if (out[2] > 0) Box(x, top, x + wide, top + drawn) else Box(x - wide, top, x, top + drawn)
            Placed(centres[slot], end, x, top, out[2], box)
        }
    }

    private val screens = listOf(360f to 640f, 640f to 360f, 360f to 800f, 800f to 360f)
    private val lengths = listOf(60f, 80f, 100f, 140f, 160f, 200f, 240f, 280f, 320f)
    private val heights = listOf(0.4f, 0.6f, 0.8f, 1.0f, 1.2f, 1.4f, 1.6f, 1.8f)

    @Test
    fun noTwoUnitsOverlapEachOtherOrTheButtonsAtEveryLengthAndHeight() {
        for ((w, h) in screens) {
            for (half in lengths) {
                for (dialHeight in heights) {
                    for (value in listOf(VALUE, HELD)) {
                        val all = units(w, h, dialHeight, value, half).map { it.box }
                        val where = "$w x $h, length $half, height $dialHeight, size $value"
                        for (a in all.indices) {
                            val box = all[a]
                            for (b in a + 1 until all.size) assertTrue("slots $a and $b at $where", !box.meets(all[b]))
                            for (button in buttons(w, h)) {
                                assertTrue("slot $a and a button at $where", !box.meets(button))
                            }
                            val inside = box.left >= 0 && box.right <= w && box.top >= 0 && box.bottom <= h
                            assertTrue("slot $a off screen at $where", inside)
                        }
                    }
                }
            }
        }
    }

    @Test
    fun topEdgeUnitsStayOutOfTheButtonsKeepOut() {
        for ((w, h) in listOf(360f to 640f, 640f to 360f)) {
            val zone = zone(w, h)
            for (half in lengths) {
                for (dialHeight in heights) {
                    val all = units(w, h, dialHeight, HELD, half)
                    for (slot in listOf(1, 2)) {
                        val box = all[slot].box
                        val clear = box.right <= w / 2 - zone || box.left >= w / 2 + zone
                        assertTrue("slot $slot at $w x $h, length $half, height $dialHeight", clear)
                    }
                }
            }
        }
    }

    @Test
    fun theFarEndIsTheEndAwayFromTheBend() {
        for ((w, h) in screens) {
            val path = Perimeter(w, h, BEND)
            units(w, h, 0.6f, HELD).forEachIndexed { slot, unit ->
                assertTrue("slot $slot at $w x $h", path.fromBend(unit.end) > path.fromBend(unit.centre))
            }
        }
    }

    @Test
    fun eachUnitSitsAtItsOwnRulersFarEndAtTheDefaults() {
        for ((w, h) in screens) {
            val path = Perimeter(w, h, BEND)
            val depth = RulerGeometry.reach(0.6f) + 5f
            units(w, h, 0.6f, HELD).forEachIndexed { slot, unit ->
                val where = "slot $slot at $w x $h"
                path.point(unit.end, pt)
                val left = slot in listOf(0, 1, 6, 7)
                val top = slot in listOf(0, 1, 2, 3)
                if (slot in listOf(0, 3, 4, 7)) {
                    // A side edge: depth in from that edge, flush with the end, lying back toward the corner.
                    assertEquals(where, if (left) depth else w - depth, unit.x, EPS)
                    assertEquals(where, if (top) pt[1] - BLOCK else pt[1], unit.top, EPS)
                    assertEquals(where, if (left) 1f else -1f, unit.align)
                } else {
                    // A top or bottom edge: depth in from that edge, aligned on the end toward the corner.
                    assertEquals(where, pt[0], unit.x, EPS)
                    assertEquals(where, if (top) depth else h - depth - BLOCK, unit.top, EPS)
                    assertEquals(where, if (left) -1f else 1f, unit.align)
                }
            }
        }
    }

    @Test
    fun mirroredSlotsMirrorTheirUnits() {
        for ((w, h) in screens) {
            for (half in lengths) {
                val all = units(w, h, 0.6f, HELD, half).map { it.box }
                for (slot in all.indices) {
                    // 0 and 3, 1 and 2, 4 and 7, 5 and 6 face each other across the vertical midline.
                    val mirror = slot / 4 * 4 + 3 - slot % 4
                    val where = "slots $slot and $mirror at $w x $h, length $half"
                    assertEquals(where, w - all[mirror].right, all[slot].left, EPS)
                    assertEquals(where, all[mirror].top, all[slot].top, EPS)
                }
            }
        }
    }

    private companion object {
        const val BEND = 24f
        const val HALF = 100f
        const val LABEL = 10f
        const val GAP = 2f
        const val VALUE = 18f
        const val HELD = 22f
        const val BLOCK = LABEL + GAP + HELD
        const val EMS = 3.2f
        const val S = 8f
        const val M = 12f
        const val TOUCH = 48f
        const val TOP = 40f
        const val EPS = 0.01f
    }
}
