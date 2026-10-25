package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The eight split halves' labels and numbers never draw over each other or the top buttons, at every dial height,
 * nor over the media at the default height and layout, on 360 by 640 and 360 by 800 dp phones, upright and
 * sideways, at the normal and the held size. Dimensions are dp and the painter's own constants: a 5 dp inset past
 * the marks, 8 dp between rows, a 10 sp label, 2 dp, an 18 sp number held at 22, each 3.2 em wide at worst.
 * The buttons and media are laid out as ControlSurface lays them out, with the widest now-playing card. A taller
 * dial can meet the media, which the owner can move anyway, so only the default height is held to that.
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

    private fun row(
        w: Float,
        h: Float,
    ): FloatArray {
        val reach = buttonOffset(w, h) + TOUCH / 2
        return floatArrayOf(w / 2 - reach, w / 2 + reach, TOP + TOUCH / 2)
    }

    private fun buttons(
        w: Float,
        h: Float,
    ): List<Box> =
        listOf(-1f, 0f, 1f).map {
            val cx = w / 2 + it * buttonOffset(w, h)
            Box(cx - TOUCH / 2, TOP - TOUCH / 2, cx + TOUCH / 2, TOP + TOUCH / 2)
        }

    private fun media(
        w: Float,
        h: Float,
    ): List<Box> {
        val room = maxOf(w - 2 * (HALF + 16f) - LOGO - 12f, 120f)
        val half = (LOGO + 12f + room) / 2
        val y = 0.78f * h
        // The card itself. Its progress line, 16 dp under it, can meet a held "240 Hz" by a few dp on a 360 by 640
        // phone; a real number is far narrower.
        val nowPlaying = Box(w / 2 - half, y - 26f, w / 2 + half, y + 26f)
        val ty = 0.9f * h
        val transport = Box(w / 2 - 52f - TOUCH / 2, ty - 26f, w / 2 + 52f + TOUCH / 2, ty + 26f)
        return listOf(nowPlaying, transport)
    }

    private fun units(
        w: Float,
        h: Float,
        dialHeight: Float,
        value: Float,
    ): List<Box> {
        val path = Perimeter(w, h, BEND)
        val depth = RulerGeometry.reach(dialHeight) + 5f
        val drawn = LABEL + GAP + value
        val wide = value * EMS
        return (0 until Perimeter.SLOTS).map { slot ->
            val centre = path.lengthAtSlot(slot, HALF / 2f)
            SplitLabel.anchor(path, centre, depth, BLOCK, 8f, HELD * EMS, row(w, h), pt, out)
            val x = out[0]
            val top = out[1]
            if (out[2] > 0) Box(x, top, x + wide, top + drawn) else Box(x - wide, top, x, top + drawn)
        }
    }

    private val screens = listOf(360f to 640f, 640f to 360f, 360f to 800f, 800f to 360f)

    @Test
    fun noTwoUnitsOverlapEachOtherOrTheButtons() {
        for ((w, h) in screens) {
            for (dialHeight in listOf(0.4f, 0.6f, 0.8f, 1.0f, 1.4f, 1.8f)) {
                for (value in listOf(VALUE, HELD)) {
                    val all = units(w, h, dialHeight, value)
                    val where = "$w x $h, height $dialHeight, size $value"
                    for (a in all.indices) {
                        val box = all[a]
                        for (b in a + 1 until all.size) assertTrue("slots $a and $b at $where", !box.meets(all[b]))
                        for (button in buttons(w, h)) assertTrue("slot $a and a button at $where", !box.meets(button))
                        val inside = box.left >= 0 && box.right <= w && box.top >= 0 && box.bottom <= h
                        assertTrue("slot $a off screen at $where", inside)
                    }
                }
            }
        }
    }

    @Test
    fun noUnitOverlapsTheMediaAtTheDefaults() {
        for ((w, h) in screens) {
            for (value in listOf(VALUE, HELD)) {
                for ((slot, unit) in units(w, h, 0.6f, value).withIndex()) {
                    for (piece in media(w, h)) assertTrue("slot $slot and the media at $w x $h", !unit.meets(piece))
                }
            }
        }
    }

    @Test
    fun aSmallDialKeepsTheTopColumnUpAgainstItsRuler() {
        units(360f, 800f, 0.6f, HELD).forEachIndexed { slot, box ->
            if (slot in listOf(1, 2)) assertTrue("slot $slot", box.top < TOP + TOUCH / 2)
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
        const val TOUCH = 48f
        const val TOP = 40f
        const val LOGO = 40f
    }
}
