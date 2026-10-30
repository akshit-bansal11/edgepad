package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.net.Sas

/**
 * One laptop this phone knows of right now and everything that belongs to it alone: what it reported, its round
 * trips, its link and where its retries stand. [LaptopConnections] owns these and keeps them on the main thread.
 *
 * [id] is [Target.key]. [target] is what a retry connects to, so it never carries a "Pair again".
 */
class Laptop(
    val id: String,
    var name: String,
    var kind: Transport,
    var target: Target,
) {
    /** What a prompt from this laptop is waiting for. */
    enum class Ask { PIN, PAIR_CODE }

    val address: String get() = target.key
    val state = LaptopState()
    val rtt = RttStats()

    /** Set from the moment an attempt starts, so null means nothing is being tried. */
    var link: ManagedLink? = null
    var attempts = 0
    var lostAt = 0L
    var connectedAt = 0L

    /** What the laptop's last PIN question allowed, for the PIN screen; a count, never a digit. */
    var pinAttemptsLeft = 0

    /** True once the PIN screen sent its answer to the current question, so a rebuild cannot send a second. */
    var pinAnswered = false

    /** The laptop has a PIN question open, answered or not. Over when the link is up or gone. */
    var pinAsked = false

    /** The code a network pairing shows and when its countdown ends; null when no pairing is waiting. */
    var pairCode: Sas? = null
    var pairDeadline = 0L

    /** The laptop answered the pairing and the owner is asked whether it accepted the code. */
    var pairAsking = false

    /** The laptop is deciding whether to trust this phone. */
    var awaiting = false

    /** The last attempt ended and nobody ended it on purpose. */
    var failed = false

    /** True from a working link dropping until it is back or given up on: a failed attempt then retries. */
    var reconnecting = false

    /** A tap has one quick retry coming if its connect never reaches the laptop. */
    var quickRetryLeft = false

    /** Cancels the waiting ping or next attempt. */
    var pingTimer: (() -> Unit)? = null
    var retryTimer: (() -> Unit)? = null

    val connected: Boolean get() = link?.connected == true

    /** Nothing is happening and nothing is coming: no link, no retry on its way. */
    val idle: Boolean get() = link == null && !reconnecting && retryTimer == null

    /** The prompt waiting on the owner, held here so a laptop that is not selected can wait on its row. */
    val asking: Ask?
        get() =
            when {
                pinAsked && !pinAnswered -> Ask.PIN
                pairCode != null -> Ask.PAIR_CODE
                else -> null
            }
}
