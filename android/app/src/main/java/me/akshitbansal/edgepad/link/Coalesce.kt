package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.Frame

/**
 * Collapses a backlog of queued frames before it is written. Consecutive MOVE, SCROLL and ZOOM frames are
 * summed, and only the last SET per control and the last PAD_STATE survive, so a link that falls behind the
 * finger catches up in one write instead of replaying every sample it missed. Everything else keeps its
 * order, KEY frames above all: see [merge].
 */
object Coalesce {
    /**
     * A KEY frame is never collapsed into another, and neither is a POINTER_BUTTON: they are edges, and a
     * press and its release are two different messages that happen to name the same key. Dropping either
     * would leave the key held down on the laptop until the user pressed it again. Everything collapsed
     * below is a snapshot instead — a position, a level, the whole pad — where only the newest one means
     * anything and the ones before it describe a moment that has already passed.
     */
    fun merge(frames: List<Frame>): List<Frame> {
        val out = ArrayList<Frame>(frames.size)
        for (frame in frames) {
            val last = out.lastOrNull()
            when {
                frame is Frame.Move && last is Frame.Move -> {
                    out[out.lastIndex] = Frame.Move(sum(last.dx, frame.dx), sum(last.dy, frame.dy))
                }

                frame is Frame.Scroll && last is Frame.Scroll -> {
                    out[out.lastIndex] = Frame.Scroll(sum(last.dx, frame.dx), sum(last.dy, frame.dy))
                }

                frame is Frame.Zoom && last is Frame.Zoom -> {
                    out[out.lastIndex] = Frame.Zoom(sum(last.delta, frame.delta))
                }

                // A PAD_STATE is the whole controller as it stands, not a change to it, so an older one is
                // worth nothing whatever landed between the two: the laptop holds the last one it was given
                // and the newest is the only one that describes the fingers now on the glass. It may pass a
                // KEY frame on the way — a layout may hold both kinds — and that reorders nothing that
                // matters, since the two land on a keyboard and a controller, which are separate devices.
                frame is Frame.PadState -> {
                    out.removeAll { it is Frame.PadState }
                    out.add(frame)
                }

                frame is Frame.SetValue -> {
                    out.removeAll { it is Frame.SetValue && it.control == frame.control }
                    out.add(frame)
                }

                else -> {
                    out.add(frame)
                }
            }
        }
        return out
    }

    /**
     * MOVE, SCROLL and ZOOM carry i16 fields, so a merged run that would overflow one is clamped rather
     * than allowed to wrap: a fast flick must not come out as a jump in the opposite direction. The codec
     * would reject the frame anyway; clamping keeps the pointer moving the way the finger did.
     */
    private fun sum(
        a: Int,
        b: Int,
    ): Int = (a + b).coerceIn(Short.MIN_VALUE.toInt(), Short.MAX_VALUE.toInt())
}
