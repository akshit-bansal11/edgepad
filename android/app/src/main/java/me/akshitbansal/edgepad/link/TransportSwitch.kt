package me.akshitbansal.edgepad.link

/**
 * Whether a tap's connect takes the selection at once. With nothing working it does; so it does when the tap moves the
 * same laptop to another transport, whose old link is ended on the spot so that its close is never read as a drop.
 */
internal fun selectsOnTap(
    tapped: Boolean,
    connected: Boolean,
    switchesConnected: Boolean,
): Boolean = tapped && (!connected || switchesConnected)
