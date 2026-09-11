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

    /** Laptop to phone. Kind 0 is what is playing, kind 1 the app playing it. At most 255 UTF-8 bytes. */
    data class Text(
        val kind: Int,
        val text: String,
    ) : Frame
}
