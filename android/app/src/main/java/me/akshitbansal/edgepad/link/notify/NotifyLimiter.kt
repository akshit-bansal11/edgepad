package me.akshitbansal.edgepad.link.notify

/**
 * The phone's twin of the laptop's limiter: at most [PER_MINUTE] notifications in any minute, and an identical one
 * within [DUPLICATE_MS] of the last is dropped. It keeps a time per post of the last minute and nothing of their text.
 *
 * @param now The clock in milliseconds, a seam for tests.
 */
class NotifyLimiter(
    private val now: () -> Long = System::currentTimeMillis,
) {
    private val times = ArrayDeque<Long>()
    private val seen = HashMap<Int, Long>()

    /** True when a notification with this [key], a hash of its content, may be posted now. */
    @Synchronized
    fun allow(key: Int): Boolean {
        val t = now()
        while (times.isNotEmpty() && t - times.first() >= MINUTE_MS) times.removeFirst()
        seen.values.removeAll { t - it >= DUPLICATE_MS }
        if (key in seen || times.size >= PER_MINUTE) return false
        times.addLast(t)
        seen[key] = t
        return true
    }

    companion object {
        const val PER_MINUTE = 10
        const val DUPLICATE_MS = 10_000L
        private const val MINUTE_MS = 60_000L
    }
}
