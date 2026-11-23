package me.akshitbansal.edgepad.link

import android.net.TrafficStats
import android.os.Handler
import android.os.Looper
import android.os.SystemClock

/** Pure math of the phone's live network speed, for a JVM test. */
object TrafficRate {
    private const val MS_PER_SECOND = 1_000L
    private const val BYTES_PER_KIB = 1_024L

    /**
     * KiB/s between two cumulative byte counters read [elapsedMs] apart, or -1 (unknown) when either counter is
     * unsupported (negative), went backwards (a reset) or no time passed.
     */
    fun kibPerSecond(
        before: Long,
        after: Long,
        elapsedMs: Long,
    ): Int {
        if (before < 0 || after < before || elapsedMs <= 0) return -1
        val kib = (after - before) * MS_PER_SECOND / elapsedMs / BYTES_PER_KIB
        return kib.coerceAtMost(Int.MAX_VALUE.toLong()).toInt()
    }
}

/**
 * Reads this phone's total received and sent bytes once a second between [start] and [stop], and reports the speeds
 * in KiB/s, -1 while unknown: the first reading has nothing to subtract, and [TrafficStats.UNSUPPORTED] stays -1.
 * Nothing runs while stopped. Main thread only.
 */
class TrafficSampler(
    private val onRate: (down: Int, up: Int) -> Unit,
) {
    private val handler = Handler(Looper.getMainLooper())
    private var running = false
    private var rx = TrafficStats.UNSUPPORTED.toLong()
    private var tx = TrafficStats.UNSUPPORTED.toLong()
    private var at = 0L

    private val tick =
        object : Runnable {
            override fun run() {
                val now = SystemClock.elapsedRealtime()
                val nextRx = TrafficStats.getTotalRxBytes()
                val nextTx = TrafficStats.getTotalTxBytes()
                val elapsed = now - at
                onRate(
                    if (at == 0L) -1 else TrafficRate.kibPerSecond(rx, nextRx, elapsed),
                    if (at == 0L) -1 else TrafficRate.kibPerSecond(tx, nextTx, elapsed),
                )
                rx = nextRx
                tx = nextTx
                at = now
                handler.postDelayed(this, PERIOD_MS)
            }
        }

    fun start() {
        if (running) return
        running = true
        at = 0L
        tick.run()
    }

    fun stop() {
        running = false
        handler.removeCallbacks(tick)
    }

    private companion object {
        const val PERIOD_MS = 1_000L
    }
}
