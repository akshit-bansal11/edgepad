package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class GestureTest {
    @Test
    fun everyFingerCountHasATapAndFourSwipes() {
        for (fingers in 3..4) {
            for (kind in listOf(
                Gesture.Kind.TAP,
                Gesture.Kind.LEFT,
                Gesture.Kind.RIGHT,
                Gesture.Kind.UP,
                Gesture.Kind.DOWN,
            )) {
                assertEquals("$fingers $kind", fingers, Gesture.of(fingers, kind)?.fingers)
            }
        }
        assertNull(Gesture.of(1, Gesture.Kind.TAP))
        assertNull(Gesture.of(3, Gesture.Kind.PINCH))
    }

    @Test
    fun theDefaultsAreWindowsWithThreeAndFourFingersSwapped() {
        assertEquals(GestureAction.DESKTOP_LEFT, Gesture.THREE_LEFT.default)
        assertEquals(GestureAction.APP_SWITCHER, Gesture.FOUR_RIGHT.default)
        assertEquals(GestureAction.RIGHT_CLICK, Gesture.TWO_TAP.default)
        assertEquals(GestureAction.ZOOM, Gesture.TWO_PINCH.default)
    }

    @Test
    fun oneShotActionsNameALaptopActionAndContinuousOnesDoNot() {
        for (action in GestureAction.entries) {
            val clicks =
                action in listOf(GestureAction.LEFT_CLICK, GestureAction.RIGHT_CLICK, GestureAction.MIDDLE_CLICK)
            val expectsId = !action.continuous && !clicks && action != GestureAction.NOTHING
            assertEquals(action.name, expectsId, action.actionId != null)
        }
        assertEquals(ActionId.TASK_VIEW, GestureAction.TASK_VIEW.actionId)
        assertTrue(GestureAction.VOLUME.continuous)
    }
}
