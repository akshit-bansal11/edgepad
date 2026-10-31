package me.akshitbansal.edgepad.link.blob

import me.akshitbansal.edgepad.protocol.Frame
import java.io.ByteArrayOutputStream
import java.security.MessageDigest

/**
 * Puts one blob back together from what a laptop sent, and trusts none of it. Nothing is allocated from the size a
 * BLOB_BEGIN claims: the buffer grows with the bytes that arrived and stops at that size, which [cap] has already
 * bounded, so at most one capped blob is held at a time.
 *
 * A blob that goes wrong is drained to its BLOB_END and dropped, and the next BLOB_BEGIN starts clean: the framing was
 * valid, so the link stays up. A refused blob answers false once and is quiet after that. [take] runs on the link's
 * reading thread and [reset] on whichever thread turned sharing off, hence the lock. Neither throws.
 *
 * @param cap The most bytes a kind may carry, or null for a kind this side does not take.
 * @param completed Handed the kind, the bytes and their checked SHA-256; false drops them.
 */
class BlobReceiver(
    private val cap: (Int) -> Long?,
    private val completed: (Int, ByteArray, ByteArray) -> Boolean,
) {
    private var buffer: ByteArrayOutputStream? = null
    private var draining = false
    private var kind = 0
    private var size = 0L
    private var seq = 0
    private var spent = 0L

    /** One blob frame. False for one that was refused, and for any other frame. */
    @Synchronized
    fun take(frame: Frame): Boolean =
        when (frame) {
            is Frame.BlobBegin -> begin(frame)
            is Frame.BlobChunk -> chunk(frame)
            is Frame.BlobEnd -> end(frame)
            else -> false
        }

    /** Forgets any blob in progress. What this connection has already spent stays spent. */
    @Synchronized
    fun reset() {
        buffer = null
        draining = false
    }

    // Starts a blob, aborting any still open: the sender gave up on that one.
    private fun begin(frame: Frame.BlobBegin): Boolean {
        reset()
        if (!admits(frame)) {
            draining = true
            return false
        }
        kind = frame.kind
        size = frame.size
        seq = 0
        buffer = ByteArrayOutputStream()
        return true
    }

    private fun admits(frame: Frame.BlobBegin): Boolean {
        val limit = cap(frame.kind) ?: return false
        return frame.size <= limit &&
            spent + frame.size <= MAX_CONNECTION_BYTES &&
            frame.label.none { it.isISOControl() }
    }

    private fun chunk(frame: Frame.BlobChunk): Boolean {
        spent += frame.data.size
        if (draining) return true
        val open = buffer ?: return false
        // An empty chunk is refused so the number of chunks is bounded by the size, not only their bytes.
        if (frame.data.isEmpty() || frame.seq != seq || open.size() + frame.data.size > size) {
            buffer = null
            draining = true
            return false
        }
        open.write(frame.data, 0, frame.data.size)
        seq = (seq + 1) and SEQ_MASK
        return true
    }

    private fun end(frame: Frame.BlobEnd): Boolean {
        val open = buffer
        val drained = draining
        reset()
        if (drained) return true
        if (open == null || open.size().toLong() != size) return false
        val data = open.toByteArray()
        return MessageDigest.isEqual(sha256(data), frame.sha256) && completed(kind, data, frame.sha256)
    }

    companion object {
        /** All the blob bytes one connection may send, kept or drained. Past it every blob is refused. */
        const val MAX_CONNECTION_BYTES = 256L * 1024 * 1024

        private const val SEQ_MASK = 0xFF
    }
}

/** True for the frames a link hands to its blob sink rather than to the screens. */
internal fun isBlob(frame: Frame): Boolean =
    frame is Frame.BlobBegin || frame is Frame.BlobChunk || frame is Frame.BlobEnd
