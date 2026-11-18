package me.akshitbansal.edgepad.link.xfer

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.provider.OpenableColumns
import me.akshitbansal.edgepad.link.Laptop
import me.akshitbansal.edgepad.link.LinkHooks
import me.akshitbansal.edgepad.link.ManagedLink
import me.akshitbansal.edgepad.link.Target
import me.akshitbansal.edgepad.link.Transport
import me.akshitbansal.edgepad.link.net.LinkKeys
import me.akshitbansal.edgepad.protocol.Frame
import java.io.IOException
import java.util.IdentityHashMap
import java.util.concurrent.atomic.AtomicInteger
import kotlin.concurrent.thread

/**
 * The file transfers of every connected link, process-wide so a transfer outlives the activity that began it. One
 * [Link] a connected laptop: the receiving half is built on the first XFER frame, on the link's reading thread, and
 * the sending half does its work on the thread of whoever calls [Link.sendAll], or on the one [Link.enqueue] starts.
 * Frames from the second socket take the same road as the control link's. Nothing here runs while no transfer
 * does: no timer, no thread of its own. Main thread only for [attach] and [link].
 */
object Transfers {
    private val links = IdentityHashMap<ManagedLink, Link>()
    private val main = Handler(Looper.getMainLooper())

    /** The one batch running, in either direction, for the card. */
    val progress = XferProgress(post = { work -> main.post(work) })

    // A receiving batch: the laptop never says how many files come, so it ends a while after the last one.
    private val receiveLock = Any()
    private var receiveOpen = false
    private var receiveName: String? = null
    private var receiveArmedAt = 0L
    private val sendBatches = AtomicInteger()
    private val endReceive = Runnable { closeReceive(XferFailure.TIMED_OUT) }

    /** Brings the links in line with the laptops: a link that is gone is let go, a new connected one is taken. */
    fun attach(
        context: Context,
        laptops: List<Laptop>,
    ) {
        val app = context.applicationContext
        val live = laptops.filter { it.connected && it.link != null }
        val iterator = links.entries.iterator()
        while (iterator.hasNext()) {
            val held = iterator.next()
            if (live.none { it.link === held.key }) {
                held.value.end()
                held.key.xfer = null
                iterator.remove()
            }
        }
        for (laptop in live) {
            val managed = laptop.link ?: continue
            if (links.containsKey(managed)) continue
            val made = Link(app, managed, laptop.kind, laptop.target as? Target.Network, notice(app))
            managed.xfer = made::take
            links[managed] = made
        }
    }

    /** The transfers of [laptop]'s current link, or null when it has none. */
    fun link(laptop: Laptop): Link? = laptop.link?.let { links[it] }

    /** The Cancel of the notification: stops whatever is moving, in either direction. */
    fun cancel() {
        for (link in links.values.toList()) link.cancel()
    }

    /** The file name a picked document goes by, or null when its provider gives none. Does I/O: not the main thread. */
    fun displayName(
        context: Context,
        uri: Uri,
    ): String? = query(context, uri)?.first

