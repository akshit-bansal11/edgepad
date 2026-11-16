package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.surface.TrackpadRecognizer.Action
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The Windows gesture table, at density 1 so dp and px are the same. */
class TrackpadRecognizerTest {
    private val out = mutableListOf<Frame>()
    private val pad = TrackpadRecognizer(density = 1f) { out.add(it) }

    /** The last stroke [shaped] handed over, copied out of the recogniser's reused buffers. */
    private var path: Pair<FloatArray, FloatArray>? = null
    private val shaped =
        TrackpadRecognizer(density = 1f, onShape = { xs, ys, n -> path = xs.copyOf(n) to ys.copyOf(n) }) {
            out.add(it)
        }

    private fun touch(
        action: Action,
        time: Long,
        vararg points: Pair<Float, Float>,
    ) = pad.send(action, time, *points)

    private fun TrackpadRecognizer.send(
        action: Action,
        time: Long,
        vararg points: Pair<Float, Float>,
    ) {
        handle(action, points.map { it.first }.toFloatArray(), points.map { it.second }.toFloatArray(), time)
    }

    /** One finger down, a second beside it at 10 ms, both held still until the stroke arms. */
    private fun armAShape() {
        shaped.send(Action.DOWN, 0, 100f to 100f)
        shaped.send(Action.DOWN, 10, 100f to 100f, 160f to 100f)
        shaped.tick(10 + TrackpadRecognizer.HOLD_MS)
        assertTrue(shaped.shaping)
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
    fun holdingStillThenMovingDrags() {
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.MOVE, TrackpadRecognizer.HOLD_MS, 101f to 100f)
        assertEquals("the hold grips the button", listOf(Frame.PointerButton(0, true)), out)
        assertTrue(pad.held)
        touch(Action.MOVE, TrackpadRecognizer.HOLD_MS + 50, 160f to 100f)
        touch(Action.UP, 900)
        assertTrue(out[1] is Frame.Move)
        assertEquals(Frame.PointerButton(0, false), out.last())
    }

    @Test
    fun theTimerGripsAFingerThatSendsNoSamples() {
        touch(Action.DOWN, 0, 100f to 100f)
        pad.tick(TrackpadRecognizer.HOLD_MS - 1)
        assertTrue("not due yet", out.isEmpty())
        pad.tick(TrackpadRecognizer.HOLD_MS)
        assertEquals(listOf(Frame.PointerButton(0, true)), out)
        pad.tick(TrackpadRecognizer.HOLD_MS + 100)
        assertEquals("one grip per hold", 1, out.size)
    }

