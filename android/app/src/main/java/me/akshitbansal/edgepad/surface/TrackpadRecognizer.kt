package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.Frame
import kotlin.math.abs
import kotlin.math.hypot

/**
 * Turns raw touches on the trackpad into frames:
 *
 * - one finger: move; tap = left click; tap then hold-and-move = drag
 * - two fingers: drag = scroll both axes; pinch = zoom; tap = right click
 * - three fingers: left/right = switch desktop, up = task view, down = show desktop, tap = search
 * - four fingers: left/right = app switcher (Alt held while the fingers are down), up/down as three,
 *   tap = notifications
 *
 * Pure: no Android types, so the gesture table is tested on the JVM. Positions are in pixels; [density]
 * (pixels per dp) scales every threshold so the feel is the same on any screen. A gesture is classified by
 * the most fingers it ever had, so a finger lifting early never turns a swipe into pointer moves.
 */
class TrackpadRecognizer(
    private val density: Float,
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
    private var switching = false
    private var stepAnchor = 0f
    private var twoFinger = TwoFingerMode.UNDECIDED
    private var startSpan = 0f
    private var lastSpan = 0f
    private var zoomRemainder = 0f
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
            if (time - lastTapUp <= DRAG_TAP_GAP_MS) {
                dragging = true
                sink(Frame.PointerButton(LEFT, true))
            }
        }
        fingers = xs.size
        maxFingers = maxOf(maxFingers, fingers)
        lastX = centroid(xs)
        lastY = centroid(ys)
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
            else -> swipe(cx, fromStartX, fromStartY)
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
            zoomRemainder += (current - lastSpan) / dp(ZOOM_STEP_DP)
            val steps = zoomRemainder.toInt()
            zoomRemainder -= steps
            if (steps != 0) sink(Frame.Zoom(steps * WHEEL_NOTCH))
        } else {
            // Windows' default: content follows the fingers. Fingers down = wheel forward = positive.
            scrollRemX += -dx * SCROLL_UNITS_PER_DP / density
            scrollRemY += dy * SCROLL_UNITS_PER_DP / density
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
        fromStartX: Float,
        fromStartY: Float,
    ) {
        if (switching) {
            // Each further step of travel moves the Alt+Tab selection, so a long swipe walks the list.
            while (cx - stepAnchor > dp(SWITCH_STEP_DP)) {
                stepAnchor += dp(SWITCH_STEP_DP)
                sink(ActionId.APP_SWITCH_NEXT.frame())
            }
            while (stepAnchor - cx > dp(SWITCH_STEP_DP)) {
                stepAnchor -= dp(SWITCH_STEP_DP)
                sink(ActionId.APP_SWITCH_PREVIOUS.frame())
            }
            return
        }
        if (swipeFired) return
        val vertical = abs(fromStartY) >= abs(fromStartX)
        if (vertical && abs(fromStartY) > dp(SWIPE_DP)) {
            swipeFired = true
            sink((if (fromStartY < 0) ActionId.TASK_VIEW else ActionId.SHOW_DESKTOP).frame())
        } else if (!vertical && abs(fromStartX) > dp(SWIPE_DP)) {
            swipeFired = true
            if (maxFingers == THREE) {
                sink((if (fromStartX < 0) ActionId.DESKTOP_LEFT else ActionId.DESKTOP_RIGHT).frame())
            } else {
                switching = true
                stepAnchor = cx
                sink(ActionId.APP_SWITCH_BEGIN.frame())
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
        if (switching) sink(ActionId.APP_SWITCH_END.frame())
        val untouched = !moved && !swipeFired && twoFinger == TwoFingerMode.UNDECIDED
        if (dragging) {
            sink(Frame.PointerButton(LEFT, false))
        } else if (tapAllowed && untouched && time - startTime <= TAP_MS) {
            tap(time)
        }
        fingers = 0
    }

    private fun tap(time: Long) {
        when (maxFingers) {
            1 -> {
                click(LEFT)
                lastTapUp = time
            }

            2 -> {
                click(RIGHT)
            }

            THREE -> {
                sink(ActionId.SEARCH.frame())
            }

            else -> {
                sink(ActionId.NOTIFICATIONS.frame())
            }
        }
    }

    private fun click(button: Int) {
        sink(Frame.PointerButton(button, true))
        sink(Frame.PointerButton(button, false))
    }

    private fun reset() {
        maxFingers = 0
        moved = false
        dragging = false
        swipeFired = false
        switching = false
        twoFinger = TwoFingerMode.UNDECIDED
        zoomRemainder = 0f
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
        const val WHEEL_NOTCH = 120
        private const val THREE = 3

        // ponytail: feel constants, tuned by hand on the M52. Pointer gain is laptop pixels per phone
        // pixel before Windows' own acceleration; scroll is wheel units per dp of finger travel.
        const val POINTER_GAIN = 1.6f
        const val SCROLL_UNITS_PER_DP = 3f
        const val ZOOM_STEP_DP = 48f
        const val SLOP_DP = 8f
        const val SWIPE_DP = 48f
        const val SWITCH_STEP_DP = 72f
        const val TAP_MS = 250L
        const val DRAG_TAP_GAP_MS = 300L
    }
}
