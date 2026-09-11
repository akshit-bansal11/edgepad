package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.surface.TrackpadRecognizer.Action
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** The Windows gesture table, at density 1 so dp and px are the same. */
class TrackpadRecognizerTest {
    private val out = mutableListOf<Frame>()
    private val pad = TrackpadRecognizer(density = 1f) { out.add(it) }

    private fun touch(
        action: Action,
        time: Long,
        vararg points: Pair<Float, Float>,
    ) {
        pad.handle(action, points.map { it.first }.toFloatArray(), points.map { it.second }.toFloatArray(), time)
    }

    @Test
    fun aQuickTapIsALeftClick() {
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.UP, 100)
        assertEquals(listOf(Frame.PointerButton(0, true), Frame.PointerButton(0, false)), out)
    }

    @Test
    fun oneFingerMovesThePointerOnceBeyondTheSlop() {
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.MOVE, 10, 104f to 100f)
        assertTrue("inside the slop nothing moves", out.isEmpty())
        touch(Action.MOVE, 20, 150f to 100f)
        touch(Action.UP, 400)
        assertEquals(1, out.size)
        val move = out.single() as Frame.Move
        assertTrue("moved right by ${move.dx}", move.dx > 0)
        assertEquals(0, move.dy)
    }

    @Test
    fun tapThenHoldAndMoveDrags() {
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.UP, 80)
        touch(Action.DOWN, 200, 100f to 100f)
        touch(Action.MOVE, 300, 160f to 100f)
        touch(Action.UP, 900)
        assertEquals(Frame.PointerButton(0, true), out[2])
        assertTrue(out[3] is Frame.Move)
        assertEquals(Frame.PointerButton(0, false), out.last())
    }

    @Test
    fun twoFingersDraggingDownScrollsForward() {
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.DOWN, 1, 100f to 100f, 160f to 100f)
        touch(Action.MOVE, 20, 100f to 140f, 160f to 140f)
        touch(Action.UP, 30, 160f to 140f)
        touch(Action.UP, 40)
        val scroll = out.filterIsInstance<Frame.Scroll>().single()
        assertTrue("wheel forward for fingers moving down, got ${scroll.dy}", scroll.dy > 0)
        assertEquals(0, scroll.dx)
    }

    @Test
    fun twoFingersSpreadingZoomsIn() {
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.DOWN, 1, 100f to 100f, 140f to 100f)
        touch(Action.MOVE, 20, 60f to 100f, 180f to 100f)
        touch(Action.UP, 30, 180f to 100f)
        touch(Action.UP, 40)
        assertEquals(listOf(Frame.Zoom(120)), out)
    }

    @Test
    fun aTwoFingerTapIsARightClick() {
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.DOWN, 1, 100f to 100f, 140f to 100f)
        touch(Action.UP, 60, 140f to 100f)
        touch(Action.UP, 70)
        assertEquals(listOf(Frame.PointerButton(1, true), Frame.PointerButton(1, false)), out)
    }

    @Test
    fun threeFingersUpIsTaskViewOnce() {
        touch(Action.DOWN, 0, 100f to 300f)
        touch(Action.DOWN, 1, 100f to 300f, 150f to 300f)
        touch(Action.DOWN, 2, 100f to 300f, 150f to 300f, 200f to 300f)
        touch(Action.MOVE, 20, 100f to 200f, 150f to 200f, 200f to 200f)
        touch(Action.MOVE, 30, 100f to 100f, 150f to 100f, 200f to 100f)
        touch(Action.UP, 40, 150f to 100f, 200f to 100f)
        touch(Action.UP, 41, 200f to 100f)
        touch(Action.UP, 42)
        assertEquals(listOf(ActionId.TASK_VIEW.frame()), out)
    }

    @Test
    fun fourFingersSidewaysWalksTheAppSwitcherAndReleasesAlt() {
        touch(Action.DOWN, 0, 100f to 300f, 150f to 300f, 200f to 300f, 250f to 300f)
        touch(Action.MOVE, 20, 160f to 300f, 210f to 300f, 260f to 300f, 310f to 300f)
        touch(Action.MOVE, 30, 260f to 300f, 310f to 300f, 360f to 300f, 410f to 300f)
        touch(Action.UP, 40)
        assertEquals(
            listOf(
                ActionId.APP_SWITCH_BEGIN.frame(),
                ActionId.APP_SWITCH_NEXT.frame(),
                ActionId.APP_SWITCH_END.frame(),
            ),
            out,
        )
    }

    @Test
    fun threeFingersSidewaysSwitchesDesktop() {
        touch(Action.DOWN, 0, 100f to 300f, 150f to 300f, 200f to 300f)
        touch(Action.MOVE, 20, 40f to 300f, 90f to 300f, 140f to 300f)
        touch(Action.UP, 40)
        assertEquals(listOf(ActionId.DESKTOP_LEFT.frame()), out)
    }

    @Test
    fun aFourFingerTapOpensNotifications() {
        touch(Action.DOWN, 0, 100f to 300f, 150f to 300f, 200f to 300f, 250f to 300f)
        touch(Action.UP, 100)
        assertEquals(listOf(ActionId.NOTIFICATIONS.frame()), out)
    }

    @Test
    fun aCancelledDragStillReleasesTheButton() {
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.UP, 80)
        touch(Action.DOWN, 200, 100f to 100f)
        touch(Action.CANCEL, 300)
        assertEquals(Frame.PointerButton(0, false), out.last())
    }

    @Test
    fun withNaturalScrollingOffFingersDownScrollBack() {
        val reversed = TrackpadRecognizer(density = 1f, naturalScroll = false) { out.add(it) }
        reversed.handle(Action.DOWN, floatArrayOf(100f), floatArrayOf(100f), 0)
        reversed.handle(Action.DOWN, floatArrayOf(100f, 160f), floatArrayOf(100f, 100f), 1)
        reversed.handle(Action.MOVE, floatArrayOf(100f, 160f), floatArrayOf(140f, 140f), 20)
        val scroll = out.filterIsInstance<Frame.Scroll>().single()
        assertTrue("wheel back for fingers moving down, got ${scroll.dy}", scroll.dy < 0)
    }

    @Test
    fun anAssignedContinuousActionStepsWithTheSwipe() {
        val volume = TrackpadRecognizer(density = 1f, map = { GestureAction.VOLUME }) { out.add(it) }
        volume.handle(Action.DOWN, floatArrayOf(100f, 150f, 200f), floatArrayOf(300f, 300f, 300f), 0)
        volume.handle(Action.MOVE, floatArrayOf(100f, 150f, 200f), floatArrayOf(240f, 240f, 240f), 20)
        volume.handle(Action.MOVE, floatArrayOf(100f, 150f, 200f), floatArrayOf(160f, 160f, 160f), 30)
        volume.handle(Action.MOVE, floatArrayOf(100f, 150f, 200f), floatArrayOf(240f, 240f, 240f), 40)
        volume.handle(Action.UP, FloatArray(0), FloatArray(0), 50)
        assertEquals(
            listOf(ActionId.VOLUME_UP.frame(), ActionId.VOLUME_UP.frame(), ActionId.VOLUME_DOWN.frame()),
            out,
        )
    }

    @Test
    fun anAssignedTapActionFiresOnce() {
        val pad = TrackpadRecognizer(density = 1f, map = { GestureAction.PLAY_PAUSE }) { out.add(it) }
        pad.handle(Action.DOWN, floatArrayOf(100f, 150f, 200f), floatArrayOf(300f, 300f, 300f), 0)
        pad.handle(Action.UP, FloatArray(0), FloatArray(0), 50)
        assertEquals(listOf(ActionId.PLAY_PAUSE.frame()), out)
    }
}
