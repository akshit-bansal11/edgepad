package me.akshitbansal.edgepad.link.xfer

import java.util.concurrent.CopyOnWriteArrayList

/** Why one file of a batch did not arrive. The last four are local: the laptop never sends them. */
enum class XferFailure {
    REFUSED,
    DAMAGED,
    WRITE_FAILED,
    BUSY,
    TIMED_OUT,
    LINK_LOST,
    UNREADABLE,
    TOO_BIG,
    CANCELLED,
    NOT_SENT,
}

/** How a transfer that ended with [code] failed, or null when it was saved. READY and PROGRESS are never final. */
fun failureOf(code: XferCode): XferFailure? =
    when (code) {
        XferCode.SAVED -> null
        XferCode.REJECTED -> XferFailure.REFUSED
        XferCode.BAD_HASH -> XferFailure.DAMAGED
        XferCode.WRITE_FAILED -> XferFailure.WRITE_FAILED
        XferCode.CANCELLED -> XferFailure.CANCELLED
        XferCode.BUSY -> XferFailure.BUSY
        XferCode.TIMED_OUT -> XferFailure.TIMED_OUT
        XferCode.SESSION_ENDED -> XferFailure.LINK_LOST
        XferCode.READY, XferCode.PROGRESS, XferCode.PAUSED, XferCode.RESUME -> XferFailure.NOT_SENT
    }

/** Whether a batch goes on to its next file after one failed for [failure]: only a fault of that one file does. */
fun continuesAfter(failure: XferFailure): Boolean =
    when (failure) {
        XferFailure.REFUSED,
        XferFailure.DAMAGED,
        XferFailure.WRITE_FAILED,
        XferFailure.UNREADABLE,
        XferFailure.TOO_BIG,
        -> true

        XferFailure.BUSY,
        XferFailure.TIMED_OUT,
        XferFailure.LINK_LOST,
        XferFailure.CANCELLED,
        XferFailure.NOT_SENT,
        -> false
    }

/** A file of a batch that did not arrive, and why. */
data class XferFailed(
    val name: String,
    val why: XferFailure,
)

/** Whether a batch has not started, is moving, or has ended, however it ended. */
enum class XferPhase { IDLE, RUNNING, FINISHED }

/**
 * Where a batch of transfers stands, as the card shows it. Immutable; one is made per publish. [index] counts from 1
 * and is 0 before the first file; [total] is 0 when unknown (a receiving batch: the laptop never says how many).
 */
data class XferSnapshot(
    val phase: XferPhase,
    val sending: Boolean,
    val index: Int,
    val total: Int,
    val name: String,
    val sent: Long,
    val size: Long,
    val bytesPerSecond: Long,
    val done: Int,
    val failed: List<XferFailed>,
    val cancelling: Boolean,
    /** Either end holds the current file (or a hold waits for the next one). */
    val paused: Boolean = false,
) {
    /** The current file's percent; an empty file is whole. */
    val percent: Int get() = if (size <= 0) 100 else (sent * 100 / size).toInt().coerceIn(0, 100)

    companion object {
        val IDLE = XferSnapshot(XferPhase.IDLE, false, 0, 0, "", 0, 0, 0, 0, emptyList(), false)
    }
}

/**
 * Bytes a second, from the last two samples at least [SAMPLE_MS] apart; a sample sooner than that is ignored, so a
 * burst of chunks does not swing the figure. Not thread-safe: [XferProgress] holds its lock around it.
 */
class SpeedMeter(
    private val clock: () -> Long,
) {
    private var lastAt = -1L
    private var lastBytes = 0L

    /** The last rate measured, 0 before two samples. */
    var bytesPerSecond = 0L
        private set

    /** Takes [totalBytes] moved so far in the current file, at [now]. */
    fun add(
        totalBytes: Long,
        now: Long = clock(),
    ) {
        if (lastAt < 0) {
            lastAt = now
            lastBytes = totalBytes
            return
        }
        val elapsed = now - lastAt
        if (elapsed < SAMPLE_MS) return
        bytesPerSecond = (totalBytes - lastBytes).coerceAtLeast(0) * 1000 / elapsed
        lastAt = now
        lastBytes = totalBytes
    }

    fun reset() {
        lastAt = -1L
        lastBytes = 0L
        bytesPerSecond = 0L
    }
}

