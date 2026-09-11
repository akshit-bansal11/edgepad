package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** At 0.25 units per dp: 4 dp of slide per unit, 40 dp per stepper step. */
class DialTest {
    private val out = mutableListOf<Frame>()
    private var haptics = 0

    private fun dial(
        kind: DialKind,
        snap: Boolean = false,
    ) = kind.dial(Placement(0f), "X", UNITS_PER_DP, snap, { out.add(it) }, { haptics++ })

    /** Presses, slides past the slop so the dial arms, then slides [dp] more. */
    private fun Dial.slideBy(dp: Float) {
        slide(SLOP + 1f, SLOP)
        slide(dp, SLOP)
    }

    @Test
    fun aTapRunsTheDialAction() {
        val volume = dial(DialKind.VOLUME)
        volume.down()
        assertTrue(volume.up())
        assertEquals(listOf(ActionId.MUTE_TOGGLE.frame()), out)
    }

    @Test
    fun movementInsideTheSlopIsStillATap() {
        val volume = dial(DialKind.VOLUME)
        volume.down()
        volume.slide(3f, SLOP)
        volume.slide(-2f, SLOP)
        assertTrue(volume.up())
    }

    @Test
    fun slidingClockwiseRaisesTheLevelAndSendsIt() {
        val volume = dial(DialKind.VOLUME)
        volume.fromLaptop(50, flag = false)
        volume.down()
        volume.slideBy(40f)
        assertFalse(volume.up())
        assertEquals(60, volume.value)
        assertEquals(ControlId.VOLUME.set(60), out.last())
    }

    @Test
    fun slidingAnticlockwiseLowersItAndStopsAtZero() {
        val volume = dial(DialKind.VOLUME)
        volume.fromLaptop(10, flag = false)
        volume.down()
        volume.slideBy(-400f)
        volume.up()
        assertEquals(0, volume.value)
    }

    @Test
    fun aLevelIsUnknownUntilTheLaptopReportsIt() {
        val brightness = dial(DialKind.BRIGHTNESS)
        assertFalse(brightness.known)
        brightness.fromLaptop(100, flag = false)
        assertTrue(brightness.known)
        assertEquals(100, brightness.value)
    }

    @Test
    fun oneHapticTickPerNotchThatPasses() {
        val volume = dial(DialKind.VOLUME)
        volume.fromLaptop(0, flag = false)
        volume.down()
        volume.slide(SLOP + 1f, SLOP)
        assertEquals("arming ticks once", 1, haptics)
        repeat(44) { volume.slide(1f, SLOP) }
        assertEquals("four notches of 11 dp", 5, haptics)
    }

    @Test
    fun aLateStateFromTheLaptopDoesNotMoveASlidingDial() {
        val volume = dial(DialKind.VOLUME)
        volume.fromLaptop(20, flag = false)
        volume.down()
        volume.slide(SLOP + 1f, SLOP)
        volume.fromLaptop(5, flag = true)
        assertEquals(20, volume.value)
        assertTrue(volume.flag)
        volume.up()
        volume.fromLaptop(5, flag = false)
        assertEquals(5, volume.value)
    }

    @Test
    fun snappingRoundsToFiveWhenTheFingerLifts() {
        val volume = dial(DialKind.VOLUME, snap = true)
        volume.fromLaptop(50, flag = false)
        volume.down()
        volume.slideBy(11f)
        assertEquals(53, volume.value)
        volume.up()
        assertEquals(55, volume.value)
        assertEquals(ControlId.VOLUME.set(55), out.last())
    }

    @Test
    fun theAppSwitcherHoldsAltForTheWholeSlide() {
        val apps = dial(DialKind.APP_SWITCHER)
        apps.down()
        apps.slide(SLOP + 1f, SLOP)
        apps.slide(80f, SLOP)
        apps.slide(-40f, SLOP)
        apps.up()
        assertEquals(
            listOf(
                ActionId.APP_SWITCH_BEGIN.frame(),
                ActionId.APP_SWITCH_NEXT.frame(),
                ActionId.APP_SWITCH_NEXT.frame(),
                ActionId.APP_SWITCH_PREVIOUS.frame(),
                ActionId.APP_SWITCH_END.frame(),
            ),
            out,
        )
    }

    @Test
    fun zoomCountsNotchesUntilATapResetsIt() {
        val zoom = dial(DialKind.ZOOM)
        zoom.down()
        zoom.slideBy(120f)
        zoom.up()
        assertEquals(List(3) { Frame.Zoom(TrackpadRecognizer.WHEEL_NOTCH) }, out)
        assertEquals(3, zoom.steps)
        zoom.down()
        zoom.slideBy(-40f)
        zoom.up()
        assertEquals("zoom keeps counting across slides", 2, zoom.steps)
        zoom.down()
        assertTrue(zoom.up())
        assertEquals(ActionId.ZOOM_RESET.frame(), out.last())
        assertEquals(0, zoom.steps)
    }

    private companion object {
        const val UNITS_PER_DP = 0.25f
        const val SLOP = 8f
    }
}
