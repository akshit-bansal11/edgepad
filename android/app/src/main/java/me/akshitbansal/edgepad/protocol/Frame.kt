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

    /** Text, of a [TextKind]: what is playing, the app, the timeline, or characters to type. At most 255 UTF-8 bytes. */
    data class Text(
        val kind: Int,
        val text: String,
    ) : Frame

    /** One keyboard key, by Windows virtual-key code (e.g. 0x41 = A, 0x10 = SHIFT). */
    data class Key(
        val code: Int,
        val down: Boolean,
    ) : Frame

    /**
     * The whole controller in one frame: every button, both triggers and both sticks together, sent when any
     * of them changes, with the laptop holding the last one until the next arrives. One frame rather than a
     * frame per control because a gamepad is read as a snapshot, not as a stream of edges — a dropped button
     * release would otherwise stick a button down until the user pressed it again.
     *
     * The field order and widths are deliberately byte-for-byte those of Windows' `XINPUT_GAMEPAD`
     * (`wButtons`, `bLeftTrigger`, `bRightTrigger`, `sThumbLX`, `sThumbLY`, `sThumbRX`, `sThumbRY`), so the
     * laptop copies the payload into the struct instead of translating it. Anything this side invented —
     * a different stick range, a button order of our own — would be a conversion on the hot path and a
     * second definition to keep in step with Microsoft's.
     *
     * [buttons] is XInput's `wButtons` mask, see [PadButton]. [lt] and [rt] are 0..255. The four stick axes
     * are -32768..32767, positive up and right, exactly as XInput reports them.
     */
    data class PadState(
        val buttons: Int,
        val lt: Int,
        val rt: Int,
        val lx: Int,
        val ly: Int,
        val rx: Int,
        val ry: Int,
    ) : Frame

    /**
     * The laptop asking for the PIN, or refusing the phone for a while. Sent only between the trust question
     * and HELLO_ACK. [lockedSeconds] is 0 for a question and the wait for a lock-out, in which case
     * [attemptsLeft] is 0 and the laptop closes the link after sending it.
     */
    data class PinRequest(
        val attemptsLeft: Int,
        val lockedSeconds: Int,
    ) : Frame

    /** The phone's answer to [PinRequest]. [Pin] keeps the digits out of any log line this frame reaches. */
    data class PinAnswer(
        val pin: Pin,
    ) : Frame
}

/**
 * Six decimal digits, the one secret that crosses the link. Frames are logged (`LaptopLink` prints the frame
 * it did not expect), so every way of printing this class hides the digits: [toString] is the only one a
 * data class or a string template reaches, and it returns stars. Equality compares the digits, which is what
 * a test needs; nothing here exposes them as text.
 */
class Pin(
    private val digits: String,
) {
    init {
        require(digits.length == LENGTH && digits.all { it in '0'..'9' }) { "A PIN is exactly six digits" }
    }

    /** The digit at [index] as a value 0..9, which is what goes on the wire (a value, not an ASCII code). */
    fun digitAt(index: Int): Int = digits[index] - '0'

    override fun equals(other: Any?): Boolean = other is Pin && other.digits == digits

    override fun hashCode(): Int = digits.hashCode()

    override fun toString(): String = "******"

    companion object {
        const val LENGTH = 6
    }
}
