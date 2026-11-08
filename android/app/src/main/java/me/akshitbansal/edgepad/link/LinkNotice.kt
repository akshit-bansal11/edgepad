package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.Feature

/**
 * What the notification that keeps the links alive says: the laptop the screens are on, and how many more are
 * connected beside it. [reconnecting] is a link that dropped and is being won back, with nothing connected, where the
 * notification says so instead of vanishing. [sharing] says clipboard sharing is live with the laptop it names, which
 * is the only time it has anything to say about what a tap sends. Data, so a notification is only rewritten when this
 * actually changed.
 */
data class LinkNotice(
    val name: String,
    val others: Int,
    val reconnecting: Boolean = false,
    val sharing: Boolean = false,
)

/**
 * The notice for [laptops], or null when none is connected and none is being won back, which is when the notification
 * goes away. [selected] is named when it is connected; when it is not, a connected laptop that is takes its place, so
 * the notification never names a laptop with no link. With none connected, a laptop with a retry on its way is named
 * as being reconnected to, until its attempts run out. [clipboard] is the Share clipboard setting.
 */
internal fun linkNotice(
    laptops: List<Laptop>,
    selected: Laptop?,
    clipboard: Boolean = false,
): LinkNotice? {
    val live = laptops.filter { it.connected }
    val shown = live.firstOrNull { it === selected } ?: live.firstOrNull()
    if (shown == null) {
        val lost = laptops.filter(::beingWonBack)
        val back = lost.firstOrNull { it === selected } ?: lost.firstOrNull() ?: return null
        return LinkNotice(nameOf(back), 0, reconnecting = true)
    }
    return LinkNotice(nameOf(shown), live.size - 1, sharing = clipboard && Feature.CLIPBOARD in shown.state.features)
}

private fun nameOf(laptop: Laptop): String = laptop.state.laptopName?.takeIf { it.isNotEmpty() } ?: laptop.name

/** Dropped on its own with an attempt under way or one on its timer, and with attempts left. */
private fun beingWonBack(laptop: Laptop): Boolean =
    laptop.reconnecting &&
        laptop.attempts < LaptopConnections.MAX_ATTEMPTS &&
        (laptop.link != null || laptop.retryTimer != null)
