package me.akshitbansal.edgepad.link.blob

import me.akshitbansal.edgepad.link.Transport
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.FrameCodec
import java.security.MessageDigest

/**
 * Sends blobs to the laptop as BLOB_BEGIN, BLOB_CHUNKs and BLOB_END, one at a time, because the far end holds one
 * transfer and a second BEGIN would abort the first. One per link. It blocks its caller for the whole transfer, so it
 * is never called on the main thread or the link's reading thread.
 *
 * Paced as the laptop paces a capture: each chunk is its own [write], so input gets between any two, and a tick's
 * budget drops to [BUSY_CHUNKS_PER_TICK] while the phone is sending input. Unlike a laptop's send, this [write] only
 * queues and a full queue ends the link, so the pace alone is not enough: a Bluetooth link slower than the budget would
 * fill the queue during an image. [backlog] is the frames still queued, and no more chunks go while it is [MAX_BACKLOG]
 * or over.
 *
 * @param write Takes one frame; a throw ends the transfer and reaches the caller.
 * @param transport What carries the link, which sets the budget.
 * @param lastInputMs When this link last sent input, on [clock].
 * @param ended True once the link is gone.
 * @param clock Milliseconds, for tests.
 * @param pause Waits that long, and answers false when the link ended. For tests.
 * @param backlog The frames [write] has queued and not written yet.
 */
class BlobSender(
    private val write: (Frame) -> Unit,
    private val transport: Transport,
    private val lastInputMs: () -> Long,
    ended: () -> Boolean,
    private val clock: () -> Long = { System.nanoTime() / NANOS_PER_MS },
    private val pause: (Int) -> Boolean = { ms -> pauseUnless(ended, ms) },
    private val backlog: () -> Int = { 0 },
) {
    /**
     * Sends one blob. False when the link ended or [abandoned] said so part way: no BLOB_END goes, and the next
     * BLOB_BEGIN clears what the far end was holding.
     */
    @Synchronized
    fun send(
        kind: BlobKind,
        label: String,
        data: ByteArray,
        abandoned: () -> Boolean = { false },
    ): Boolean {
        write(Frame.BlobBegin(kind.id, data.size.toLong(), label))
        var seq = 0
        var sent = 0
        for (at in data.indices step FrameCodec.MAX_CHUNK_BYTES) {
            if (abandoned()) return false
            val next = at + FrameCodec.MAX_CHUNK_BYTES
            write(Frame.BlobChunk(seq, data.copyOfRange(at, minOf(next, data.size))))
            seq = (seq + 1) and SEQ_MASK
            if (++sent >= budget() && next < data.size) {
                sent = 0
                if (!tick(abandoned)) return false
            }
        }
        write(Frame.BlobEnd(sha256(data)))
        return true
    }

    // One tick, and more while the writer is behind. An abandoned transfer stops waiting and its next chunk stops it.
    private fun tick(abandoned: () -> Boolean): Boolean {
        do {
            if (!pause(TICK_MS)) return false
        } while (backlog() >= MAX_BACKLOG && !abandoned())
        return true
    }

    private fun budget(): Int =
        when {
            clock() - lastInputMs() < INPUT_QUIET_MS -> BUSY_CHUNKS_PER_TICK
            transport == Transport.BLUETOOTH -> BLUETOOTH_CHUNKS_PER_TICK
            else -> NETWORK_CHUNKS_PER_TICK
        }

    companion object {
        const val TICK_MS = 20
        const val BLUETOOTH_CHUNKS_PER_TICK = 16
        const val NETWORK_CHUNKS_PER_TICK = 128
        const val BUSY_CHUNKS_PER_TICK = 2
        const val INPUT_QUIET_MS = 250L

        /** One Bluetooth tick's worth: a key press queued behind this goes out soon after a working link catches up. */
        const val MAX_BACKLOG = BLUETOOTH_CHUNKS_PER_TICK

        private const val NANOS_PER_MS = 1_000_000L
        private const val SEQ_MASK = 0xFF
    }
}

/** The SHA-256 a BLOB_END carries. */
internal fun sha256(data: ByteArray): ByteArray = MessageDigest.getInstance("SHA-256").digest(data)

private fun pauseUnless(
    ended: () -> Boolean,
    ms: Int,
): Boolean {
    if (ended()) return false
    return try {
        Thread.sleep(ms.toLong())
        !ended()
    } catch (e: InterruptedException) {
        false
    }
}
