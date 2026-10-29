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

    /**
     * One point of a pen stroke on the annotate or sign surface. [x] and [y] span the whole 0..65535 range, 0
     * the laptop screen's left and top edge and 65535 its right and bottom, so no pixel count crosses the link.
     * [state] is 0 up, 1 down, 2 move. It is not range-checked here: a newer phone may add states, and the
     * laptop drops an unknown one and counts it rather than closing the link.
     */
    data class Pen(
        val x: Int,
        val y: Int,
        val state: Int,
    ) : Frame

    /**
     * The start of a file the laptop is sending: a capture, when the phone asked for captures to be saved here.
     * [type] is a [FileType] id, never a MIME string, [size] the whole file in bytes, [name] the laptop's file
     * name with no folder. Nothing here is trusted: the phone checks all three before it writes anything.
     */
    data class FileBegin(
        val type: Int,
        val size: Long,
        val name: String,
    ) : Frame

    /**
     * One piece of the file, 255 bytes at most. [seq] is the piece's index mod 256, so a lost or repeated piece
     * shows. Not a data class: an array compares by identity, and two chunks of the same bytes must be equal.
     */
    class FileChunk(
        val seq: Int,
        val data: ByteArray,
    ) : Frame {
        override fun equals(other: Any?): Boolean =
            other is FileChunk && other.seq == seq && other.data.contentEquals(data)

        override fun hashCode(): Int = 31 * seq + data.contentHashCode()

        override fun toString(): String = "FileChunk(seq=$seq, ${data.size} bytes)"
    }

    /** The end of the file: the SHA-256 of all of it, 32 bytes, which the phone checks before it keeps the file. */
    class FileEnd(
        val sha256: ByteArray,
    ) : Frame {
        override fun equals(other: Any?): Boolean = other is FileEnd && other.sha256.contentEquals(sha256)

        override fun hashCode(): Int = sha256.contentHashCode()

        override fun toString(): String = "FileEnd(${sha256.joinToString("") { "%02x".format(it) }})"
    }

    /**
     * The start of a blob: the clipboard, or an app list or icon, in either direction, on a lane of its own beside the
     * file one. FILE_BEGIN's shape. [kind] is a [BlobKind] id but is data here, not a codec error: the receiver reads
     * an unknown kind to its end and drops it. [size] is the whole blob in bytes and [label] a short UTF-8 name.
     */
    data class BlobBegin(
        val kind: Int,
        val size: Long,
        val label: String,
    ) : Frame

    /** One piece of the blob, 255 bytes at most. Not a data class, for the reason [FileChunk] is not. */
    class BlobChunk(
        val seq: Int,
        val data: ByteArray,
    ) : Frame {
        override fun equals(other: Any?): Boolean =
            other is BlobChunk && other.seq == seq && other.data.contentEquals(data)

        override fun hashCode(): Int = 31 * seq + data.contentHashCode()

        override fun toString(): String = "BlobChunk(seq=$seq, ${data.size} bytes)"
    }

    /** The end of the blob: the SHA-256 of all of it, 32 bytes. */
    class BlobEnd(
        val sha256: ByteArray,
    ) : Frame {
        override fun equals(other: Any?): Boolean = other is BlobEnd && other.sha256.contentEquals(sha256)

        override fun hashCode(): Int = sha256.contentHashCode()

        override fun toString(): String = "BlobEnd(${sha256.joinToString("") { "%02x".format(it) }})"
    }

    /** The phone's answer to a file, one [FileStatus] id. Only [FileStatus.SAVED] lets the laptop delete its copy. */
    data class FileResult(
        val status: Int,
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
