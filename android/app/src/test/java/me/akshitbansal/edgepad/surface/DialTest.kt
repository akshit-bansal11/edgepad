package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** At 0.25 units per dp: 4 dp of slide per unit, 40 dp per stepper step. */
class DialTest {
    private val out = mutableListOf<Frame>()
    private var haptics = 0

    private fun dial(
        kind: DialKind,
        snap: Boolean = false,
        devices: DeviceSwitch = DeviceSwitch.None,
    ) = kind.dial(0, "X", UNITS_PER_DP, snap, { out.add(it) }, { haptics++ }, devices = devices)

    /** Records what the Devices dial asks of the laptops, and answers nothing. */
    private class Devices : DeviceSwitch {
        val steps = mutableListOf<Int>()
        var opened = 0
        var onStep: () -> Unit = {}
        override val names = listOf("A", "B", "C")
        override val activeIndex = 0

        override fun step(direction: Int) {
            steps.add(direction)
            onStep()
        }

        override fun open() {
            opened++
        }
    }

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
    fun scrollSendsOneWheelNotchPerStepAndClockwiseGoesDown() {
        val scroll = dial(DialKind.SCROLL)
        scroll.down()
        scroll.slideBy(80f)
        scroll.slide(-40f, SLOP)
        scroll.up()
        val notch = TrackpadRecognizer.WHEEL_NOTCH
        assertEquals(listOf(Frame.Scroll(0, -notch), Frame.Scroll(0, -notch), Frame.Scroll(0, notch)), out)
    }

    @Test
    fun horizontalScrollSendsOneSidewaysNotchPerStepAndClockwiseGoesRight() {
        val side = dial(DialKind.SCROLL_H)
        side.down()
        side.slideBy(80f)
        side.slide(-40f, SLOP)
        side.up()
        val notch = TrackpadRecognizer.WHEEL_NOTCH
        assertEquals(listOf(Frame.Scroll(notch, 0), Frame.Scroll(notch, 0), Frame.Scroll(-notch, 0)), out)
    }

    @Test
    fun desktopsMoveByStepAndATapOpensTaskView() {
        val desktops = dial(DialKind.DESKTOPS)
        desktops.down()
        desktops.slideBy(80f)
        desktops.slide(-40f, SLOP)
        desktops.up()
        assertEquals(
            listOf(ActionId.DESKTOP_RIGHT.frame(), ActionId.DESKTOP_RIGHT.frame(), ActionId.DESKTOP_LEFT.frame()),
            out,
        )
        out.clear()
        desktops.down()
        assertTrue(desktops.up())
        assertEquals(listOf(ActionId.TASK_VIEW.frame()), out)
    }

    @Test
    fun tabsHoldCtrlForTheWholeSlideAndShiftOnlyGoingBack() {
        val tabs = dial(DialKind.TABS)
        tabs.down()
        tabs.slideBy(40f)
        tabs.slide(-40f, SLOP)
        tabs.up()
        assertEquals(
            listOf(
                Frame.Key(CTRL, true),
                Frame.Key(TAB, true),
                Frame.Key(TAB, false),
                Frame.Key(SHIFT, true),
                Frame.Key(TAB, true),
                Frame.Key(TAB, false),
                Frame.Key(SHIFT, false),
                Frame.Key(CTRL, false),
            ),
            out,
        )
    }

    @Test
    fun aCancelledTabSlideStillLetsGoOfCtrl() {
        val tabs = dial(DialKind.TABS)
        tabs.down()
        tabs.slideBy(40f)
        tabs.cancel()
        assertEquals(Frame.Key(CTRL, false), out.last())
    }

    @Test
    fun aTabsTapSendsNothing() {
        val tabs = dial(DialKind.TABS)
        tabs.down()
        assertTrue(tabs.up())
        assertTrue(out.isEmpty())
    }

