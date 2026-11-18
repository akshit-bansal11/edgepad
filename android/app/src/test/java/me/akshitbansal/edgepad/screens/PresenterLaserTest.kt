package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.protocol.ActionId
import org.junit.Assert.assertEquals
import org.junit.Test

class PresenterLaserTest {
    @Test
    fun theTrailSettingPicksTheTool() {
        assertEquals(ActionId.TOOL_LASER, laserTool(false))
        assertEquals(ActionId.TOOL_LASER_TRAIL, laserTool(true))
    }

    @Test
    fun theDefaultSizeIsTheOriginalDot() {
        assertEquals(0.03f * 1000f, laserDotRadius(1000f, 20), 0.001f)
    }

    @Test
    fun theDotGrowsWithTheSize() {
        assertEquals(laserDotRadius(800f, 20) * 5f, laserDotRadius(800f, 100), 0.001f)
    }
}
