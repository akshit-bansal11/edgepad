package me.akshitbansal.edgepad.link.xfer

import me.akshitbansal.edgepad.link.Transport
import me.akshitbansal.edgepad.link.blob.BlobSender
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.FrameCodec
import java.io.BufferedInputStream
import java.io.InputStream
import java.security.MessageDigest
import java.util.concurrent.TimeUnit
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

/**
 * Sends one file to the laptop as XFER_BEGIN, XFER_CHUNKs and XFER_END, and blocks its caller until the laptop has
 * answered, so it is never called on the main thread or the link's reading thread. The statuses that answer come in
 * through [take] on the reading thread. One per link: a second call waits for the first.
 *
 * It starts where the laptop says (READY's offset), hashing the bytes before that without sending them. Pacing is
 * [BlobSender]'s: each chunk is its own [write], a tick's budget by transport, the smaller busy budget while the phone
 * is sending input, and no more chunks while [backlog] is full.
 *
 * @param write Takes one frame; a throw ends the transfer and reaches the caller.
 * @param ended True once the link is gone.
 * @param pause Waits that long, and answers false when the link ended. For tests.
 */
class XferSender(
    private val write: (Frame) -> Unit,
    private val transport: Transport,
    private val lastInputMs: () -> Long,
    private val ended: () -> Boolean,
    private val answerTimeoutMs: Long = XferLimits.ANSWER_TIMEOUT_MS,
    private val clock: () -> Long = { System.nanoTime() / NANOS_PER_MS },
    private val pause: (Int) -> Boolean = { ms -> !ended() && sleep(ms) },
    private val backlog: () -> Int = { 0 },
) {
    private val lane = Any()
    private val guard = ReentrantLock()
    private val changed = guard.newCondition()

    private var sending = false
    private var activeId = 0
    private var readyAt = -1L
    private var final: XferCode? = null

    /**
     * Sends [name] (opened through [open], [size] bytes) as transfer [id] (1 to 255). [XferCode.SAVED] when the laptop
     * has the file whole and checked; any other code is how it ended: the laptop's own answer, [XferCode.CANCELLED]
     * when [cancelled] turned true (the laptop is told), [XferCode.TIMED_OUT] or [XferCode.SESSION_ENDED]. A file that
     * cannot be read throws. [progress] takes the bytes sent so far after each chunk.
     */
    fun send(
        open: () -> InputStream,
        name: String,
        size: Long,
        id: Int,
        cancelled: () -> Boolean = { false },
        progress: (Long) -> Unit = {},
    ): XferCode =
        synchronized(lane) {
            val clean = SafeName.clean(name)
            if (clean == null || size > XferLimits.MAX_BYTES) return XferCode.REJECTED
            guard.withLock {
                activeId = id
                sending = true
                readyAt = -1
                final = null
            }
            try {
                BufferedInputStream(open(), XferLimits.BUFFER_BYTES).use {
                    run(it, clean, size, id, cancelled, progress)
                }
            } finally {
                guard.withLock { sending = false }
            }
        }

    /** The laptop's answer to the file being sent. False when it is not about that file, or not a known status. */
    fun take(status: Frame.XferStatus): Boolean =
        guard.withLock {
            val code = XferCode.of(status.status)
            if (!sending || status.id != activeId || code == null) return false
            when (code) {
                XferCode.READY -> readyAt = status.offset.toLong()
                XferCode.PROGRESS -> Unit
                else -> final = code
            }
            changed.signalAll()
            true
        }

    private fun run(
        input: InputStream,
        name: String,
        size: Long,
        id: Int,
        cancelled: () -> Boolean,
        progress: (Long) -> Unit,
    ): XferCode {
        write(Frame.XferBegin(id, size.toULong(), name))
        await({ readyAt >= 0 }, id, 0, cancelled)?.let { return it }
        val offset = readyAt
        if (offset > size) {
            write(Frame.XferStatus(id, XferCode.CANCELLED.value, 0uL))
            return XferCode.REJECTED
        }
        val digest = MessageDigest.getInstance("SHA-256")
        val skipBuffer = ByteArray(XferLimits.BUFFER_BYTES)
        var skipped = 0L
        while (skipped < offset) {
            val read = input.read(skipBuffer, 0, minOf(skipBuffer.size.toLong(), offset - skipped).toInt())
            if (read <= 0) return XferCode.WRITE_FAILED
            digest.update(skipBuffer, 0, read)
            skipped += read
        }
        val buffer = ByteArray(FrameCodec.MAX_XFER_CHUNK_BYTES)
        var sent = 0
        var at = offset
        while (at < size) {
            await({ true }, id, at, cancelled)?.let { return it }
            val count = input.read(buffer, 0, minOf(buffer.size.toLong(), size - at).toInt())
            if (count <= 0) {
                write(Frame.XferStatus(id, XferCode.CANCELLED.value, at.toULong()))
                return XferCode.WRITE_FAILED
            }
            digest.update(buffer, 0, count)
            write(Frame.XferChunk(id, at.toULong(), buffer.copyOf(count)))
            at += count
            progress(at)
            if (++sent >= budget() && at < size) {
                sent = 0
                if (!tick(cancelled)) return XferCode.SESSION_ENDED
            }
        }
        write(Frame.XferEnd(id, digest.digest()))
        return await({ false }, id, size, cancelled) ?: XferCode.TIMED_OUT
    }

    // One tick, and more while the writer is behind. A cancelled transfer stops waiting and its next wait ends it.
    private fun tick(cancelled: () -> Boolean): Boolean {
        do {
            if (!pause(BlobSender.TICK_MS)) return false
        } while (backlog() >= BlobSender.MAX_BACKLOG && !cancelled())
        return true
    }

    /**
     * Null when [ready] holds and nothing has ended the transfer; otherwise how it ended. With a condition that never
     * holds this waits for the laptop's final answer, or the timeout.
     */
    private fun await(
        ready: () -> Boolean,
        id: Int,
        at: Long,
        cancelled: () -> Boolean,
    ): XferCode? {
        val deadline = clock() + answerTimeoutMs
        guard.withLock {
            while (!cancelled()) {
                final?.let { return it }
                if (ended()) return XferCode.SESSION_ENDED
                if (ready()) return null
                if (clock() >= deadline) return XferCode.TIMED_OUT
                changed.await(SLICE_MS, TimeUnit.MILLISECONDS)
            }
        }
        // Outside the lock: a queued write must not keep the reading thread from delivering statuses.
        write(Frame.XferStatus(id, XferCode.CANCELLED.value, at.toULong()))
        return XferCode.CANCELLED
    }

    private fun budget(): Int =
        when {
            clock() - lastInputMs() < BlobSender.INPUT_QUIET_MS -> BlobSender.BUSY_CHUNKS_PER_TICK
            transport == Transport.BLUETOOTH -> BlobSender.BLUETOOTH_CHUNKS_PER_TICK
            else -> BlobSender.NETWORK_CHUNKS_PER_TICK
        }

    private companion object {
        const val SLICE_MS = 50L
        const val NANOS_PER_MS = 1_000_000L

        fun sleep(ms: Int): Boolean =
            try {
                Thread.sleep(ms.toLong())
                true
            } catch (e: InterruptedException) {
                false
            }
    }
}
