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
    fun thePadDotIsTheLaptopsShareTimesTheFingerScale() {
        assertEquals(1000f * 0.0075f * 2.5f, laserDotRadius(1000f, 15), 0.001f)
    }

    @Test
    fun theDotGrowsWithTheSize() {
        assertEquals(laserDotRadius(800f, 10) * 2f, laserDotRadius(800f, 20), 0.001f)
    }
}
