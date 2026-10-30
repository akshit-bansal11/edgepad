package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.Frame
import java.util.concurrent.BlockingQueue
import java.util.concurrent.TimeUnit

/**
 * Gathers the frames the writer sends in one go. With no pace it is the plain batch: [first] and whatever is
 * already queued behind it. With a pace and a motion frame first, it also waits out the pace for more motion to
 * arrive, so a link whose socket never blocks still hands [Coalesce] something to merge.
 */
object PacedBatch {
    private const val PEN_MOVE = 2
    private const val NANOS_PER_MS = 1_000_000L

    /**
     * Adds [first] and then what [outbox] holds to [into]. When [paceMs] is above 0 and [first] is motion, it keeps
     * taking frames until [paceMs] has passed on [now] (nanoseconds) since the batch began, and ends the moment a
     * frame that is not motion arrives: a key, a button, an action, text or a ping is an edge, and an edge is never
     * made to wait on the pace.
     */
    fun collect(
        first: Frame,
        outbox: BlockingQueue<Frame>,
        paceMs: Int,
        now: () -> Long,
        into: MutableList<Frame>,
    ) {
        into.add(first)
        if (paceMs > 0 && isMotion(first)) {
            val deadline = now() + paceMs * NANOS_PER_MS
            while (true) {
                val left = deadline - now()
                if (left <= 0) break
                val next = outbox.poll(left, TimeUnit.NANOSECONDS) ?: break
                into.add(next)
                if (!isMotion(next)) break
            }
        }
        outbox.drainTo(into)
    }

    // Frames [Coalesce] folds into a snapshot, which is what makes waiting for more of them worth it.
    private fun isMotion(frame: Frame): Boolean =
        frame is Frame.Move ||
            frame is Frame.Scroll ||
            frame is Frame.Zoom ||
            frame is Frame.SetValue ||
            frame is Frame.PadState ||
            (frame is Frame.Pen && frame.state == PEN_MOVE)
}
