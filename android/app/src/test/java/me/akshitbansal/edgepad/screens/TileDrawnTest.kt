package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class TileDrawnTest {
    @Test
    fun theDeveloperTileNeedsDeveloperMode() {
        assertFalse(tileDrawn(ControlsPopup.Target.DEVELOPER, false))
        assertTrue(tileDrawn(ControlsPopup.Target.DEVELOPER, true))
    }

    @Test
    fun everyOtherTileIsDrawnEitherWay() {
        for (target in ControlsPopup.Target.entries.filter { it != ControlsPopup.Target.DEVELOPER }) {
            assertTrue(tileDrawn(target, false))
        }
    }
}
