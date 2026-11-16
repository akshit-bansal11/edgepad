package me.akshitbansal.edgepad.link.xfer

import me.akshitbansal.edgepad.link.Transport
import me.akshitbansal.edgepad.link.blob.BlobSender
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.FrameCodec
import java.io.BufferedInputStream
import java.io.Closeable
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
 * It starts where the laptop says (READY's offset), hashing the bytes before that without sending them. A PROGRESS
 * that reports a gap sends it back to that offset: the file is reopened, rehashed up to there and sent on from it.
 * Pacing is [BlobSender]'s: each chunk is its own [write], a tick's budget by transport, the smaller busy budget while
 * the phone is sending input, and no more chunks while [backlog] is full.
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
    private var rewindTo = -1L
    private var lastProgress = -1L
    private var ending = false

    @Volatile private var sentTo = 0L

    /**
     * Sends [name] (opened through [open], [size] bytes) as transfer [id] (1 to 255). [XferCode.SAVED] when the laptop
     * has the file whole and checked; any other code is how it ended: the laptop's own answer, [XferCode.CANCELLED]
     * when [cancelled] turned true, [XferCode.TIMED_OUT] or [XferCode.SESSION_ENDED]; the laptop is told of the first
     * two, so it is not left holding the file. A file that cannot be read throws. [progress] takes the bytes sent so
     * far after each chunk.
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
                rewindTo = -1
                lastProgress = -1
                ending = false
                sentTo = 0
            }
            try {
                Reader(open).use { run(it, clean, size, id, cancelled, progress) }
            } finally {
                guard.withLock { sending = false }
            }
        }

    /**
     * The laptop's answer to the file being sent. A PROGRESS below what was sent is a gap when it repeats (every chunk
     * after a lost one is answered with the same offset) or when it answers the END; one that only trails the chunks
     * still in flight is not. False when it is not about that file, or not a known status.
     */
    fun take(status: Frame.XferStatus): Boolean =
        guard.withLock {
            val code = XferCode.of(status.status)
            if (!sending || status.id != activeId || code == null) return false
            when (code) {
                XferCode.READY -> {
                    readyAt = status.offset.toLong()
                }

                XferCode.PROGRESS -> {
                    val offset = status.offset.toLong()
                    if (offset < sentTo && (ending || offset == lastProgress)) rewindTo = offset
                    lastProgress = offset
                }

                else -> {
                    final = code
                }
            }
            changed.signalAll()
            true
        }

    private fun run(
        reader: Reader,
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
        if (!reader.seek(offset)) return XferCode.WRITE_FAILED
        val buffer = ByteArray(FrameCodec.MAX_XFER_CHUNK_BYTES)
        var sent = 0
        var at = offset
        while (true) {
            while (at < size) {
                await({ true }, id, at, cancelled)?.let { return it }
                val back = takeRewind()
                if (back >= 0) {
                    if (!reader.seek(back)) return XferCode.WRITE_FAILED
                    at = back
                    continue
                }
                val count = reader.read(buffer, minOf(buffer.size.toLong(), size - at).toInt())
                if (count <= 0) {
                    write(Frame.XferStatus(id, XferCode.CANCELLED.value, at.toULong()))
                    return XferCode.WRITE_FAILED
                }
                write(Frame.XferChunk(id, at.toULong(), buffer.copyOf(count)))
                at += count
                sentTo = at
                progress(at)
                if (++sent >= budget() && at < size) {
                    sent = 0
                    if (!tick(cancelled)) return XferCode.SESSION_ENDED
                }
            }
            guard.withLock { ending = true }
            write(Frame.XferEnd(id, reader.digest()))
            // Only the laptop's final answer, a gap it reports, or the timeout ends this wait.
            await({ rewindTo >= 0 }, id, size, cancelled)?.let { return it }
            at = takeRewind()
            guard.withLock { ending = false }
            if (!reader.seek(at)) return XferCode.WRITE_FAILED
        }
    }

    private fun takeRewind(): Long =
        guard.withLock {
            val back = rewindTo
            if (back >= 0) {
                rewindTo = -1
                sentTo = back
            }
            back
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
     * holds this waits for the laptop's final answer, or the timeout. A timeout, like a cancel, tells the laptop.
     */
    private fun await(
        ready: () -> Boolean,
        id: Int,
        at: Long,
        cancelled: () -> Boolean,
    ): XferCode? {
        val deadline = clock() + answerTimeoutMs
        val late =
            guard.withLock {
                var timedOut = false
                while (!timedOut && !cancelled()) {
                    final?.let { return it }
                    if (ended()) return XferCode.SESSION_ENDED
                    if (ready()) return null
                    timedOut = clock() >= deadline
                    if (!timedOut) changed.await(SLICE_MS, TimeUnit.MILLISECONDS)
                }
                timedOut
            }
        // Outside the lock: a queued write must not keep the reading thread from delivering statuses.
        write(Frame.XferStatus(id, XferCode.CANCELLED.value, at.toULong()))
        return if (late) XferCode.TIMED_OUT else XferCode.CANCELLED
    }

    private fun budget(): Int =
        when {
            clock() - lastInputMs() < BlobSender.INPUT_QUIET_MS -> BlobSender.BUSY_CHUNKS_PER_TICK
            transport == Transport.BLUETOOTH -> BlobSender.BLUETOOTH_CHUNKS_PER_TICK
            else -> BlobSender.NETWORK_CHUNKS_PER_TICK
        }

    // The file as the sender reads it. Going back reopens it and rehashes from the start.
    private class Reader(
        private val open: () -> InputStream,
    ) : Closeable {
        private var input = BufferedInputStream(open(), XferLimits.BUFFER_BYTES)
        private var sha = MessageDigest.getInstance("SHA-256")
        private var position = 0L

        fun read(
            buffer: ByteArray,
            count: Int,
        ): Int {
            val read = input.read(buffer, 0, count)
            if (read > 0) {
                sha.update(buffer, 0, read)
                position += read
            }
            return read
        }

        /** Positions at [to], hashing what is passed over. False when the file ends first. */
        fun seek(to: Long): Boolean {
            if (to < position) {
                input.close()
                input = BufferedInputStream(open(), XferLimits.BUFFER_BYTES)
                sha = MessageDigest.getInstance("SHA-256")
                position = 0
            }
            val skip = ByteArray(XferLimits.BUFFER_BYTES)
            while (position < to) {
                if (read(skip, minOf(skip.size.toLong(), to - position).toInt()) <= 0) return false
            }
            return true
        }

        fun digest(): ByteArray = sha.digest()

        override fun close() = input.close()
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
