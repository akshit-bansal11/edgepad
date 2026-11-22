package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.Space
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

private const val UPRIGHT = ControlSurface.TOP_BUTTON_OFFSET_PORTRAIT_DP
private const val SIDEWAYS = ControlSurface.TOP_BUTTON_OFFSET_LANDSCAPE_DP

class BatteryStripTest {
    /** The buttons' half of the keep-out, before the margin, as [ControlSurface.onSizeChanged] works it out. */
    private fun rowHalf(step: Float) = 1.5f * step + Space.TOUCH / 2

    /** The row reaches 1.5 steps either side of the centre. */
    private fun room(step: Float) = BatteryStrip.roomDp(1.5f * step)

    @Test
    fun theStripFitsTheButtonsKeepOutUpright() {
        assertTrue(room(UPRIGHT) / 2 <= rowHalf(UPRIGHT))
    }

    @Test
    fun theStripFitsTheButtonsKeepOutSideways() {
        assertTrue(room(SIDEWAYS) / 2 <= rowHalf(SIDEWAYS))
    }

    @Test
    fun theUprightRoomIsTheRowsWidth() {
        assertEquals(BatteryStrip.MIN_ROOM_DP, room(UPRIGHT), 0f)
    }

    @Test
    fun theShapesStayAboveTheButtonsAndTheTapRectMeetsTheTouchTarget() {
        val shapesBottom = BatteryStrip.CENTRE_DP + BatteryStrip.HEIGHT_DP / 2
        assertTrue(shapesBottom <= ControlSurface.TOP_DP - Space.TOUCH / 2)
        assertTrue(BatteryStrip.CENTRE_DP - BatteryStrip.HEIGHT_DP / 2 >= BatteryStrip.HIT_TOP_DP)
        assertTrue(BatteryStrip.HIT_BOTTOM_DP - BatteryStrip.HIT_TOP_DP >= Space.TOUCH)
    }

    @Test
    fun theShapesFitInsideTheStripsHeight() {
        assertTrue(BatteryShape.ICON_DP <= BatteryStrip.HEIGHT_DP)
        assertTrue(BatteryShape.BODY_H_DP <= BatteryStrip.HEIGHT_DP)
    }

    @Test
    fun theWidestCaseShrinksTheTextThenDropsTheIconsInsteadOfOverflowing() {
        // Two charging devices at 100% (36dp of text each) and a +99 (24dp), in the upright room.
        val room = BatteryStrip.MIN_ROOM_DP
        val spacing = 2 * BatteryShape.SPACING_DP
        val withIcons = 2 * BatteryShape.fixedWidth(device = true, icon = true, charging = true) + spacing
        val without = 2 * BatteryShape.fixedWidth(device = true, icon = false, charging = true) + spacing
        val text = 2 * 36f + 24f
        val floor = 9f / 13f
        assertTrue(StripFit.dropsIcons(BatteryShape.neededScale(withIcons, text, room), floor))
        // Still too wide at the floor: the labels are cut to their share of what is left, never past the room.
        val squeeze = BatteryShape.neededScale(without, text, room)
        assertTrue(without + text * squeeze <= room)
    }
}
