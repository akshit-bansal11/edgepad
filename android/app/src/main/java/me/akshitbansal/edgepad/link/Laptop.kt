package me.akshitbansal.edgepad.link

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

    /** True from a working link dropping until it is back or given up on: a failed attempt then retries. */
    var reconnecting = false

    /** A tap has one quick retry coming if its connect never reaches the laptop. */
    var quickRetryLeft = false

    /** Cancels the waiting ping or next attempt. */
    var pingTimer: (() -> Unit)? = null
    var retryTimer: (() -> Unit)? = null

    val connected: Boolean get() = link?.connected == true
}
