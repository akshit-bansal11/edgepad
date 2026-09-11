package me.akshitbansal.edgepad.protocol

/** One message on the wire. Type bytes and payload layouts live in [FrameCodec]. */
sealed interface Frame {
    data class Hello(
        val version: Int,
    ) : Frame

    data class HelloAck(
        val version: Int,
    ) : Frame

    data class Move(
        val dx: Int,
        val dy: Int,
    ) : Frame

    data class PointerButton(
        val id: Int,
        val down: Boolean,
    ) : Frame

    data class Scroll(
        val dx: Int,
        val dy: Int,
    ) : Frame

    data class Zoom(
        val delta: Int,
    ) : Frame

    data class RunAction(
        val id: Int,
    ) : Frame

    data class SetValue(
        val control: Int,
        val value: Int,
    ) : Frame

    data class Ping(
        val time: Long,
    ) : Frame

    data class Pong(
        val time: Long,
    ) : Frame

    data class StateReport(
        val control: Int,
        val value: Int,
        val flags: Int,
    ) : Frame
}
