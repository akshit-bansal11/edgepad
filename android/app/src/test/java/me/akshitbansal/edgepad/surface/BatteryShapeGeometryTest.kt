package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class BatteryShapeGeometryTest {
    @Test
    fun theFillIsTheInnerWidthTimesTheLevel() {
        assertEquals(0f, BatteryShape.fillWidth(0), 0f)
        assertEquals(16f * 0.52f, BatteryShape.fillWidth(52), 0.001f)
        assertEquals(16f, BatteryShape.fillWidth(100), 0f)
        assertEquals(16f, BatteryShape.fillWidth(130), 0f)
    }

    @Test
    fun theFillHeightIsTheInnerHeight() {
        assertEquals(5f, BatteryShape.fillHeight(), 0f)
    }

    @Test
    fun lowBeatsChargingAndChargingBeatsPlain() {
        assertEquals(1, BatteryShape.fillColor(low = true, charging = false, danger = 1, accent = 2, ink = 3))
        assertEquals(2, BatteryShape.fillColor(low = false, charging = true, danger = 1, accent = 2, ink = 3))
        assertEquals(3, BatteryShape.fillColor(low = false, charging = false, danger = 1, accent = 2, ink = 3))
    }

    @Test
    fun aDeviceChipTakesItsPartsAndThePlusChipTakesNothing() {
        assertEquals(0f, BatteryShape.fixedWidth(device = false, icon = false, charging = false), 0f)
        assertEquals(30f, BatteryShape.fixedWidth(device = true, icon = false, charging = false), 0f)
        assertEquals(48f, BatteryShape.fixedWidth(device = true, icon = true, charging = false), 0f)
        assertEquals(64f, BatteryShape.fixedWidth(device = true, icon = true, charging = true), 0f)
    }

    @Test
    fun chipsAreSpacedTwentyFourApart() {
        assertEquals(0f, BatteryShape.span(floatArrayOf(), 0), 0f)
        assertEquals(100f, BatteryShape.span(floatArrayOf(100f), 1), 0f)
        assertEquals(30f + 40f + 24f, BatteryShape.span(floatArrayOf(30f, 40f, 99f), 2), 0f)
    }

    @Test
    fun theTextShrinksOnlyWhenTheStripWouldBeWiderThanItsRoom() {
        assertEquals(1f, BatteryShape.neededScale(100f, 40f, 240f), 0f)
        assertEquals(0.5f, BatteryShape.neededScale(200f, 80f, 240f), 0.001f)
        assertEquals(1f, BatteryShape.neededScale(100f, 0f, 240f), 0f)
    }

    @Test
    fun twoChargingDevicesAt100FitTheStrip() {
        // "100%" at 13sp bold is about 36dp wide.
        val one = BatteryShape.fixedWidth(device = true, icon = true, charging = true) + 36f
        assertTrue(2 * one + BatteryShape.SPACING_DP <= BatteryStrip.WIDTH_DP)
    }
}
