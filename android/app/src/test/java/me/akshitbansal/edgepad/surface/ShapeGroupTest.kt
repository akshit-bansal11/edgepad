package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Test

class ShapeGroupTest {
    private fun group(action: GestureAction) = ShapeGroup.of(ShapeTarget.Run(action))

    @Test
    fun clicksAreMouse() {
        assertEquals(ShapeGroup.MOUSE, group(GestureAction.LEFT_CLICK))
        assertEquals(ShapeGroup.MOUSE, group(GestureAction.RIGHT_CLICK))
        assertEquals(ShapeGroup.MOUSE, group(GestureAction.MIDDLE_CLICK))
    }

    @Test
    fun transportKeysAreMedia() {
        listOf(
            GestureAction.PLAY_PAUSE,
            GestureAction.NEXT_TRACK,
            GestureAction.PREVIOUS_TRACK,
            GestureAction.MUTE,
            GestureAction.MIC_MUTE,
        ).forEach { assertEquals(ShapeGroup.MEDIA, group(it)) }
    }

    @Test
    fun theRestOfTheLaptopActionsAreSystem() {
        listOf(
            GestureAction.DESKTOP_LEFT,
            GestureAction.DESKTOP_RIGHT,
            GestureAction.TASK_VIEW,
            GestureAction.SHOW_DESKTOP,
            GestureAction.SEARCH,
            GestureAction.NOTIFICATIONS,
            GestureAction.LOCK,
            GestureAction.ZOOM_RESET,
        ).forEach { assertEquals(ShapeGroup.SYSTEM, group(it)) }
    }

    @Test
    fun padModesAreScreensAndMacrosAreMacros() {
        assertEquals(ShapeGroup.SCREENS, ShapeGroup.of(ShapeTarget.Pad(PadMode.FOCUS)))
        assertEquals(ShapeGroup.SCREENS, ShapeGroup.of(ShapeTarget.Pad(PadMode.PAD_LOCKED)))
        assertEquals(ShapeGroup.MACROS, ShapeGroup.of(ShapeTarget.Macro(0)))
    }
}
