package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.Frame
import kotlin.math.abs
import kotlin.math.hypot

/**
 * Turns raw touches on the trackpad into frames. One and two fingers are fixed, the way every trackpad
 * behaves: one finger moves, taps to click, taps then holds to drag; two fingers drag to scroll, pinch to
 * zoom and tap to right-click. Three and four fingers tapping or swiping run whatever [map] assigns them.
 *
 * One finger has a second life: held still for [SHAPE_HOLD_MS] it stops moving the pointer and starts
 * drawing, and the whole stroke goes to [onShape] when it lifts. That seam was free — a bare press and hold
 * with no tap in front of it did nothing at all before — but it is a narrow one, so [armShape] says in full
 * what it refuses to claim.
 *
 * Pure: no Android types, so the gesture table is tested on the JVM. Positions are in pixels; [density]
 * (pixels per dp) scales every threshold so the feel is the same on any screen. A gesture is classified by
 * the most fingers it ever had, so a finger lifting early never turns a swipe into pointer moves.
 */
class TrackpadRecognizer(
    private val density: Float,
    /** Content follows the fingers, as on Windows' own touchpads; false scrolls the other way. */
    private val naturalScroll: Boolean = true,
    /** Multiplies [POINTER_GAIN]: how far the laptop's pointer travels per unit of finger travel. */
    private val pointerSpeed: Float = 1f,
    /** Multiplies [SCROLL_UNITS_PER_DP]: how much wheel one dp of two-finger drag is worth. */
    private val scrollSpeed: Float = 1f,
    private val map: (Gesture) -> GestureAction = { it.default },
    /**
     * Handed the whole stroke when a finger that became a shape lifts: the first `count` entries of the two
     * arrays, in pixels, starting at the press rather than at the moment the hold expired.
     *
     * Null turns shape mode off outright, which is what the surface passes when nothing is bound to a
     * shape. Then a press and hold stays what it has always been on this pad, which is nothing at all —
     * rather than swallowing a third of a second of pointer movement on every phone that never drew one.
     */
    private val onShape: ((xs: FloatArray, ys: FloatArray, count: Int) -> Unit)? = null,
    private val sink: (Frame) -> Unit,
) {
    enum class Action { DOWN, MOVE, UP, CANCEL }

    private enum class TwoFingerMode { UNDECIDED, SCROLL, PINCH }

    /**
     * True from the moment a held single finger becomes a stroke until it lifts. Read by the surface, which
     * ticks once on the change and draws the trail solid while it is set; the decision itself is made here
     * and nowhere else, so there is one answer to "is this a shape" rather than two that can disagree.
     */
    var shaping = false
        private set

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

    // The stroke, in screen pixels, kept from the press onward whether or not it ever becomes one. Fixed
    // arrays because this is filled on the touch path, which the surface asks for unbuffered: a fresh
    // buffer per gesture is garbage collected while a finger is drawing on the glass.
    private val pathXs = FloatArray(MAX_PATH)
    private val pathYs = FloatArray(MAX_PATH)
    private var pathCount = 0

    /**
     * One touch sample. The first [count] entries of [xs] and [ys] hold every finger still on the surface
     * after this event, so on an UP of one finger among several the caller passes the ones that remain.
     *
     * [count] is separate from `xs.size` so a caller on the touch path can hand over one buffer it keeps
     * and refills, rather than a fresh pair of arrays per sample. It defaults to the whole array, which is
     * what a test passing an exact-sized literal wants.
     */
    fun handle(
        action: Action,
        xs: FloatArray,
        ys: FloatArray,
        time: Long,
        count: Int = xs.size,
    ) {
        when (action) {
            Action.DOWN -> down(xs, ys, count, time)
            Action.MOVE -> move(xs, ys, count, time)
            Action.UP -> up(xs, ys, count, time)
            Action.CANCEL -> finish(time, tapAllowed = false)
        }
    }

    private fun down(
        xs: FloatArray,
        ys: FloatArray,
        count: Int,
        time: Long,
    ) {
        if (fingers == 0) {
            reset()
            startTime = time
            startX = centroid(xs, count)
            startY = centroid(ys, count)
            // Added, not subtracted: the never-tapped sentinel is Long.MIN_VALUE and must not overflow.
            if (time <= lastTapUp + DRAG_TAP_GAP_MS) {
                dragging = true
                sink(Frame.PointerButton(LEFT, true))
            }
        }
        fingers = count
        maxFingers = maxOf(maxFingers, fingers)
        lastX = centroid(xs, count)
        lastY = centroid(ys, count)
        // Another finger moves the centroid without anything having slid: the gesture starts again from
        // here, or every two-finger touch would count as moved and never as a tap.
        startX = lastX
        startY = lastY
        // A second finger is not part of a one-finger stroke: whatever was being drawn is abandoned here,
        // and the gesture carries on as the two-finger one it has become.
        if (maxFingers == 1) record(lastX, lastY) else shaping = false
        if (fingers == 2) {
            startSpan = span(xs, ys)
            lastSpan = startSpan
        }
    }

    private fun move(
        xs: FloatArray,
        ys: FloatArray,
        count: Int,
        time: Long,
    ) {
        if (fingers == 0 || count == 0) return
        val cx = centroid(xs, count)
        val cy = centroid(ys, count)
        val dx = cx - lastX
        val dy = cy - lastY
        lastX = cx
        lastY = cy
        val fromStartX = cx - startX
        val fromStartY = cy - startY
        // Every single-finger sample is kept from the press onward, not from the moment the hold expires.
        // Same trap the scroll-or-pinch decision below documents: the travel spent before the decision is
        // travel the user drew, and a stroke missing its first few millimetres is a different stroke.
        // [armShape] runs before `moved` is updated, so "still for the whole hold" means exactly that.
        // Once it has armed, the finger stops driving the pointer: the laptop must not sit there watching
        // the shape being drawn on it.
        if (maxFingers == 1) {
            record(cx, cy)
            armShape(time)
        }
        if (!moved && hypot(fromStartX, fromStartY) > dp(SLOP_DP)) moved = true

        when (maxFingers) {
            1 -> if (moved && !shaping) pointer(dx, dy)
            2 -> twoFingers(xs, ys, dx, dy, fromStartX, fromStartY)
            else -> swipe(cx, cy, fromStartX, fromStartY)
        }
    }

    /**
     * Turns a single finger that has stayed put into a stroke. [SHAPE_HOLD_MS] is added to the press time
     * and never subtracted from now, for the same reason [down] adds to [lastTapUp].
     *
     * A drag is excluded outright. A drag on this pad is tap, lift, press, move, so its second press is a
     * hold — the very thing this claims — and arming on it would turn every unhurried drag into a scrawl.
     * That exclusion is also what made a bare press and hold free to take: with no tap in front of it, it
     * reached [finish] past the [TAP_MS] window and did nothing at all.
     */
    private fun armShape(time: Long) {
        if (shaping || moved || dragging || onShape == null) return
        if (time >= startTime + SHAPE_HOLD_MS) shaping = true
    }

    /**
     * Keeps one more sample of the stroke. A full buffer stops taking them rather than growing or dropping
     * the beginning: [MAX_PATH] samples is several seconds of drawing at any digitiser's rate, and a fixed
     * array is what keeps this path free of allocation while a finger is on the glass.
     */
    private fun record(
        x: Float,
        y: Float,
    ) {
        if (pathCount >= MAX_PATH) return
        pathXs[pathCount] = x
        pathYs[pathCount] = y
        pathCount++
    }

    private fun pointer(
        dx: Float,
        dy: Float,
    ) {
        moveRemX += dx * POINTER_GAIN * pointerSpeed
        moveRemY += dy * POINTER_GAIN * pointerSpeed
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
        var justDecided = false
        if (twoFinger == TwoFingerMode.UNDECIDED) {
            val spread = abs(current - startSpan)
            val travel = hypot(fromStartX, fromStartY)
            if (spread < dp(SLOP_DP) && travel < dp(SLOP_DP)) return
            twoFinger = if (spread > travel) TwoFingerMode.PINCH else TwoFingerMode.SCROLL
            justDecided = true
        }
        if (twoFinger == TwoFingerMode.PINCH) {
            // Ctrl+wheel, once per notch of spread: fingers apart is forward. lastSpan is still startSpan
            // on the deciding sample, because the undecided ones return above without touching it, so the
            // spread spent deciding is already part of this first step.
            pinchRemainder += (current - lastSpan) / dp(PINCH_STEP_DP)
            val steps = pinchRemainder.toInt()
            pinchRemainder -= steps
            if (steps != 0) sink(Frame.Zoom(steps * WHEEL_NOTCH))
        } else {
            // Natural scrolling, Windows' default: content follows the fingers, so fingers down is wheel forward.
            //
            // On the sample that settles scroll-or-pinch, the travel replayed is the whole distance since
            // the touch began, not just this sample's. move() advances lastX/lastY on every sample including
            // the undecided ones, so dx/dy here hold only the last step and the slop spent deciding would be
            // dropped on the floor: the scroll would start late, by exactly SLOP_DP, on every single stroke.
            // Pinch never had this bug because lastSpan is only advanced once a mode is settled.
            val alongX = if (justDecided) fromStartX else dx
            val alongY = if (justDecided) fromStartY else dy
            val direction = if (naturalScroll) 1f else -1f
            val units = SCROLL_UNITS_PER_DP * scrollSpeed / density
            scrollRemX += -alongX * direction * units
            scrollRemY += alongY * direction * units
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

    /**
     * Fires [action] once. Public because a drawn shape runs the same one-shot vocabulary a tap does, and
     * one place deciding what "left click" means beats two places that can drift apart.
     */
    fun oneShot(action: GestureAction) {
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
        count: Int,
        time: Long,
    ) {
        if (count == 0) {
            finish(time, tapAllowed = true)
            return
        }
        fingers = count
        lastX = centroid(xs, count)
        lastY = centroid(ys, count)
        if (fingers == 2) lastSpan = span(xs, ys)
    }

    private fun finish(
        time: Long,
        tapAllowed: Boolean,
    ) {
        if (fingers == 0) return
        if (running == GestureAction.APP_SWITCHER) sink(ActionId.APP_SWITCH_END.frame())
        running = null
        val drawn = shaping
        shaping = false
        val untouched = !moved && !swipeFired && twoFinger == TwoFingerMode.UNDECIDED
        // Cleared before the stroke is handed over, not after: the surface may lock the pad from inside
        // that call, and locking cancels the touch, which comes straight back in here.
        fingers = 0
        when {
            // A stroke ends as a stroke and as nothing else: no tap, no click on whatever was under the
            // finger. A cancelled one is dropped, because the system took the gesture away mid-draw and
            // the part that arrived is not what the user meant to draw.
            drawn -> {
                if (tapAllowed) onShape?.invoke(pathXs, pathYs, pathCount)
            }

            dragging -> {
                sink(Frame.PointerButton(LEFT, false))
            }

            tapAllowed && untouched && time - startTime <= TAP_MS -> {
                tap(time)
            }
        }
    }

    private fun tap(time: Long) {
        if (maxFingers == 1) {
            click(LEFT)
            lastTapUp = time
            return
        }
        if (maxFingers == 2) {
            click(RIGHT)
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
        shaping = false
        pathCount = 0
        pinchRemainder = 0f
        moveRemX = 0f
        moveRemY = 0f
        scrollRemX = 0f
        scrollRemY = 0f
    }

    private fun dp(value: Float): Float = value * density

    /** The mean of the first [count] entries. Reads the buffer's live part, never its capacity. */
    private fun centroid(
        values: FloatArray,
        count: Int,
    ): Float {
        var sum = 0f
        for (i in 0 until count) sum += values[i]
        return sum / count
    }

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

        /**
         * How long one finger must stay inside [SLOP_DP] before the press becomes a stroke. Comfortably
         * past [TAP_MS], so a tap can never grow into a shape, and past the gap a drag's second press
         * lands in, so the two never race. Shorter and a thinking pause starts drawing; longer and the
         * tick feels like the pad noticed late.
         */
        const val SHAPE_HOLD_MS = 350L

        /**
         * The most samples one stroke keeps. At any digitiser's rate this is several seconds of drawing,
         * and a stroke longer than that is not a shape anyone will reproduce twice. A full buffer stops
         * recording rather than growing, so the ceiling is a slightly clipped tail, never an allocation.
         *
         * Public because the screen a shape is drawn on keeps the same ceiling: an editor that accepted a
         * longer stroke than the pad can take would save a shape whose tail the pad never sees again.
         */
        const val MAX_PATH = 512
    }
}
