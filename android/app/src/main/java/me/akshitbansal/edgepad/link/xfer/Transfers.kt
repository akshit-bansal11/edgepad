package me.akshitbansal.edgepad.link.xfer

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.provider.OpenableColumns
import me.akshitbansal.edgepad.link.Laptop
import me.akshitbansal.edgepad.link.ManagedLink
import me.akshitbansal.edgepad.link.Target
import me.akshitbansal.edgepad.link.Transport
import me.akshitbansal.edgepad.link.net.LinkKeys
import me.akshitbansal.edgepad.protocol.Frame
import java.io.IOException
import java.util.IdentityHashMap
import java.util.concurrent.atomic.AtomicBoolean

/**
 * The file transfers of every connected link, process-wide so a transfer outlives the activity that began it. One
 * [Link] a connected laptop: the receiving half is built on the first XFER frame, on the link's reading thread, and
 * the sending half does its work on the thread of whoever calls [Link.send]. Frames from the second socket take the
 * same road as the control link's. Nothing here runs while no transfer
 * does: no timer, no thread of its own. Main thread only for [attach] and [link].
 */
object Transfers {
    private val links = IdentityHashMap<ManagedLink, Link>()

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

    /** One link's two directions. */
    class Link internal constructor(
        private val app: Context,
        private val link: ManagedLink,
        private val transport: Transport,
        private val network: Target.Network?,
        private val notice: XferNotification,
    ) {
        @Volatile private var bulk: ((Frame) -> Unit)? = null

        @Volatile private var cancelled = false

        // The receiver's idle check: one delayed post while a file is arriving, none otherwise.
        private val idle = Handler(Looper.getMainLooper())

        private val moving = AtomicBoolean()
        private var nextId = 0

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
                    arrived = { notice.finish(it, true) },
                    progress = { name, held, size -> notice.progress(name, held, size, false) },
                    after = { delayMs, work ->
                        val task = Runnable(work)
                        idle.postDelayed(task, delayMs)
                        val stop: () -> Unit = { idle.removeCallbacks(task) }
                        stop
                    },
                )
            }
        private val receiver by receiverOnce

        /** Whether a file is going out now. */
        val sending: Boolean get() = moving.get()

        /** Takes an XFER frame on the link's reading thread: the laptop's answer to a file sent, else a file arriving. */
        internal fun take(frame: Frame) {
            routeXfer(frame, sender) { receiver }
        }

        internal fun end() {
            cancel()
        }

        internal fun cancel() {
            cancelled = true
            if (receiverOnce.isInitialized() && receiver.busy) receiver.cancel()
        }

        /**
         * Sends the document [uri] and blocks until the laptop has answered, so never on the main thread. The second
         * socket carries the chunks when the laptop is on the network and lets it; otherwise, or when it breaks,
         * they go on the control link. [XferCode.BUSY] when a file is already going out.
         */
        fun send(uri: Uri): XferCode {
            if (!moving.compareAndSet(false, true)) return XferCode.BUSY
            cancelled = false
            var name = ""
            var socket: BulkSocket? = null
            try {
                val (picked, size) = query(app, uri) ?: return XferCode.REJECTED
                name = picked
                socket = openBulk()
                val code = socket?.background { sendOn(uri, picked, size) } ?: sendOn(uri, picked, size)
                notice.finish(picked, code == XferCode.SAVED)
                return code
            } catch (e: IOException) {
                notice.finish(name, false)
                return XferCode.REJECTED
            } catch (e: SecurityException) {
                // The provider took its grant back, or never gave one.
                notice.finish(name, false)
                return XferCode.REJECTED
            } finally {
                bulk = null
                socket?.close()
                moving.set(false)
            }
        }

        private fun sendOn(
            uri: Uri,
            name: String,
            size: Long,
        ): XferCode {
            nextId = nextId % MAX_ID + 1
            return sender.send(
                open = { app.contentResolver.openInputStream(uri) ?: throw IOException("No stream for $uri") },
                name = name,
                size = size,
                id = nextId,
                cancelled = { cancelled },
                progress = { sent -> notice.progress(name, sent, size, true) },
            )
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
