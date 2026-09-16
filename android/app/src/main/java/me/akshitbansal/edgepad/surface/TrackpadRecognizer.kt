package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.Frame
import kotlin.math.abs
import kotlin.math.hypot

/**
 * Turns raw touches on the trackpad into frames. One finger is fixed: move, tap to click, tap then hold
 * to drag. Two fingers dragging always scroll. Everything else (two-finger tap and pinch, three and four
 * fingers tapping or swiping) runs whatever [map] assigns it.
 *
 * Pure: no Android types, so the gesture table is tested on the JVM. Positions are in pixels; [density]
 * (pixels per dp) scales every threshold so the feel is the same on any screen. A gesture is classified by
 * the most fingers it ever had, so a finger lifting early never turns a swipe into pointer moves.
 */
class TrackpadRecognizer(
    private val density: Float,
    /** Content follows the fingers, as on Windows' own touchpads; false scrolls the other way. */
    private val naturalScroll: Boolean = true,
    private val map: (Gesture) -> GestureAction = { it.default },
    private val sink: (Frame) -> Unit,
) {
    enum class Action { DOWN, MOVE, UP, CANCEL }

    private enum class TwoFingerMode { UNDECIDED, SCROLL, PINCH }

    private var fingers = 0
    private var maxFingers = 0
    private var startTime = 0L
    private var startX = 0f
    private var startY = 0f
    private var lastX = 0f
    private var lastY = 0f
    private var moved = false
    private var dragging = false
    private var lastTapUp = Long.MIN_VALUE
    private var swipeFired = false
    private var running: GestureAction? = null
    private var runningAlongX = false
    private var runningSign = 1f
    private var stepAnchor = 0f
    private var twoFinger = TwoFingerMode.UNDECIDED
    private var startSpan = 0f
    private var lastSpan = 0f
    private var pinchRemainder = 0f
    private var moveRemX = 0f
    private var moveRemY = 0f
    private var scrollRemX = 0f
    private var scrollRemY = 0f

    /**
     * One touch sample. [xs] and [ys] hold every finger still on the surface after this event, so on an
     * UP of one finger among several the caller passes the ones that remain.
     */
    fun handle(
        action: Action,
        xs: FloatArray,
        ys: FloatArray,
        time: Long,
    ) {
        when (action) {
            Action.DOWN -> down(xs, ys, time)
            Action.MOVE -> move(xs, ys)
            Action.UP -> up(xs, ys, time)
            Action.CANCEL -> finish(time, tapAllowed = false)
        }
    }

    private fun down(
        xs: FloatArray,
        ys: FloatArray,
        time: Long,
    ) {
        if (fingers == 0) {
            reset()
            startTime = time
            startX = centroid(xs)
            startY = centroid(ys)
            // Added, not subtracted: the never-tapped sentinel is Long.MIN_VALUE and must not overflow.
            if (time <= lastTapUp + DRAG_TAP_GAP_MS) {
                dragging = true
                sink(Frame.PointerButton(LEFT, true))
            }
        }
        fingers = xs.size
        maxFingers = maxOf(maxFingers, fingers)
        lastX = centroid(xs)
        lastY = centroid(ys)
        // Another finger moves the centroid without anything having slid: the gesture starts again from
        // here, or every two-finger touch would count as moved and never as a tap.
        startX = lastX
        startY = lastY
        if (fingers == 2) {
            startSpan = span(xs, ys)
            lastSpan = startSpan
        }
    }

    private fun move(
        xs: FloatArray,
        ys: FloatArray,
    ) {
        if (fingers == 0 || xs.isEmpty()) return
        val cx = centroid(xs)
        val cy = centroid(ys)
        val dx = cx - lastX
        val dy = cy - lastY
        lastX = cx
        lastY = cy
        val fromStartX = cx - startX
        val fromStartY = cy - startY
        if (!moved && hypot(fromStartX, fromStartY) > dp(SLOP_DP)) moved = true

        when (maxFingers) {
            1 -> if (moved) pointer(dx, dy)
            2 -> twoFingers(xs, ys, dx, dy, fromStartX, fromStartY)
            else -> swipe(cx, cy, fromStartX, fromStartY)
        }
    }

    private fun pointer(
        dx: Float,
        dy: Float,
    ) {
        moveRemX += dx * POINTER_GAIN
        moveRemY += dy * POINTER_GAIN
        val ix = moveRemX.toInt()
        val iy = moveRemY.toInt()
        moveRemX -= ix
        moveRemY -= iy
        if (ix != 0 || iy != 0) sink(Frame.Move(ix, iy))
    }

    private fun twoFingers(
        xs: FloatArray,
        ys: FloatArray,
        dx: Float,
        dy: Float,
        fromStartX: Float,
        fromStartY: Float,
    ) {
        if (fingers != 2) return
        val current = span(xs, ys)
        if (twoFinger == TwoFingerMode.UNDECIDED) {
            val spread = abs(current - startSpan)
            val travel = hypot(fromStartX, fromStartY)
            if (spread < dp(SLOP_DP) && travel < dp(SLOP_DP)) return
            twoFinger = if (spread > travel) TwoFingerMode.PINCH else TwoFingerMode.SCROLL
        }
        if (twoFinger == TwoFingerMode.PINCH) {
            // The pinch's action, once per notch of spread: fingers apart is forward.
            pinchRemainder += (current - lastSpan) / dp(PINCH_STEP_DP)
            val steps = pinchRemainder.toInt()
            pinchRemainder -= steps
            if (steps != 0) {
                val action = map(Gesture.TWO_PINCH)
                if (action.continuous) {
                    begin(action, alongX = true)
                    step(action, steps)
                } else if (!swipeFired) {
                    swipeFired = true
                    oneShot(action)
                }
            }
        } else {
            // Natural scrolling, Windows' default: content follows the fingers, so fingers down is wheel forward.
            val direction = if (naturalScroll) 1f else -1f
            scrollRemX += -dx * direction * SCROLL_UNITS_PER_DP / density
            scrollRemY += dy * direction * SCROLL_UNITS_PER_DP / density
            val ix = scrollRemX.toInt()
            val iy = scrollRemY.toInt()
            scrollRemX -= ix
            scrollRemY -= iy
            if (ix != 0 || iy != 0) sink(Frame.Scroll(ix, iy))
        }
        lastSpan = current
    }

    private fun swipe(
        cx: Float,
        cy: Float,
        fromStartX: Float,
        fromStartY: Float,
    ) {
        val action = running
        if (action != null) {
            // Each further step of travel along the swipe fires again: a long swipe walks on.
            val at = if (runningAlongX) cx else cy
            while ((at - stepAnchor) * runningSign > dp(SWITCH_STEP_DP)) {
                stepAnchor += runningSign * dp(SWITCH_STEP_DP)
                step(action, 1)
            }
            while ((stepAnchor - at) * runningSign > dp(SWITCH_STEP_DP)) {
                stepAnchor -= runningSign * dp(SWITCH_STEP_DP)
                step(action, -1)
            }
            return
        }
        if (swipeFired) return
        val vertical = abs(fromStartY) >= abs(fromStartX)
        val far = if (vertical) abs(fromStartY) else abs(fromStartX)
        if (far <= dp(SWIPE_DP)) return
        swipeFired = true
        val kind =
            when {
                vertical && fromStartY < 0 -> Gesture.Kind.UP
                vertical -> Gesture.Kind.DOWN
                fromStartX < 0 -> Gesture.Kind.LEFT
                else -> Gesture.Kind.RIGHT
            }
        val assigned = Gesture.of(maxFingers.coerceAtMost(MAX_FINGERS), kind)?.let(map) ?: GestureAction.NOTHING
        if (assigned.continuous) {
            // Up and right count as forward, so volume rises with the finger and falls back down.
            runningSign = if (vertical) -1f else 1f
            stepAnchor = if (vertical) cy else cx
            begin(assigned, alongX = !vertical)
            // Alt+Tab already moved to the next app when the switcher opened.
            if (assigned != GestureAction.APP_SWITCHER) step(assigned, 1)
        } else {
            oneShot(assigned)
        }
    }

    private fun begin(
        action: GestureAction,
        alongX: Boolean,
    ) {
        if (running != null) return
        running = action
        runningAlongX = alongX
        if (action == GestureAction.APP_SWITCHER) sink(ActionId.APP_SWITCH_BEGIN.frame())
    }

    private fun step(
        action: GestureAction,
        direction: Int,
    ) {
        val forward = direction > 0
        when (action) {
            GestureAction.VOLUME -> {
                repeat(abs(direction)) { sink((if (forward) ActionId.VOLUME_UP else ActionId.VOLUME_DOWN).frame()) }
            }

            GestureAction.BRIGHTNESS -> {
                repeat(
                    abs(direction),
                ) { sink((if (forward) ActionId.BRIGHTNESS_UP else ActionId.BRIGHTNESS_DOWN).frame()) }
            }

            GestureAction.ZOOM -> {
                sink(Frame.Zoom(direction * WHEEL_NOTCH))
            }

            GestureAction.APP_SWITCHER -> {
                repeat(
                    abs(direction),
                ) { sink((if (forward) ActionId.APP_SWITCH_NEXT else ActionId.APP_SWITCH_PREVIOUS).frame()) }
            }

            else -> {
                Unit
            }
        }
    }

    private fun oneShot(action: GestureAction) {
        when (action) {
            GestureAction.LEFT_CLICK -> {
                click(LEFT)
            }

            GestureAction.RIGHT_CLICK -> {
                click(RIGHT)
            }

            GestureAction.MIDDLE_CLICK -> {
                click(MIDDLE)
            }

            GestureAction.APP_SWITCHER -> {
                sink(ActionId.APP_SWITCH_BEGIN.frame())
                sink(ActionId.APP_SWITCH_END.frame())
            }

            else -> {
                if (action.continuous) step(action, 1) else action.actionId?.let { sink(it.frame()) }
            }
        }
    }

    private fun up(
        xs: FloatArray,
        ys: FloatArray,
        time: Long,
    ) {
        if (xs.isEmpty()) {
            finish(time, tapAllowed = true)
            return
        }
        fingers = xs.size
        lastX = centroid(xs)
        lastY = centroid(ys)
        if (fingers == 2) lastSpan = span(xs, ys)
    }

    private fun finish(
        time: Long,
        tapAllowed: Boolean,
    ) {
        if (fingers == 0) return
        if (running == GestureAction.APP_SWITCHER) sink(ActionId.APP_SWITCH_END.frame())
        running = null
        val untouched = !moved && !swipeFired && twoFinger == TwoFingerMode.UNDECIDED
        if (dragging) {
            sink(Frame.PointerButton(LEFT, false))
        } else if (tapAllowed && untouched && time - startTime <= TAP_MS) {
            tap(time)
        }
        fingers = 0
    }

    private fun tap(time: Long) {
        if (maxFingers == 1) {
            click(LEFT)
            lastTapUp = time
            return
        }
        oneShot(Gesture.of(maxFingers.coerceAtMost(MAX_FINGERS), Gesture.Kind.TAP)?.let(map) ?: GestureAction.NOTHING)
    }

    private fun click(button: Int) {
        sink(Frame.PointerButton(button, true))
        sink(Frame.PointerButton(button, false))
    }

    /**
     * Deliberately does not clear [lastTapUp]. It is the one piece of state that has to survive the end of
     * a gesture: tap-then-hold-to-drag works by the second touch seeing how recently the previous one
     * lifted. Clearing it here makes every drag a plain move, and no test of a single gesture catches it.
     */
    private fun reset() {
        maxFingers = 0
        moved = false
        dragging = false
        swipeFired = false
        running = null
        twoFinger = TwoFingerMode.UNDECIDED
        pinchRemainder = 0f
        moveRemX = 0f
        moveRemY = 0f
        scrollRemX = 0f
        scrollRemY = 0f
    }

    private fun dp(value: Float): Float = value * density

    private fun centroid(values: FloatArray): Float = values.sum() / values.size

    private fun span(
        xs: FloatArray,
        ys: FloatArray,
    ): Float = hypot(xs[1] - xs[0], ys[1] - ys[0])

    companion object {
        const val LEFT = 0
        const val RIGHT = 1
        const val MIDDLE = 2
        const val WHEEL_NOTCH = 120
        private const val MAX_FINGERS = 4

        // ponytail: feel constants, tuned by hand on the M52. Pointer gain is laptop pixels per phone
        // pixel before Windows' own acceleration; scroll is wheel units per dp of finger travel.
        const val POINTER_GAIN = 1.6f
        const val SCROLL_UNITS_PER_DP = 3f
        const val PINCH_STEP_DP = 48f
        const val SLOP_DP = 8f
        const val SWIPE_DP = 48f
        const val SWITCH_STEP_DP = 72f
        const val TAP_MS = 250L
        const val DRAG_TAP_GAP_MS = 300L
    }
}