    private fun query(
        context: Context,
        uri: Uri,
    ): Pair<String, Long>? {
        val columns = arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE)
        context.contentResolver.query(uri, columns, null, null, null)?.use { cursor ->
            if (!cursor.moveToFirst()) return null
            val name = cursor.getString(0) ?: uri.lastPathSegment ?: return null
            return if (cursor.isNull(1)) null else name to cursor.getLong(1)
        }
        return null
    }

    private fun notice(app: Context): XferNotification {
        val cancel =
            PendingIntent.getBroadcast(
                app,
                0,
                Intent(app, XferCancelReceiver::class.java),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
        return XferNotification(app, cancel)
    }

    // A send batch took the card: a receive running alongside stops feeding it.
    private fun sendBegan() {
        sendBatches.incrementAndGet()
        synchronized(receiveLock) {
            receiveOpen = false
            receiveName = null
            main.removeCallbacks(endReceive)
        }
    }

    private fun sendEnded() {
        sendBatches.decrementAndGet()
    }

    // A chunk of [name] arrived. Re-arms the stall check at most once a second, so a chunk costs no post.
    private fun receiving(
        name: String,
        held: Long,
        size: Long,
    ) {
        if (sendBatches.get() > 0) return
        synchronized(receiveLock) {
            if (!receiveOpen) {
                receiveOpen = true
                progress.beginBatch(sending = false, total = 0)
            }
            val now = SystemClock.uptimeMillis()
            if (name != receiveName) {
                receiveName = name
                progress.startFile(name, size)
                arm(now, XferLimits.IDLE_MS + RECEIVE_END_MS)
            } else if (now - receiveArmedAt >= REARM_MS) {
                arm(now, XferLimits.IDLE_MS + RECEIVE_END_MS)
            }
        }
        progress.bytes(held)
    }

    private fun received(name: String) {
        synchronized(receiveLock) {
            if (!receiveOpen) return
            receiveName = null
            progress.fileDone(name, null)
            arm(SystemClock.uptimeMillis(), RECEIVE_END_MS)
        }
    }

    // Under receiveLock.
    private fun arm(
        now: Long,
        delayMs: Long,
    ) {
        receiveArmedAt = now
        main.removeCallbacks(endReceive)
        main.postDelayed(endReceive, delayMs)
    }

    // Ends the receiving batch; a file still arriving is marked [failure].
    private fun closeReceive(failure: XferFailure) {
        synchronized(receiveLock) {
            if (!receiveOpen) return
            receiveName?.let { progress.fileDone(it, failure) }
            receiveOpen = false
            receiveName = null
            main.removeCallbacks(endReceive)
            progress.endBatch()
        }
    }

    /** One link's two directions. */
    class Link internal constructor(
        private val app: Context,
        private val link: ManagedLink,
        private val transport: Transport,
        private val network: Target.Network?,
        private val notice: XferNotification,
    ) {
        @Volatile private var bulk: ((Frame) -> Unit)? = null

        // The receiver's idle check: one delayed post while a file is arriving, none otherwise.
        private val idle = Handler(Looper.getMainLooper())

        private var nextId = 0

        private val batcher =
            XferBatcher<Uri>(
                progress = progress,
                sharing = LinkHooks.sharing,
                describe = { uri -> query(app, uri) },
                label = ::labelOf,
                sendOne = ::sendOne,
                began = ::sendBegan,
                finished = ::finished,
            )

        private val sender =
            XferSender(
                write = { frame -> (bulk ?: link::send)(frame) },
                transport = transport,
                lastInputMs = link::lastInputMs,
                ended = { !link.connected },
                backlog = link::queued,
            )

        // Built on the first XFER frame: its start purges old parts, which is file work the main thread must not do.
        private val receiverOnce =
            lazy {
                XferReceiver(
                    store = AndroidXferStore(app),
                    reply = { link.send(it) },
                    arrived = { name ->
                        notice.finish(name, true)
                        received(name)
                    },
                    progress = { name, held, size ->
                        notice.progress(name, held, size, false)
                        receiving(name, held, size)
                    },
                    after = { delayMs, work ->
                        val task = Runnable(work)
                        idle.postDelayed(task, delayMs)
                        val stop: () -> Unit = { idle.removeCallbacks(task) }
                        stop
                    },
                )
            }
        private val receiver by receiverOnce

        /** Whether files are going out now. */
        val sending: Boolean get() = batcher.busy

        /** The files of the last batch that did not arrive, failed or not sent, for Try again. */
        val failedUris: List<Uri> get() = batcher.failed

        /** Takes an XFER frame on the link's reading thread: the laptop's answer to a file sent, else a file arriving. */
        internal fun take(frame: Frame) {
            routeXfer(frame, sender) { receiver }
        }

        internal fun end() {
            cancel()
        }

        internal fun cancel() {
            batcher.cancel()
            if (receiverOnce.isInitialized() && receiver.busy && receiver.cancel()) closeReceive(XferFailure.CANCELLED)
        }

        /**
         * Sends [uris] one after another and blocks until the last is answered, so never on the main thread. The
         * second socket carries the chunks when the laptop is on the network and lets it; otherwise, or when it
         * breaks, they go on the control link. Every file BUSY when a batch is already going out.
         */
        fun sendAll(uris: List<Uri>): XferBatchResult =
            batcher.run(uris)
                ?: XferBatchResult(0, uris.map { XferFailed(labelOf(it), XferFailure.BUSY) })

        /** Sends [uris] on a worker of its own, or adds them to the batch already going out. Returns at once. */
        fun enqueue(uris: List<Uri>) {
            batcher.enqueue(uris) { work -> thread(name = "edgepad-send-files", block = work) }
        }

        /** Sends again the files of the last batch that did not arrive. */
        fun retryFailed() {
            enqueue(failedUris)
        }

        /** Sends the one document [uri], blocking; the code of how it ended. */
        fun send(uri: Uri): XferCode {
            val result = sendAll(listOf(uri))
            return result.failed.firstOrNull()?.let { codeOf(it.why) } ?: XferCode.SAVED
        }

        private fun labelOf(uri: Uri) = uri.lastPathSegment ?: uri.toString()

        private fun sendOne(
            uri: Uri,
            name: String,
            size: Long,
        ): XferCode {
            var socket: BulkSocket? = null
            try {
                socket = openBulk()
                return socket?.background { sendOn(uri, name, size) } ?: sendOn(uri, name, size)
            } finally {
                bulk = null
                socket?.close()
            }
        }

        private fun sendOn(
            uri: Uri,
            name: String,
            size: Long,
        ): XferCode {
            nextId = nextId % MAX_ID + 1
            val at = progress.snapshot
            return sender.send(
                open = { app.contentResolver.openInputStream(uri) ?: throw IOException("No stream for $uri") },
                name = name,
                size = size,
                id = nextId,
                cancelled = batcher::cancelled,
                progress = { sent ->
                    notice.progress(name, sent, size, true, at.index, at.total)
                    progress.bytes(sent)
                },
            )
        }

        private fun finished(result: XferBatchResult) {
            sendEnded()
            if (result.total <= 1) {
                notice.finish(result.failed.firstOrNull()?.name ?: progress.snapshot.name, result.allSent)
            } else {
                notice.finishBatch(result.sent, result.failed.size)
            }
        }

        // The second socket, when this link is a network one and the laptop accepts it; null means the control link.
        private fun openBulk(): BulkSocket? {
            val target = network ?: return null
            val host = link.peer ?: return null
            val keys = LinkKeys.open(app)
            val socket =
                BulkSocket(
                    host = host,
                    transport = transport,
                    laptopId = target.laptopId,
                    keys = { keys },
                    onFrame = ::take,
                )
            if (!socket.open()) return null
            bulk = socket.writer(link::send)
            return socket
        }
    }

    private const val MAX_ID = 255
    private const val RECEIVE_END_MS = 2_000L
    private const val REARM_MS = 1_000L
}

