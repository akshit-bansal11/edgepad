package me.akshitbansal.edgepad.screens

import android.animation.Animator
import android.animation.AnimatorListenerAdapter
import android.animation.ValueAnimator
import android.graphics.RectF
import android.os.SystemClock
import android.view.View
import android.view.ViewGroup
import android.view.ViewTreeObserver
import android.view.Window
import android.view.animation.DecelerateInterpolator
import android.widget.FrameLayout
import android.widget.TextView
import me.akshitbansal.edgepad.Motion
import java.lang.ref.WeakReference

/**
 * The pure arithmetic of [Carry], free of Android types so a JVM test can run it. Lengths are in pixels.
 */
object CarryMath {
    /** A rectangle in screen coordinates. */
    data class Box(
        val l: Float,
        val t: Float,
        val r: Float,
        val b: Float,
    )

    /** The transform that squeezes a full-screen page onto a [Box]: scale about the page centre, then translate. */
    data class Start(
        val scaleX: Float,
        val scaleY: Float,
        val tx: Float,
        val ty: Float,
    )

    /** How long a mark stays the origin of the next change of place. */
    const val FRESH_MS = 600L

    /** The scale given to an origin with no size, about its centre. */
    const val POINT_SCALE = 0.2f

    /** Where a page of [w] by [h] starts so that it covers [origin], which is clamped into the screen first. */
    fun start(
        origin: Box,
        w: Float,
        h: Float,
    ): Start {
        val l = origin.l.coerceIn(0f, w)
        val r = origin.r.coerceIn(0f, w)
        val t = origin.t.coerceIn(0f, h)
        val b = origin.b.coerceIn(0f, h)
        val tx = (l + r) / 2f - w / 2f
        val ty = (t + b) / 2f - h / 2f
        if (r - l <= 0f || b - t <= 0f) return Start(POINT_SCALE, POINT_SCALE, tx, ty)
        return Start((r - l) / w, (b - t) / h, tx, ty)
    }

    /** The last [max] origins, one per forward change of place; the newest is popped first. */
    class Origins(
        private val max: Int = 8,
    ) {
        private val keys = IntArray(max)
        private val boxes = arrayOfNulls<Box>(max)
        private var count = 0

        val size: Int get() = count

        fun push(
            screenKey: Int,
            box: Box,
        ) {
            if (count == max) {
                keys.copyInto(keys, 0, 1, max)
                boxes.copyInto(boxes, 0, 1, max)
                count--
            }
            keys[count] = screenKey
            boxes[count] = box
            count++
        }

        fun pop(): Box? {
            if (count == 0) return null
            count--
            val box = boxes[count]
            boxes[count] = null
            return box
        }
    }

    /** Whether a mark made at [markedAtMs] still belongs to the tap that is now being handled. */
    fun fresh(
        markedAtMs: Long,
        nowMs: Long,
    ) = nowMs - markedAtMs <= FRESH_MS

    /** A change to the same screen is a rebuild, not a change of place, and is instant. */
    fun shouldCarry(
        from: Int?,
        to: Int,
    ) = from != null && from != to
}

/**
 * Motion that carries (UX-32): a page grows out of the card that opened it and shrinks back into it on Back; a
 * popover grows from its button. One [ValueAnimator] is reused for every change of place, so nothing is allocated
 * per frame and nothing runs while idle. The control surface never goes through here with a scale (UX-28), and a
 * new [swap] first finishes the one in flight so a second tap performs the second action (UX-29).
 */
object Carry {
    private const val POPOVER_FROM = 0.85f

    private val decelerate = DecelerateInterpolator()
    private val origins = CarryMath.Origins()
    private var mark: CarryMath.Box? = null
    private var markedAt = 0L

    // Weak, because this is a process-wide object: a strong View here would hold a finished activity alive.
    private var outgoingRef = WeakReference<View>(null)
    private var incomingRef = WeakReference<View>(null)
    private var hostRef = WeakReference<FrameLayout>(null)
    private var outgoing: View?
        get() = outgoingRef.get()
        set(value) {
            outgoingRef = WeakReference(value)
        }
    private var incoming: View?
        get() = incomingRef.get()
        set(value) {
            incomingRef = WeakReference(value)
        }
    private var host: FrameLayout?
        get() = hostRef.get()
        set(value) {
            hostRef = WeakReference(value)
        }
    private var start: CarryMath.Start? = null
    private var toOrigin = false
    private var alphaSpan = Motion.REVEAL

    private val animator: ValueAnimator =
        ValueAnimator.ofFloat(0f, 1f).apply {
            addUpdateListener { frame() }
            addListener(
                object : AnimatorListenerAdapter() {
                    override fun onAnimationEnd(animation: Animator) = finish()
                },
            )
        }

    /** Remembers [view]'s screen rectangle as the origin of the next change of place. */
    fun mark(view: View) {
        val at = IntArray(2)
        view.getLocationOnScreen(at)
        remember(
            CarryMath.Box(
                at[0].toFloat(),
                at[1].toFloat(),
                (at[0] + view.width).toFloat(),
                (at[1] + view.height).toFloat(),
            ),
        )
    }

    /** As [mark], for a button drawn on a canvas, with its rectangle already in screen coordinates. */
    fun mark(rect: RectF) = remember(CarryMath.Box(rect.left, rect.top, rect.right, rect.bottom))

    /** Whether a tap marked an origin recently enough to be the cause of the change now being made. */
    fun hasFreshMark() = freshMark() != null

    private fun remember(box: CarryMath.Box) {
        mark = box
        markedAt = SystemClock.uptimeMillis()
    }

