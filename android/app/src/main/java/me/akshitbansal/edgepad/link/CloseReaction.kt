package me.akshitbansal.edgepad.link

/** What a closed link means for its record. */
internal enum class CloseReaction { QUIET, RECONNECT, FAILED }

/**
 * A link ended on purpose is quiet, and so is a working one that closes while another transport's record, which names
 * it as the one it replaces, is still connecting: the laptop let go as the switch began. Any other working link that
 * closes was dropped, and never-connected ones failed.
 */
internal fun closeReaction(
    ended: Boolean,
    wasConnected: Boolean,
    handoverPending: Boolean,
): CloseReaction =
    when {
        ended || (wasConnected && handoverPending) -> CloseReaction.QUIET
        wasConnected -> CloseReaction.RECONNECT
        else -> CloseReaction.FAILED
    }