/**
 * The progress of the one batch of transfers running, for the card. Any thread may feed it; every listener is called on
 * the thread [post] runs work on (the main one), at most every [SAMPLE_MS] while bytes move and at once when the state
 * changes. No timer or thread of its own, and no post at all while no listener is added. Pure: no Android type, so a
 * JVM test runs it.
 */
class XferProgress(
    private val clock: () -> Long = { System.nanoTime() / 1_000_000 },
    private val post: (() -> Unit) -> Unit,
) {
    private val lock = Any()
    private val speed = SpeedMeter(clock)
    private var state = XferSnapshot.IDLE

    // Kept apart from [state] so a chunk costs no allocation.
    private var sent = 0L
    private var lastPublish = Long.MIN_VALUE / 2

    // Copy on write: a listener removed while the others are told is safe, and each activity owns only its own.
    private val listeners = CopyOnWriteArrayList<(XferSnapshot) -> Unit>()

    /** Adds [heard]; adding the same one again keeps one. */
    fun addListener(heard: (XferSnapshot) -> Unit) {
        if (listeners.none { it === heard }) listeners += heard
    }

    /** Removes [heard] alone, by identity; the others stay. */
    fun removeListener(heard: (XferSnapshot) -> Unit) {
        listeners.removeIf { it === heard }
    }

    /** The state now. Safe on any thread. */
    val snapshot: XferSnapshot get() = synchronized(lock) { view() }

    fun beginBatch(
        sending: Boolean,
        total: Int,
    ) {
        change {
            speed.reset()
            sent = 0
            XferSnapshot.IDLE.copy(phase = XferPhase.RUNNING, sending = sending, total = total)
        }
    }

    /** Grows a running batch by [more] files. A batch of unknown size stays unknown. */
    fun addToBatch(more: Int) = change { if (it.total > 0) it.copy(total = it.total + more) else it }

    fun startFile(
        name: String,
        size: Long,
    ) {
        change {
            speed.reset()
            sent = 0
            it.copy(index = it.index + 1, name = name, size = size)
        }
    }

    /** Takes the bytes of the current file moved so far: one clock read, and nothing more unless a publish is due. */
    fun bytes(sent: Long) {
        synchronized(lock) {
            this.sent = sent
            val now = clock()
            if (now - lastPublish < SAMPLE_MS) return
            speed.add(sent, now)
            publish(now)
        }
    }

    /** The end of file [name]: arrived when [failure] is null. */
    fun fileDone(
        name: String,
        failure: XferFailure?,
    ) {
        change {
            if (failure == null) {
                it.copy(done = it.done + 1)
            } else {
                it.copy(failed = it.failed + XferFailed(name, failure))
            }
        }
    }

    fun requestCancel() = change { if (it.phase == XferPhase.RUNNING) it.copy(cancelling = true) else it }

    /** Whether either end holds the transfer now. Called in the order the holds changed; only a running batch shows it. */
    fun setPaused(held: Boolean) = change { if (it.phase == XferPhase.RUNNING) it.copy(paused = held) else it }

    fun endBatch() =
        change { if (it.phase == XferPhase.RUNNING) it.copy(phase = XferPhase.FINISHED, paused = false) else it }

    /** Whether the card shows a send batch that ended with failures, which a receive must not replace. */
    fun holdsFailedSend(): Boolean =
        synchronized(lock) { state.phase == XferPhase.FINISHED && state.sending && state.failed.isNotEmpty() }

    /** The card was closed: back to nothing. */
    fun dismiss() {
        change {
            speed.reset()
            sent = 0
            XferSnapshot.IDLE
        }
    }

    private fun change(next: (XferSnapshot) -> XferSnapshot) {
        synchronized(lock) {
            state = next(state)
            publish(clock())
        }
    }

    // Under the lock.
    private fun publish(now: Long) {
        lastPublish = now
        if (listeners.isEmpty()) return
        val shown = view()
        post { for (heard in listeners) heard(shown) }
    }

    private fun view() = state.copy(sent = sent, bytesPerSecond = speed.bytesPerSecond)
}

/** The least time between two publishes, and between two speed samples. */
private const val SAMPLE_MS = 250L
