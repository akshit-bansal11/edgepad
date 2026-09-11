package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class DialTest {
    private val out = mutableListOf<Frame>()
    private var haptics = 0

    private fun volume(placement: Placement = Placement(Edge.BOTTOM, 1f)) =
        DialKind.VOLUME.dial(placement, "Volume", { out.add(it) }, { haptics++ })

    @Test
    fun aTapWithoutAHoldRunsTheDialAction() {
        val dial = volume()
        dial.down(-40f, -10f)
        assertTrue(dial.up())
        assertEquals(listOf(ActionId.MUTE_TOGGLE.frame()), out)
    }

    @Test
    fun holdingArmsItAndSweepingUpTheRightEdgeRaisesTheValue() {
        val dial = volume()
        dial.fromLaptop(50, muted = false)
        // Bottom-right corner: the finger starts left of the corner and sweeps up, along the right edge.
        dial.down(-60f, -10f)
        dial.hold()
        assertTrue(dial.armed)
        dial.move(-50f, -35f, slop = 8f)
        dial.move(-35f, -50f, slop = 8f)
        dial.move(-10f, -60f, slop = 8f)
        assertFalse(dial.up())
        assertTrue("value rose from 50 to ${dial.value}", dial.value > 50)
        val last = out.last() as Frame.SetValue
        assertEquals(ControlId.VOLUME.id, last.control)
        assertEquals(dial.value, last.value)
        assertTrue("one haptic tick per unit", haptics >= dial.value - 50)
    }

    @Test
    fun upRaisesOnTheSidesAndRightRaisesAlongTheTopAndBottom() {
        // Each case: where the finger starts and ends relative to the dial's centre.
        val cases =
            listOf(
                Triple(Placement(Edge.RIGHT, 0.5f), -40f to 40f, -40f to -40f),
                Triple(Placement(Edge.LEFT, 0.5f), 40f to 40f, 40f to -40f),
                Triple(Placement(Edge.TOP, 0.5f), -40f to 40f, 40f to 40f),
                Triple(Placement(Edge.BOTTOM, 0.5f), -40f to -40f, 40f to -40f),
                Triple(Placement(Edge.TOP, 0f), 10f to 60f, 60f to 10f),
                Triple(Placement(Edge.TOP, 1f), -10f to 60f, -60f to 10f),
                Triple(Placement(Edge.BOTTOM, 0f), 60f to -10f, 10f to -60f),
                Triple(Placement(Edge.BOTTOM, 1f), -60f to -10f, -10f to -60f),
            )
        for ((placement, start, end) in cases) {
            val dial = volume(placement)
            dial.fromLaptop(50, muted = false)
            dial.down(start.first, start.second)
            dial.hold()
            dial.move(end.first, end.second, slop = 8f)
            dial.up()
            assertTrue("$placement rose to ${dial.value}", dial.value > 50)
        }
    }

    @Test
    fun aDragBeyondTheSlopArmsWithoutWaitingForTheHold() {
        val dial = volume()
        dial.down(-60f, -10f)
        dial.move(-50f, -35f, slop = 8f)
        assertTrue(dial.armed)
        assertFalse(dial.up())
    }

    @Test
    fun aLateStateFromTheLaptopDoesNotMoveAnArmedDial() {
        val dial = volume()
        dial.down(-60f, -10f)
        dial.hold()
        dial.fromLaptop(5, muted = true)
        assertEquals(0, dial.value)
        assertTrue(dial.muted)
        dial.up()
        dial.fromLaptop(5, muted = false)
        assertEquals(5, dial.value)
    }

    @Test
    fun theAppSwitcherHoldsAltForTheWholeDragAndStepsOncePerStep() {
        val dial = DialKind.APP_SWITCHER.dial(Placement(Edge.TOP, 1f), "Apps", { out.add(it) }, { haptics++ })
        // Top-right corner: a quarter turn up from below the corner to left of it is 30 units, six steps.
        dial.down(0f, 60f)
        dial.hold()
        assertEquals(listOf(ActionId.APP_SWITCH_BEGIN.frame()), out)
        dial.move(-60f, 0f, slop = 8f)
        assertEquals(List(6) { ActionId.APP_SWITCH_NEXT.frame() }, out.drop(1))
        dial.move(0f, 60f, slop = 8f)
        assertEquals(List(6) { ActionId.APP_SWITCH_PREVIOUS.frame() }, out.drop(7))
        dial.up()
        assertEquals(ActionId.APP_SWITCH_END.frame(), out.last())
    }

    @Test
    fun theZoomDialSendsWheelNotchesAndATapResets() {
        val dial = DialKind.ZOOM.dial(Placement(Edge.RIGHT, 0.5f), "Zoom", { out.add(it) }, { haptics++ })
        dial.down(-40f, 40f)
        dial.hold()
        dial.move(-40f, -40f, slop = 8f)
        assertTrue(out.isNotEmpty())
        assertTrue(out.all { it == Frame.Zoom(TrackpadRecognizer.WHEEL_NOTCH) })
        dial.up()
        out.clear()
        dial.down(-40f, 0f)
        dial.up()
        assertEquals(listOf(ActionId.ZOOM_RESET.frame()), out)
    }
}
