package me.akshitbansal.edgepad.link

/**
 * What the notification that keeps the links alive says: the laptop the screens are on, and how many more are
 * connected beside it. Data, so a notification is only rewritten when this actually changed.
 */
data class LinkNotice(
    val name: String,
    val others: Int,
)

/**
 * The notice for [laptops], or null when none is connected, which is when the notification goes away. [selected] is
 * named when it is connected; when it is not, a connected laptop that is takes its place, so the notification never
 * names a laptop with no link.
 */
internal fun linkNotice(
    laptops: List<Laptop>,
    selected: Laptop?,
): LinkNotice? {
    val live = laptops.filter { it.connected }
    val shown = live.firstOrNull { it === selected } ?: live.firstOrNull() ?: return null
    val name = shown.state.laptopName?.takeIf { it.isNotEmpty() } ?: shown.name
    return LinkNotice(name, live.size - 1)
}
