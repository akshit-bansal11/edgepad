package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.Frame

/**
 * Collapses a backlog of queued frames before it is written. Consecutive MOVE, SCROLL and ZOOM frames are
 * summed, and only the last SET per control survives, so a link that falls behind the finger catches up in
 * one write instead of replaying every sample it missed. Everything else keeps its order.
 */
object Coalesce {
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

    private fun sum(
        a: Int,
        b: Int,
    ): Int = (a + b).coerceIn(Short.MIN_VALUE.toInt(), Short.MAX_VALUE.toInt())
}
