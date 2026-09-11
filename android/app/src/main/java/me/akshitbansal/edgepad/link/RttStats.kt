package me.akshitbansal.edgepad.link

/** Round-trip times: the median of the last [window] samples, and the best seen since [clear]. */
class RttStats(
    private val window: Int = 20,
) {
    private val samples = ArrayDeque<Double>()

    var best = Double.NaN
        private set

    fun add(ms: Double) {
        samples.addLast(ms)
        if (samples.size > window) samples.removeFirst()
        best = if (best.isNaN()) ms else minOf(best, ms)
    }

    fun median(): Double {
        val sorted = samples.sorted()
        val mid = sorted.size / 2
        return when {
            sorted.isEmpty() -> Double.NaN
            sorted.size % 2 == 1 -> sorted[mid]
            else -> (sorted[mid - 1] + sorted[mid]) / 2
        }
    }

    fun clear() {
        samples.clear()
        best = Double.NaN
    }
}
