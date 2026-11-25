package me.akshitbansal.edgepad.link

/** Which dashboard screen's DASH_WATCH bit each page of the Stats and Developer hubs sets. Pure, for a JVM test. */
object HubPages {
    const val SYSTEM = 0
    const val NETWORK = 1
    const val GIT = 1

    /** This phone as the device needs nothing from the laptop, so it sets no bit on either page. */
    fun system(
        page: Int,
        phone: Boolean,
    ): DashMask.Screen? =
        when {
            phone -> null
            page == NETWORK -> DashMask.Screen.NETWORK
            else -> DashMask.Screen.SYSTEM
        }

    fun developer(page: Int): DashMask.Screen = if (page == GIT) DashMask.Screen.GITHUB else DashMask.Screen.AI
}

/** What the speed strip of the Network page shows for the laptop's last [SpeedResult]. */
enum class StripKind { IDLE, RUNNING, FAILED, STOPPED, DONE }

/** Pure choices behind the Network page's speed strip, kept off the View so they can be tested. */
object SpeedStrip {
    /** Which face the strip shows; a capped test counts as done. */
    fun kind(speed: SpeedResult?): StripKind =
        when (speed?.state) {
            null -> StripKind.IDLE
            SpeedState.RUNNING -> StripKind.RUNNING
            SpeedState.FAILED -> StripKind.FAILED
            SpeedState.CANCELLED -> StripKind.STOPPED
            SpeedState.DONE, SpeedState.CAPPED -> StripKind.DONE
        }

    /** The button reads Stop while a test runs, and Test speed otherwise. */
    fun running(speed: SpeedResult?): Boolean = kind(speed) == StripKind.RUNNING
}

/** Pure choices behind the words of the connection rows. */
object NetFormat {
    private const val STRONG_FROM = 75
    private const val GOOD_FROM = 50
    private const val FAIR_FROM = 25
    private const val GBPS_FROM = 1000

    /** The laptop's signal percent as a level; -1 (unknown, always on Windows) is UNKNOWN. */
    fun signal(pct: Int): SignalLevel =
        when {
            pct < 0 -> SignalLevel.UNKNOWN
            pct >= STRONG_FROM -> SignalLevel.EXCELLENT
            pct >= GOOD_FROM -> SignalLevel.GOOD
            pct >= FAIR_FROM -> SignalLevel.FAIR
            else -> SignalLevel.WEAK
        }

    /** Link speeds from 1000 Mbps read in Gbps. */
    fun inGbps(mbps: Int): Boolean = mbps >= GBPS_FROM
}
