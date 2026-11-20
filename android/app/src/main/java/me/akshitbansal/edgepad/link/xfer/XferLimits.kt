package me.akshitbansal.edgepad.link.xfer

/** The wire status of an XFER_STATUS frame. [TIMED_OUT] and [SESSION_ENDED] are local answers, never sent. */
enum class XferCode(
    val value: Int,
) {
    READY(0),
    PROGRESS(1),
    SAVED(2),
    BAD_HASH(3),
    REJECTED(4),
    WRITE_FAILED(5),
    CANCELLED(6),
    BUSY(7),
    TIMED_OUT(8),
    SESSION_ENDED(9),
    ;

    companion object {
        /** The code a status number names on the wire, or null for one this build does not know. */
        fun of(value: Int): XferCode? = entries.firstOrNull { it.value == value && it.value <= BUSY.value }
    }
}

/** The numbers both ends of a transfer hold to. D-XFER in the 5.0.0 plan; the same as the laptop's TransferLimits. */
object XferLimits {
    const val MAX_BYTES = 2L * 1024 * 1024 * 1024
    const val FREE_SPACE_MARGIN = 64L * 1024 * 1024
    const val PROGRESS_EVERY_BYTES = 256L * 1024
    const val PARTIAL_KEPT_MS = 7L * 24 * 60 * 60 * 1000
    const val ANSWER_TIMEOUT_MS = 30_000L
    const val IDLE_MS = 30_000L
    const val MAX_NAME_BYTES = 120
    const val MAX_EXTENSION_BYTES = 20
    const val PARTIAL_EXTENSION = ".part"

    /** The size of each of the two buffers a transfer holds. */
    const val BUFFER_BYTES = 64 * 1024
}