    @Test
    fun aStaleTimerNeverGripsALaterPress() {
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.UP, 80)
        touch(Action.DOWN, 300, 100f to 100f)
        pad.tick(TrackpadRecognizer.HOLD_MS)
        assertEquals("only the tap's click", 2, out.size)
    }

    @Test
    fun aMovingFingerNeverGrips() {
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.MOVE, 100, 160f to 100f)
        touch(Action.MOVE, TrackpadRecognizer.HOLD_MS + 100, 161f to 100f)
        touch(Action.UP, 900)
        assertTrue("moves only", out.all { it is Frame.Move })
    }

    @Test
    fun tapThenPressIsAClickAndAPlainPressNotADoubleClick() {
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.UP, 80)
        touch(Action.DOWN, 200, 100f to 100f)
        touch(Action.MOVE, 300, 160f to 100f)
        touch(Action.UP, 900)
        assertEquals(listOf(Frame.PointerButton(0, true), Frame.PointerButton(0, false)), out.take(2))
        assertTrue("then only moves", out.drop(2).all { it is Frame.Move })
    }

    @Test
    fun twoFingersHeldStillDrawAShapeAndDoNotScroll() {
        var stroke = 0
        val shaped = TrackpadRecognizer(density = 1f, onShape = { _, _, count -> stroke = count }) { out.add(it) }

        fun two(
            action: Action,
            time: Long,
            dy: Float,
        ) = shaped.handle(action, floatArrayOf(100f, 160f), floatArrayOf(100f + dy, 100f + dy), time)

        shaped.handle(Action.DOWN, floatArrayOf(100f), floatArrayOf(100f), 0)
        two(Action.DOWN, 10, 0f)
        shaped.tick(10 + TrackpadRecognizer.HOLD_MS)
        assertTrue(shaped.shaping)
        two(Action.MOVE, 500, 40f)
        two(Action.MOVE, 520, 80f)
        shaped.handle(Action.UP, floatArrayOf(), floatArrayOf(), 600)
        assertTrue("no scroll, no click: ${out.toList()}", out.isEmpty())
        assertTrue("the stroke went to the shape sink", stroke >= 3)
    }

    @Test
    fun liftOneThenDrawWithTheOther() {
        armAShape()
        shaped.send(Action.UP, 500, 160f to 100f)
        assertTrue(shaped.drawingAlone)
        shaped.send(Action.MOVE, 550, 160f to 140f)
        shaped.send(Action.MOVE, 600, 160f to 180f)
        shaped.send(Action.UP, 700)
        assertTrue("no frames: ${out.toList()}", out.isEmpty())
        val (xs, ys) = path ?: error("no stroke handed over")
        assertTrue("count ${xs.size}", xs.size >= 3)
        assertEquals(160f, xs.first())
        assertEquals(100f, ys.first())
        assertEquals(160f, xs.last())
        assertEquals(180f, ys.last())
    }

    @Test
    fun liftOneBeforeTheHoldIsNotAShape() {
        shaped.send(Action.DOWN, 0, 100f to 100f)
        shaped.send(Action.DOWN, 10, 100f to 100f, 160f to 100f)
        shaped.send(Action.UP, 100, 160f to 100f)
        shaped.send(Action.MOVE, 200, 160f to 300f)
        shaped.send(Action.MOVE, 600, 160f to 400f)
        shaped.send(Action.UP, 700)
        assertTrue("no scroll, move or click: ${out.toList()}", out.isEmpty())
        assertNull(path)
    }

    @Test
    fun twoFingerTapStillRightClicksWithAShapeBound() {
        shaped.send(Action.DOWN, 0, 100f to 100f)
        shaped.send(Action.DOWN, 1, 100f to 100f, 160f to 100f)
        shaped.send(Action.UP, 60, 160f to 100f)
        shaped.send(Action.MOVE, 65, 160f to 100f)
        shaped.send(Action.UP, 70)
        assertEquals(listOf(Frame.PointerButton(1, true), Frame.PointerButton(1, false)), out)
        assertNull(path)
    }

    @Test
    fun twoFingerScrollStillScrollsWithAShapeBound() {
        shaped.send(Action.DOWN, 0, 100f to 100f)
        shaped.send(Action.DOWN, 1, 100f to 100f, 160f to 100f)
        shaped.send(Action.MOVE, 20, 100f to 140f, 160f to 140f)
        assertFalse(shaped.shaping)
        shaped.send(Action.UP, 30, 160f to 140f)
        shaped.send(Action.UP, 40)
        assertTrue(out.single() is Frame.Scroll)
        assertNull(path)
    }

    @Test
    fun aPairThatMovedAfterArmingKeepsTheMidpointStroke() {
        armAShape()
        shaped.send(Action.MOVE, 500, 100f to 140f, 160f to 140f)
        shaped.send(Action.MOVE, 520, 100f to 180f, 160f to 180f)
        shaped.send(Action.UP, 600, 160f to 180f)
        assertFalse(shaped.drawingAlone)
        shaped.send(Action.MOVE, 605, 160f to 200f)
        shaped.send(Action.UP, 610)
        assertTrue("no frames: ${out.toList()}", out.isEmpty())
        val (xs, ys) = path ?: error("no stroke handed over")
        assertEquals("the midpoint, never the finger left behind", listOf(130f, 130f, 130f), xs.toList())
        assertEquals(listOf(100f, 140f, 180f), ys.toList())
    }

    @Test
    fun aCancelledPenDropsTheStroke() {
        armAShape()
        shaped.send(Action.UP, 500, 160f to 100f)
        shaped.send(Action.MOVE, 550, 160f to 140f)
        shaped.send(Action.CANCEL, 600)
        assertTrue(out.isEmpty())
        assertNull(path)
    }

    @Test
    fun withNoShapeBoundTwoFingersHeldStillStillScroll() {
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.DOWN, 10, 100f to 100f, 160f to 100f)
        pad.tick(10 + TrackpadRecognizer.HOLD_MS)
        touch(Action.MOVE, 500, 100f to 140f, 160f to 140f)
        assertTrue(out.single() is Frame.Scroll)
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
    fun scrollReplaysTheTravelSpentDecidingScrollFromPinch() {
        // The regression: move() advances lastX/lastY on every sample, including the ones spent below the
        // slop working out whether this is a scroll or a pinch. Reading only the deciding sample's delta
        // threw that travel away and started every scroll SLOP_DP late. Two hops that cross the slop must
        // scroll exactly as far as one hop covering the same distance.
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.DOWN, 1, 100f to 100f, 160f to 100f)
        touch(Action.MOVE, 10, 100f to 104f, 160f to 104f)
        touch(Action.MOVE, 20, 100f to 140f, 160f to 140f)
        val stepped = out.filterIsInstance<Frame.Scroll>().sumOf { it.dy }

        out.clear()
        val direct = TrackpadRecognizer(density = 1f) { out.add(it) }
        direct.handle(Action.DOWN, floatArrayOf(100f), floatArrayOf(100f), 0)
        direct.handle(Action.DOWN, floatArrayOf(100f, 160f), floatArrayOf(100f, 100f), 1)
        direct.handle(Action.MOVE, floatArrayOf(100f, 160f), floatArrayOf(140f, 140f), 20)
        val oneGo = out.filterIsInstance<Frame.Scroll>().sumOf { it.dy }

        assertEquals("the slop must be replayed, not dropped", oneGo, stepped)
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
    fun aSecondFingerLandingBesideTheFirstIsNotAMove() {
        // Real touches report a MOVE right after the second finger lands, at the same places.
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.DOWN, 5, 100f to 100f, 160f to 100f)
        touch(Action.MOVE, 10, 100f to 100f, 160f to 100f)
        touch(Action.MOVE, 20, 101f to 100f, 161f to 100f)
        touch(Action.UP, 60, 161f to 100f)
        touch(Action.UP, 70)
        assertEquals(listOf(Frame.PointerButton(1, true), Frame.PointerButton(1, false)), out)
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
    fun aTwoFingerTapSurvivesTheMoveBetweenTheLifts() {
        // A real digitiser sends a MOVE between the two lifts, at the remaining finger's place.
        touch(Action.DOWN, 0, 100f to 100f)
        touch(Action.DOWN, 1, 100f to 100f, 160f to 100f)
        touch(Action.UP, 60, 160f to 100f)
        touch(Action.MOVE, 65, 160f to 100f)
        touch(Action.UP, 70)
        assertEquals(listOf(Frame.PointerButton(1, true), Frame.PointerButton(1, false)), out)
    }

    @Test
    fun aFourFingerTapSurvivesMovesBetweenEachLift() {
        touch(Action.DOWN, 0, 100f to 300f, 150f to 300f, 200f to 300f, 250f to 300f)
        touch(Action.UP, 50, 150f to 300f, 200f to 300f, 250f to 300f)
        touch(Action.MOVE, 55, 150f to 300f, 200f to 300f, 250f to 300f)
        touch(Action.UP, 60, 200f to 300f, 250f to 300f)
        touch(Action.MOVE, 65, 200f to 300f, 250f to 300f)
        touch(Action.UP, 70, 250f to 300f)
        touch(Action.MOVE, 75, 250f to 300f)
        touch(Action.UP, 80)
        assertEquals(listOf(ActionId.NOTIFICATIONS.frame()), out)
    }

    @Test
    fun aLiftDuringASwipeKeepsTheDistanceAlreadyTravelled() {
        // 30 dp with three fingers, then 30 dp more with two: 60 dp in all, past SWIPE_DP only if the travel
        // before the lift is kept.
        touch(Action.DOWN, 0, 100f to 300f, 150f to 300f, 200f to 300f)
        touch(Action.MOVE, 20, 100f to 270f, 150f to 270f, 200f to 270f)
        touch(Action.UP, 30, 150f to 270f, 200f to 270f)
        touch(Action.MOVE, 40, 150f to 240f, 200f to 240f)
        touch(Action.UP, 50, 200f to 240f)
        touch(Action.UP, 60)
        assertEquals(listOf(ActionId.TASK_VIEW.frame()), out)
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
        pad.tick(TrackpadRecognizer.HOLD_MS)
        touch(Action.CANCEL, 500)
        assertEquals(listOf(Frame.PointerButton(0, true), Frame.PointerButton(0, false)), out)
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
        volume.handle(Action.MOVE, floatArrayOf(100f, 150f, 200f), floatArrayOf(250f, 250f, 250f), 40)
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