    @Test
    fun theNewDialsTakeNoCornerByDefault() {
        val taken = DialKind.entries.mapNotNull { it.defaultCorner }
        assertEquals(listOf(0, 1, 2, 3), taken)
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

    @Test
    fun theRefreshDialCountsRateIndicesAndStopsAtTheLastOne() {
        val refresh = dial(DialKind.REFRESH)
        // Three rates, so the top of the range is 2, nothing like the 0-100 every other level dial has.
        refresh.maxLevel = 2f
        refresh.fromLaptop(0, flag = false)
        refresh.down()
        refresh.slideBy(40f)
        assertEquals("one stepper notch of slide is one rate", 1, refresh.value)
        assertEquals(ControlId.REFRESH_RATE.set(1), out.last())
        refresh.slide(400f, SLOP)
        refresh.up()
        assertEquals(2, refresh.value)
        assertEquals(ControlId.REFRESH_RATE.set(2), out.last())
    }

    @Test
    fun aShrunkRangeBringsTheLevelDownWithIt() {
        // The laptop can name a shorter list than the one the dial was last shown at: docking, or a
        // second display taking over. A level left above the top would draw off the end of the ruler.
        val refresh = dial(DialKind.REFRESH)
        refresh.maxLevel = 4f
        refresh.fromLaptop(4, flag = false)
        refresh.maxLevel = 1f
        assertEquals(1, refresh.value)
    }

    @Test
    fun slidingTheDevicesDialStepsOncePerNotchAndNeverSendsAFrame() {
        val devices = Devices()
        val dial = dial(DialKind.DEVICES, devices = devices)
        dial.down()
        dial.slideBy(120f)
        dial.slide(-80f, SLOP)
        dial.up()
        assertEquals(listOf(1, 1, 1, -1, -1), devices.steps)
        assertEquals(0, devices.opened)
        assertTrue(out.isEmpty())
    }

    @Test
    fun aDevicesTapOpensTheListAndSendsNothing() {
        val devices = Devices()
        val dial = dial(DialKind.DEVICES, devices = devices)
        dial.down()
        assertTrue(dial.up())
        assertEquals(1, devices.opened)
        assertTrue(devices.steps.isEmpty())
        assertTrue(out.isEmpty())
    }

    @Test
    fun aSlideThatEndsTheDevicesSlideStopsStepping() {
        // One move can cross several notches. The first step swaps the laptop and cancels the touch, so the others
        // would otherwise carry on from the new laptop to the one after it.
        val devices = Devices()
        val dial = dial(DialKind.DEVICES, devices = devices)
        devices.onStep = { dial.cancel() }
        dial.down()
        dial.slideBy(120f)
        assertEquals(listOf(1), devices.steps)
        assertFalse(dial.armed)
        assertTrue(out.isEmpty())
    }

    @Test
    fun theDevicesDialIsLastAndTakesNoCorner() {
        assertEquals(DialKind.DEVICES, DialKind.entries.last())
        assertNull(DialKind.DEVICES.defaultCorner)
        assertEquals(listOf(0, 1, 2, 3), DialKind.entries.mapNotNull { it.defaultCorner })
    }

    @Test
    fun everyOlderKindKeepsItsStoredNameAndPlace() {
        // Corners and sensitivities are stored by kind name, and the name list is also the ordinal order.
        assertEquals(
            listOf(
                "VOLUME",
                "BRIGHTNESS",
                "ZOOM",
                "MEDIA",
                "APP_SWITCHER",
                "REFRESH",
                "SCROLL",
                "SCROLL_H",
                "DESKTOPS",
                "TABS",
                "DEVICES",
            ),
            DialKind.entries.map { it.name },
        )
        assertEquals(
            listOf(DialKind.VOLUME, DialKind.BRIGHTNESS, DialKind.ZOOM, DialKind.MEDIA),
            DialKind.entries.filter { it.defaultCorner != null }.sortedBy { it.defaultCorner },
        )
    }

    private companion object {
        const val UNITS_PER_DP = 0.25f
        const val SLOP = 8f
        const val TAB = 0x09
        const val SHIFT = 0x10
        const val CTRL = 0x11
    }
}
