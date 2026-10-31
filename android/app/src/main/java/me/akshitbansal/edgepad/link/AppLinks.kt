package me.akshitbansal.edgepad.link

import android.os.Handler
import android.os.Looper
import me.akshitbansal.edgepad.link.blob.AppBlobSink
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Feature

/**
 * Every laptop's app list, kept in its own [LaptopState.apps]: one [AppBlobSink] a link, set on the link's blob route
 * for a laptop that named [Feature.APPS], and for any other nothing is taken and its blobs are dropped by the link.
 *
 * A blob is put together on the link's reading thread and applied on the main thread, so [LaptopState] stays the UI's
 * alone. A laptop's list is forgotten when its link goes: the epoch it belongs to died with the session. Nothing is
 * asked for here; the screen sends APPS_LIST, and a list that arrives unasked for only fills a catalogue the screen
 * clears before it asks.
 *
 * Main thread only, except the sinks, which the links call on their reading threads. It outlives the activity with the
 * links, so [events] is the activity's to set again each time it is built.
 *
 * @param routes Where a link's blobs are shared with the clipboard.
 * @param post Runs work on the main thread, for tests.
 */
class AppLinks(
    private val routes: BlobRoutes,
    private val post: (() -> Unit) -> Unit = ::postToMain,
) {
    /** What the screen is told: [token] is null for a new list, else the row whose icon has just arrived. */
    fun interface Events {
        fun onApps(
            laptop: Laptop,
            token: String?,
        )
    }

    private class Attached(
        val laptop: Laptop,
        val link: ManagedLink,
    )

    var events: Events? = null

    private val attached = ArrayList<Attached>()

    /** Brings the sinks in line with the laptops: links that are gone are let go, and every one that offers apps is taken. */
    fun refresh(laptops: List<Laptop>) {
        val live = laptops.filter { it.connected && Feature.APPS in it.state.features }
        val iterator = attached.iterator()
        while (iterator.hasNext()) {
            val gone = iterator.next()
            if (live.none { it.link === gone.link }) {
                routes.attach(gone.link, BlobRoutes.Lane.APPS, null)
                val catalog = gone.laptop.state.apps
                catalog.clear()
                iterator.remove()
            }
        }
        for (laptop in live) {
            val link = laptop.link ?: continue
            if (attached.any { it.link === link }) continue
            val sink = AppBlobSink { kind, label, data -> post { arrived(laptop, link, kind, label, data) } }
            routes.attach(link, BlobRoutes.Lane.APPS, sink::take)
            attached.add(Attached(laptop, link))
        }
    }

    // On the main thread. A blob that was posted before its link was let go changes nothing.
    private fun arrived(
        laptop: Laptop,
        link: ManagedLink,
        kind: BlobKind,
        label: String,
        data: ByteArray,
    ) {
        if (attached.none { it.link === link }) return
        val catalog = laptop.state.apps
        when (kind) {
            BlobKind.APP_LIST -> if (catalog.setList(data)) events?.onApps(laptop, null)
            BlobKind.APP_ICON -> if (catalog.setIcon(label, data)) events?.onApps(laptop, label)
            else -> Unit
        }
    }
}

private val main by lazy { Handler(Looper.getMainLooper()) }

/** The real [AppLinks.post]. Not private, since it is the default of a public constructor. */
internal fun postToMain(work: () -> Unit) {
    main.post(work)
}
