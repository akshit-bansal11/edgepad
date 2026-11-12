package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.Space
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class BatteryStripTest {
    /** The buttons' half of the keep-out, before the margin, as [ControlSurface.onSizeChanged] works it out. */
    private fun rowHalf(step: Float) = step + Space.TOUCH / 2

    @Test
    fun theStripFitsTheButtonsKeepOutUpright() {
        assertTrue(BatteryStrip.WIDTH_DP / 2 <= rowHalf(ControlSurface.TOP_BUTTON_OFFSET_PORTRAIT_DP))
    }

    @Test
    fun theStripFitsTheButtonsKeepOutSideways() {
        assertTrue(BatteryStrip.WIDTH_DP / 2 <= rowHalf(ControlSurface.TOP_BUTTON_OFFSET_LANDSCAPE_DP))
    }

    @Test
    fun theStripsTapRectEndsWhereTheButtonsBegin() {
        assertEquals(ControlSurface.TOP_DP - Space.TOUCH / 2, BatteryStrip.HIT_BOTTOM_DP, 0f)
        val chipBottom = BatteryStrip.CENTRE_DP + BatteryStrip.HEIGHT_DP / 2
        assertTrue(chipBottom <= BatteryStrip.HIT_BOTTOM_DP)
        assertTrue(BatteryStrip.CENTRE_DP - BatteryStrip.HEIGHT_DP / 2 >= BatteryStrip.HIT_TOP_DP)
    }
}