/** How a batch ended: [sent] files arrived, [failed] did not, in order. */
data class XferBatchResult(
    val sent: Int,
    val failed: List<XferFailed>,
) {
    val total: Int get() = sent + failed.size
    val allSent: Boolean get() = failed.isEmpty()
}

/** The wire code a single-file caller gets for [failure]: local failures read as a refusal, as they always did. */
internal fun codeOf(failure: XferFailure): XferCode =
    when (failure) {
        XferFailure.DAMAGED -> XferCode.BAD_HASH
        XferFailure.WRITE_FAILED -> XferCode.WRITE_FAILED
        XferFailure.BUSY -> XferCode.BUSY
        XferFailure.TIMED_OUT -> XferCode.TIMED_OUT
        XferFailure.LINK_LOST -> XferCode.SESSION_ENDED
        XferFailure.CANCELLED -> XferCode.CANCELLED
        XferFailure.REFUSED, XferFailure.UNREADABLE, XferFailure.TOO_BIG, XferFailure.NOT_SENT -> XferCode.REJECTED
    }

/**
 * The queue of one link's outgoing files: one batch at a time, files one after another, more appended while it runs.
 * A fault of one file moves on to the next; a cancel or a lost link stops the batch and the rest are NOT_SENT. Pure
 * over [T] (a Uri in the app), so a JVM test runs it.
 *
 * @param describe The name and size of an item, or null when it cannot be read. Does I/O.
 * @param label The name an unreadable item is listed under.
 * @param sendOne Sends one item and blocks for the laptop's answer. May throw when the item cannot be read.
 * @param began Called once a batch has begun, under the queue's lock.
 * @param finished Called once a batch has ended, on the thread that ran it.
 */
