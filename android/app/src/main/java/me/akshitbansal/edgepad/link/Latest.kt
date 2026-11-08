package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.TextKind
import java.util.concurrent.atomic.AtomicReferenceArray

/**
 * Inbound frames that are a snapshot (a level, what is playing, where in it) reach the main thread through here: at
 * most one post per kind is ever waiting, and it carries the newest value, so a laptop reporting faster than the main
 * thread drains cannot queue a backlog. One-shot frames (status tokens, files, HELLO, lists) are not [offer]ed and
 * keep their order and every copy.
 *
 * The runnables are built once, so a frame costs one atomic swap and, when none was waiting, one post.
 *
 * @param post Hands a step to the main thread.
 * @param deliver Runs on the main thread with the newest frame of a slot.
 */
internal class Latest(
    private val post: (Runnable) -> Unit,
    private val deliver: (Frame) -> Unit,
) {
    private val cells = AtomicReferenceArray<Frame?>(SLOTS)
    private val runs = Array(SLOTS) { slot -> Runnable { cells.getAndSet(slot, null)?.let(deliver) } }

    /** True when [frame] was taken (and will be delivered); false for a frame the caller must deliver itself. */
    fun offer(frame: Frame): Boolean {
        val slot = slotOf(frame)
        if (slot < 0) return false
        if (cells.getAndSet(slot, frame) == null) post(runs[slot])
        return true
    }

    companion object {
        /** One slot per control id below [CONTROL_SLOTS], then one per coalesced text kind. */
        private const val CONTROL_SLOTS = 32
        private const val SLOTS = CONTROL_SLOTS + 3

        /**
         * The slot of a snapshot frame, or -1. MACRO_OPEN is left out on purpose: it is change-only and must keep its
         * order with the MACROS list that clears it.
         */
        fun slotOf(frame: Frame): Int =
            when {
                frame is Frame.StateReport && frame.control in 0 until CONTROL_SLOTS -> frame.control
                frame is Frame.Text && frame.kind == TextKind.NOW_PLAYING.id -> CONTROL_SLOTS
                frame is Frame.Text && frame.kind == TextKind.APP.id -> CONTROL_SLOTS + 1
                frame is Frame.Text && frame.kind == TextKind.TIMELINE.id -> CONTROL_SLOTS + 2
                else -> -1
            }
    }
}
