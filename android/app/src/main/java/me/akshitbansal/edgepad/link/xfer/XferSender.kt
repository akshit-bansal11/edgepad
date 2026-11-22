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
    private val pausedChanged: (Boolean) -> Unit = {},
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
    private var rewoundAt = -1L
    private var ending = false

    // Pause, all under [guard]: this end's own hold, whether the laptop has been told of it, a RESUME still owed to it,
    // the laptop's hold, when either began (-1 when none), and a pause asked for between files (the next send is held).
    private var paused = false
    private var pausedSent = false
    private var resumeOwed = false
    private var peerPaused = false
    private var heldSince = -1L
    private var pausePending = false

    // The last state given to [pausedChanged]; read and delivered under [notify], so the owner sees changes in order.
    private val notify = Any()
    private var lastHeld = false

    @Volatile private var sentTo = 0L

    /**
     * Holds the file after the chunk in flight: the laptop is told with PAUSED and nothing more is sent, and the answer
     * timeout stops, until [resume]. Asked while no file is being sent it holds the next one. False when no file is
     * being sent, it is already held, or END has gone (the answer is then only waited for). Any thread; the frame
     * itself is written by the sending thread.
     */
    fun pause(): Boolean {
        val done =
            guard.withLock {
                if (!sending) {
                    pausePending = true
                    false
                } else if (ending || paused) {
                    false
                } else {
                    paused = true
                    touch()
                    changed.signalAll()
                    true
                }
            }
        notifyOwner()
        return done
    }

    /** Lets go of this end's hold: the laptop is told with RESUME and its READY names where to go on. */
    fun resume(): Boolean {
        val done =
            guard.withLock {
                if (!sending) {
                    pausePending = false
                    false
                } else if (!paused) {
                    false
                } else {
                    paused = false
                    resumeOwed = pausedSent
                    touch()
                    changed.signalAll()
                    true
                }
            }
        notifyOwner()
        return done
    }

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
                rewoundAt = -1
                ending = false
                sentTo = 0
                resetPause()
                if (pausePending) {
                    pausePending = false
                    paused = true
                    touch()
                }
            }
            notifyOwner()
            try {
                Reader(open).use { run(it, clean, size, id, cancelled, progress) }
            } finally {
                guard.withLock {
                    sending = false
                    resetPause()
                }
                notifyOwner()
            }
        }

    // Under the guard: when a hold began, or -1 when none holds.
    private fun touch() {
        heldSince = if (paused || peerPaused) (if (heldSince >= 0) heldSince else clock()) else -1
    }

    // Under the guard.
    private fun resetPause() {
        paused = false
        pausedSent = false
        resumeOwed = false
        peerPaused = false
        heldSince = -1
    }

    // Outside the guard: tells the owner when the transfer starts or stops being held (a pause waiting for the next
    // file counts). State is read and delivered under one lock, so the owner sees the changes in the order they happened.
    private fun notifyOwner() {
        synchronized(notify) {
            val held = guard.withLock { paused || peerPaused || pausePending }
            if (held == lastHeld) return
            lastHeld = held
            pausedChanged(held)
        }
    }

    /** Drops a pause that waits for a next file that is not coming: the batch is over. */
    fun clearPending() {
        guard.withLock { pausePending = false }
        notifyOwner()
    }

    /**
     * The laptop's answer to the file being sent. A PROGRESS below what was sent is a gap when it repeats (every chunk
     * after a lost one is answered with the same offset) or when it answers the END; one that only trails the chunks
     * still in flight is not. False when it is not about that file, or not a known status.
     */
    fun take(status: Frame.XferStatus): Boolean {
        val code = XferCode.of(status.status)
        if (code == XferCode.PAUSED || code == XferCode.RESUME) return takePause(code == XferCode.PAUSED, status.id)
        return guard.withLock {
            if (!sending || status.id != activeId || code == null) return false
            when (code) {
                XferCode.READY -> {
                    readyAt = status.offset.toLong()
                }

                XferCode.PROGRESS -> {
                    val offset = status.offset.toLong()
                    // Answers to chunks sent before a rewind are still in flight: they must not rewind it again.
                    if (offset <= rewoundAt) return true
                    rewoundAt = -1
                    if (offset < sentTo && (ending || offset == lastProgress)) {
                        rewindTo = offset
                        rewoundAt = offset
                        lastProgress = -1
                    } else {
                        lastProgress = offset
                    }
                }

                else -> {
                    final = code
                }
            }
            changed.signalAll()
            true
        }
    }

    // The laptop's PAUSED or RESUME for the file being sent.
    private fun takePause(
        holding: Boolean,
        id: Int,
    ): Boolean {
        val mine =
            guard.withLock {
                if (!sending || id != activeId) {
                    false
                } else {
                    peerPaused = holding
                    touch()
                    changed.signalAll()
                    true
                }
            }
        if (mine) notifyOwner()
        return mine
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
                // A hold is honoured here, between chunks and never inside one.
                (hold(id, at, cancelled) ?: await({ true }, id, at, cancelled))?.let { return it }
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
            // A gap reported on the last chunk is resent now, not after an END the laptop would only answer with it.
            val pending = takeRewind()
            if (pending >= 0) {
                if (!reader.seek(pending)) return XferCode.WRITE_FAILED
                at = pending
                continue
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

    /**
     * Null when no end holds the file, or when it was held and has been let go and the laptop's READY has named where
     * to go on (a READY below [at] is a rewind, taken like a lost chunk); otherwise how it ended. While held the answer
     * timeout does not run. A hold older than [XferLimits.PAUSE_CAP_MS] ends as [XferCode.CANCELLED] without telling
     * the laptop, which has the same cap and so keeps its part; a user's cancel tells it, as everywhere else.
     */
    private fun hold(
        id: Int,
        at: Long,
        cancelled: () -> Boolean,
    ): XferCode? {
        guard.withLock {
            if (!paused && !peerPaused && !resumeOwed) return null
            readyAt = -1
        }
        var cancel = false
        var release = false
        while (!cancel && !release) {
            var tell: Frame.XferStatus? = null
            guard.withLock {
                final?.let { return it }
                if (ended()) return XferCode.SESSION_ENDED
                if (cancelled()) {
                    cancel = true
                } else if (paused && !pausedSent) {
                    pausedSent = true
                    tell = Frame.XferStatus(id, XferCode.PAUSED.value, at.toULong())
                } else if (!paused && resumeOwed) {
                    resumeOwed = false
                    pausedSent = false
                    readyAt = -1
                    tell = Frame.XferStatus(id, XferCode.RESUME.value, at.toULong())
                } else if (!paused && !peerPaused) {
                    release = true
                } else if (heldSince >= 0 && clock() - heldSince >= XferLimits.PAUSE_CAP_MS) {
                    return XferCode.CANCELLED
                } else {
                    changed.await(HOLD_SLICE_MS, TimeUnit.MILLISECONDS)
                }
            }
            // Outside the lock: a queued write must not keep the reading thread from delivering statuses.
            tell?.let { write(it) }
        }
        // Cancelled: a cancel goes the way a cancel always does, and tells the laptop.
        if (cancel) return await({ true }, id, at, cancelled)
        await({ readyAt >= 0 }, id, at, cancelled)?.let { return it }
        guard.withLock {
            if (readyAt in 0 until at) {
                rewindTo = readyAt
                rewoundAt = readyAt
                lastProgress = -1
            }
        }
        return null
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
        var deadline = clock() + answerTimeoutMs
        val late =
            guard.withLock {
                var timedOut = false
                while (!timedOut && !cancelled()) {
                    final?.let { return it }
                    if (ended()) return XferCode.SESSION_ENDED
                    if (ready()) return null
                    // Held by either end: the wait starts over, until the hold has run out its cap. Past it the send
                    // ends CANCELLED without telling the laptop, whose CANCELLED would delete the part.
                    if (heldSince >= 0) {
                        if (clock() - heldSince >= XferLimits.PAUSE_CAP_MS) return XferCode.CANCELLED
                        deadline = clock() + answerTimeoutMs
                    }
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

        // A hold is woken by every change to it; the slice only bounds how late a cancel or a lost link is seen.
        const val HOLD_SLICE_MS = 250L
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
