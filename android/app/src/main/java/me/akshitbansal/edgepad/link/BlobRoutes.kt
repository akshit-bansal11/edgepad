package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame
import java.util.IdentityHashMap

/**
 * Shares each link's one blob sink between the features that take blobs. A link has a single [ManagedLink.blobs], and
 * a blob's kind is only in its BLOB_BEGIN, so the router set there remembers which lane the blob in flight belongs to
 * and hands its chunks and its end to that lane alone. A lane with no sink drops its blobs, and so does a kind no lane
 * claims, which is the link's own rule for a blob nobody takes.
 *
 * The router goes in with the first lane attached to a link and comes out with the last one detached, so a link nothing
 * listens to has no sink at all. Attaching and detaching are the main thread's; [Router.take] is the link's reading
 * thread's, and reads the lanes' sinks as they were last set.
 */
class BlobRoutes {
    /** The features that take blobs, and which kinds each takes. */
    enum class Lane(
        val kinds: Set<BlobKind>,
    ) {
        CLIPBOARD(setOf(BlobKind.CLIP_TEXT, BlobKind.CLIP_IMAGE)),
        APPS(setOf(BlobKind.APP_LIST, BlobKind.APP_ICON)),
        ;

        companion object {
            /** The lane that takes blobs of this kind id, or null for a kind none does. */
            fun of(kind: Int): Lane? = entries.firstOrNull { lane -> lane.kinds.any { it.id == kind } }
        }
    }

    private class Router {
        private val sinks = arrayOfNulls<(Frame) -> Unit>(Lane.entries.size)

        /** The lane of the blob being read: the reading thread's alone. */
        private var current: Lane? = null

        @Synchronized
        fun isEmpty(): Boolean = sinks.all { it == null }

        @Synchronized
        fun set(
            lane: Lane,
            sink: ((Frame) -> Unit)?,
        ) {
            sinks[lane.ordinal] = sink
        }

        fun take(frame: Frame) {
            if (frame is Frame.BlobBegin) current = Lane.of(frame.kind)
            val lane = current ?: return
            val sink = synchronized(this) { sinks[lane.ordinal] }
            sink?.invoke(frame)
        }
    }

    private val routers = IdentityHashMap<ManagedLink, Router>()

    /** Sets, or with null removes, what [lane] does with the blobs [link] reads. */
    fun attach(
        link: ManagedLink,
        lane: Lane,
        sink: ((Frame) -> Unit)?,
    ) {
        val router = routers[link] ?: if (sink == null) return else Router().also { routers[link] = it }
        router.set(lane, sink)
        if (router.isEmpty()) {
            routers.remove(link)
            link.blobs = null
        } else {
            link.blobs = router::take
        }
    }
}