    private fun freshMark() = mark?.takeIf { CarryMath.fresh(markedAt, SystemClock.uptimeMillis()) }

    /**
     * Puts [incoming] in [host] in place of [outgoing]. [forward] grows it out of the last mark; not forward shrinks
     * [outgoing] into the origin stored by the forward change. With [crossFadeOnly], no mark, or no stored origin,
     * it is a cross-fade with no scale or translation. With animations off it is instant.
     */
    fun swap(
        host: FrameLayout,
        incoming: View,
        outgoing: View?,
        forward: Boolean,
        crossFadeOnly: Boolean,
    ) {
        cancel()
        if (outgoing === incoming) return
        // TalkBack announces a pane by its title when it appears: the screen's own heading, as setContentView did.
        if (incoming.accessibilityPaneTitle == null) incoming.accessibilityPaneTitle = heading(incoming)
        (incoming.parent as? ViewGroup)?.removeView(incoming)
        val animate =
            outgoing != null &&
                host.width > 0 &&
                host.height > 0 &&
                ValueAnimator.areAnimatorsEnabled()
        if (!animate) {
            outgoing?.let(host::removeView)
            host.addView(incoming)
            return
        }
        val box =
            if (crossFadeOnly) {
                null
            } else if (forward) {
                freshMark()
            } else {
                origins.pop()
            }
        if (box != null && forward) origins.push(origins.size, box)
        this.host = host
        this.incoming = incoming
        this.outgoing = outgoing
        toOrigin = !forward
        start = box?.let { CarryMath.start(it, host.width.toFloat(), host.height.toFloat()) }
        alphaSpan = if (start != null && !forward) Motion.SCENE else Motion.REVEAL
        if (forward) host.addView(incoming) else host.addView(incoming, 0)
        outgoing?.importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO_HIDE_DESCENDANTS
        animator.duration = if (start != null) Motion.SCENE else Motion.REVEAL
        animator.interpolator = decelerate
        frameAt(0f, 0L)
        animator.start()
    }

    /** Finishes a swap in flight at once: end listeners run and every property is reset. */
    fun cancel() {
        if (animator.isRunning) animator.end()
    }

    /**
     * Grows a dialog or popover from the last mark, else from [owner]'s centre, scale 0.85 to 1 with a fade over
     * [Motion.REVEAL]. The window is hidden at once and starts on its first draw so it never flashes at full size.
     */
    fun reveal(
        window: Window,
        owner: View,
    ) {
        if (!ValueAnimator.areAnimatorsEnabled()) return
        val decor = window.decorView
        decor.alpha = 0f
        decor.viewTreeObserver.addOnPreDrawListener(
            object : ViewTreeObserver.OnPreDrawListener {
                override fun onPreDraw(): Boolean {
                    if (decor.viewTreeObserver.isAlive) decor.viewTreeObserver.removeOnPreDrawListener(this)
                    val at = IntArray(2)
                    decor.getLocationOnScreen(at)
                    val box = freshMark()
                    val cx = if (box != null) (box.l + box.r) / 2f else centre(owner, 0)
                    val cy = if (box != null) (box.t + box.b) / 2f else centre(owner, 1)
                    decor.pivotX = cx - at[0]
                    decor.pivotY = cy - at[1]
                    decor.scaleX = POPOVER_FROM
                    decor.scaleY = POPOVER_FROM
                    decor
                        .animate()
                        .alpha(1f)
                        .scaleX(1f)
                        .scaleY(1f)
                        .setDuration(Motion.REVEAL)
                        .setInterpolator(decelerate)
                        .start()
                    return true
                }
            },
        )
    }

    private fun centre(
        view: View,
        axis: Int,
    ): Float {
        val at = IntArray(2)
        view.getLocationOnScreen(at)
        return at[axis] + (if (axis == 0) view.width else view.height) / 2f
    }

    private fun frame(): Unit = frameAt(animator.animatedValue as Float, animator.currentPlayTime)

    /** [progress] is the interpolated 0 to 1; [playMs] is the raw time, which drives the fade on its own span. */
    private fun frameAt(
        progress: Float,
        playMs: Long,
    ) {
        val seen = (playMs.toFloat() / alphaSpan).coerceIn(0f, 1f)
        val s = start
        // The moving page: the incoming one going forward, the outgoing one coming back.
        val page = (if (toOrigin) outgoing else incoming) ?: return
        val amount = if (toOrigin) progress else 1f - progress
        page.alpha = if (toOrigin) 1f - seen else seen
        if (s == null) return
        page.scaleX = 1f + (s.scaleX - 1f) * amount
        page.scaleY = 1f + (s.scaleY - 1f) * amount
        page.translationX = s.tx * amount
        page.translationY = s.ty * amount
    }

    private fun finish() {
        val out = outgoing
        val inc = incoming
        val h = host
        outgoing = null
        incoming = null
        host = null
        start = null
        for (v in arrayOf(out, inc)) {
            v?.alpha = 1f
            v?.scaleX = 1f
            v?.scaleY = 1f
            v?.translationX = 0f
            v?.translationY = 0f
        }
        out?.importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_AUTO
        if (out != null) h?.removeView(out)
    }

    /** The first visible heading in [view]'s tree, or null when it has none (an immersive surface). */
    private fun heading(view: View): CharSequence? {
        if (view.visibility != View.VISIBLE) return null
        if (view is TextView && view.isAccessibilityHeading && !view.text.isNullOrEmpty()) return view.text
        if (view !is ViewGroup) return null
        for (i in 0 until view.childCount) {
            heading(view.getChildAt(i))?.let { return it }
        }
        return null
    }
}