internal class XferBatcher<T>(
    private val progress: XferProgress,
    private val sharing: AtomicInteger,
    private val describe: (T) -> Pair<String, Long>?,
    private val label: (T) -> String,
    private val sendOne: (T, String, Long) -> XferCode,
    private val began: () -> Unit = {},
    private val finished: (XferBatchResult) -> Unit = {},
) {
    private val lock = Any()
    private val pending = ArrayDeque<T>()
    private var running = false
    private var lastFailed: List<T> = emptyList()

    // Set by a cancel, cleared only when the next batch begins: a file starting never clears it.
    @Volatile private var stop = false

    /** Whether a cancel stopped the batch running. Read by the sender between chunks. */
    fun cancelled(): Boolean = stop

    val busy: Boolean get() = synchronized(lock) { running }

    /** The items of the last batch that did not arrive. */
    val failed: List<T> get() = synchronized(lock) { lastFailed }

    fun cancel() {
        stop = true
        progress.requestCancel()
    }

    /** Runs a batch of [items] on the calling thread to its end; null when one is already running. */
    fun run(items: List<T>): XferBatchResult? {
        synchronized(lock) {
            if (running) return null
            begin(items)
        }
        return drain()
    }

    /** Appends [items] to the batch running, or begins one and hands its loop to [start]. */
    fun enqueue(
        items: List<T>,
        start: (() -> Unit) -> Unit,
    ) {
        if (items.isEmpty()) return
        synchronized(lock) {
            if (running) {
                pending.addAll(items)
                progress.addToBatch(items.size)
                return
            }
            begin(items)
        }
        sharing.incrementAndGet()
        start {
            try {
                drain()
            } finally {
                sharing.decrementAndGet()
            }
        }
    }

    // Under the lock.
    private fun begin(items: List<T>) {
        running = true
        stop = false
        lastFailed = emptyList()
        pending.clear()
        pending.addAll(items)
        progress.beginBatch(sending = true, total = items.size)
        began()
    }

    private fun drain(): XferBatchResult {
        var sent = 0
        val failures = mutableListOf<XferFailed>()
        val failedItems = mutableListOf<T>()
        var stopped = false
        try {
            while (true) {
                val item = next(failedItems) ?: break
                if (stop) stopped = true
                val failure: XferFailure?
                val name: String
                if (stopped) {
                    name = label(item)
                    failure = XferFailure.NOT_SENT
                } else {
                    val info = described(item)
                    name = info?.first ?: label(item)
                    progress.startFile(name, info?.second ?: 0)
                    failure =
                        when {
                            info == null -> XferFailure.UNREADABLE
                            info.second > XferLimits.MAX_BYTES -> XferFailure.TOO_BIG
                            else -> sent(item, name, info.second)
                        }
                }
                progress.fileDone(name, failure)
                if (failure == null) {
                    sent++
                } else {
                    failures += XferFailed(name, failure)
                    failedItems += item
                    if (!continuesAfter(failure)) stopped = true
                }
            }
        } finally {
            // Still running only after a throw: what is left is not sent, and the queue is free again.
            synchronized(lock) {
                if (running) {
                    failedItems += pending
                    pending.clear()
                    running = false
                    lastFailed = failedItems.toList()
                    progress.endBatch()
                }
            }
            finished(XferBatchResult(sent, failures.toList()))
        }
        return XferBatchResult(sent, failures)
    }

    // The next item, or null with the batch closed in the same step, so an append never lands after the end.
    private fun next(failedItems: List<T>): T? =
        synchronized(lock) {
            pending.removeFirstOrNull() ?: run {
                running = false
                lastFailed = failedItems.toList()
                progress.endBatch()
                null
            }
        }

    // A revoked grant or a dead provider throws, as any runtime exception across binder: the item cannot be read.
    private fun described(item: T): Pair<String, Long>? =
        try {
            describe(item)
        } catch (e: IOException) {
            null
        } catch (e: RuntimeException) {
            null
        }

    private fun sent(
        item: T,
        name: String,
        size: Long,
    ): XferFailure? =
        try {
            failureOf(sendOne(item, name, size))
        } catch (e: IOException) {
            XferFailure.UNREADABLE
        } catch (e: RuntimeException) {
            XferFailure.UNREADABLE
        }
}

/**
 * Where an XFER frame goes: a status that answers the file being sent to [sender], anything else to the receiver, which
 * is built only now. The control link and the bulk socket both come through here.
 */
internal fun routeXfer(
    frame: Frame,
    sender: XferSender,
    receiver: () -> XferReceiver,
) {
    if (frame is Frame.XferStatus && sender.take(frame)) return
    receiver().take(frame)
}

/** The Cancel action of the transfer notification. Not exported: only this app's own PendingIntent reaches it. */
class XferCancelReceiver : BroadcastReceiver() {
    override fun onReceive(
        context: Context,
        intent: Intent,
    ) {
        Transfers.cancel()
    }
}
