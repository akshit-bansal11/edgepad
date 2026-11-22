package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.Space
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class BatteryStripTest {
    @Test
    fun theStripsTapRectEndsWhereTheButtonsBegin() {
        assertEquals(ControlSurface.TOP_DP - Space.TOUCH / 2, BatteryStrip.HIT_BOTTOM_DP, 0f)
        val chipBottom = BatteryStrip.CENTRE_DP + BatteryStrip.HEIGHT_DP / 2
        assertTrue(chipBottom <= BatteryStrip.HIT_BOTTOM_DP)
        assertTrue(BatteryStrip.CENTRE_DP - BatteryStrip.HEIGHT_DP / 2 >= BatteryStrip.HIT_TOP_DP)
    }

    @Test
    fun theShapesFitInsideTheStripsHeight() {
        assertTrue(BatteryShape.ICON_DP <= BatteryStrip.HEIGHT_DP)
        assertTrue(BatteryShape.BODY_H_DP <= BatteryStrip.HEIGHT_DP)
    }
}
