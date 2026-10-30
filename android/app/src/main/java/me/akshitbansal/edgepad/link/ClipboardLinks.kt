package me.akshitbansal.edgepad.link

import android.content.Context
import me.akshitbansal.edgepad.link.blob.BlobSender
import me.akshitbansal.edgepad.link.blob.ClipboardSurface
import me.akshitbansal.edgepad.link.blob.ClipboardSync
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Feature

/**
 * The shared clipboard across every laptop: one [ClipboardSync] a link, and SET CLIPBOARD so that only the selected
 * laptop shares. The phone sets 1 there and 0 on every other laptop that offers it, which makes "selected only" the
 * rule in both directions: a copy here reaches that laptop alone, and a copy made on a laptop that is not being driven
 * never replaces what is on this phone.
 *
 * Only a laptop that named [Feature.CLIPBOARD] has a share; for any other nothing is sent and its blobs are dropped
 * by the link. A new selection pushes nothing: [settle] is called as sharing moves, and the next copy is the first
 * thing the laptop gets.
 *
 * Main thread only, except [ClipboardSync]'s own entry points, which the links call on their reading threads.
 *
 * @param surface This phone's clipboard.
 * @param allowed The owner's own toggle, read every time.
 * @param settle Called just before sharing turns on for a laptop, so what is on the clipboard now is not sent to it.
 * @param routes Where a link's blobs are shared with the other features that take them, so this takes the clipboard's.
 * @param syncFor Makes a link's [ClipboardSync], so a test can give it a clock and a scheduler of its own.
 */
class ClipboardLinks(
    private val surface: ClipboardSurface,
    private val allowed: () -> Boolean,
    private val settle: () -> Unit = {},
    private val routes: BlobRoutes = BlobRoutes(),
    private val syncFor: (ClipboardSurface, BlobSender, () -> Boolean, () -> Boolean) -> ClipboardSync =
        { surface, sender, allowed, ended -> ClipboardSync(surface, sender, allowed, ended) },
) {
    private class Share(
        val link: ManagedLink,
        val sync: ClipboardSync,
    ) {
        /** What this laptop was last told, so a refresh that changes nothing sends nothing; null before the first. */
        var on: Boolean? = null
    }

    private val shares = ArrayList<Share>()

    /**
     * Brings every link in line with who is [selected]: links that are gone are let go, every other laptop that
     * offers the clipboard is turned off, and then the selected one is turned on. The offs go first so that for no
     * moment is the old laptop still sharing when the new one is.
     */
    fun refresh(
        laptops: List<Laptop>,
        selected: Laptop?,
    ) {
        val live = laptops.filter { it.connected && Feature.CLIPBOARD in it.state.features }
        val iterator = shares.iterator()
        while (iterator.hasNext()) {
            val share = iterator.next()
            if (live.none { it.link === share.link }) {
                share.sync.setShared(0)
                routes.attach(share.link, BlobRoutes.Lane.CLIPBOARD, null)
                iterator.remove()
            }
        }
        val chosen = live.firstOrNull { it === selected && allowed() }
        for (laptop in live) {
            if (laptop !== chosen) shareOf(laptop)?.let(::turnOff)
        }
        chosen?.let(::shareOf)?.let(::turnOn)
    }

    /** The platform saw the clipboard change. Each share decides for itself whether it is sharing. */
    fun localChanged() {
        for (share in shares.toList()) share.sync.localChanged()
    }

    private fun shareOf(laptop: Laptop): Share? {
        val link = laptop.link ?: return null
        shares.firstOrNull { it.link === link }?.let { return it }
        val ended = { !link.connected }
        val sender = BlobSender(link::send, laptop.kind, link::lastInputMs, ended, backlog = link::queued)
        val sync = syncFor(surface, sender, allowed, ended)
        routes.attach(link, BlobRoutes.Lane.CLIPBOARD) { frame -> sync.take(frame) }
        return Share(link, sync).also { shares.add(it) }
    }

    private fun turnOn(share: Share) {
        if (share.on == true) return
        settle()
        if (!share.sync.setShared(1)) return
        share.link.send(ControlId.CLIPBOARD.set(1))
        share.on = true
    }

    private fun turnOff(share: Share) {
        // Said once even when it was never on: a laptop that has not been told is only assumed to be off.
        if (share.on == false) return
        share.sync.setShared(0)
        share.link.send(ControlId.CLIPBOARD.set(0))
        share.on = false
    }
}

/** The clipboard's two halves, kept together because they outlive the activity together. */
class ClipboardShare(
    val bridge: ClipboardBridge,
    val links: ClipboardLinks,
)

/**
 * The real clipboard shared across the links. [context] must be the application's: this outlives the activity, like the
 * manager. [routes] is shared with the other features that take a link's blobs. [allowed] is the owner's own toggle,
 * and must not hold the activity either.
 */
fun clipboardShare(
    context: Context,
    routes: BlobRoutes,
    allowed: () -> Boolean,
): ClipboardShare {
    val bridge = ClipboardBridge(AndroidClipboard(context))
    val links = ClipboardLinks(bridge, allowed, bridge::settle, routes)
    bridge.onChanged = links::localChanged
    return ClipboardShare(bridge, links)
}
