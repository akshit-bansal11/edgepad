package me.akshitbansal.edgepad.link

/** Round-trip times: the median of the last [window] samples. */
class RttStats(
    private val window: Int = 20,
) {
    private val samples = ArrayDeque<Double>()

    fun add(ms: Double) {
        samples.addLast(ms)
        if (samples.size > window) samples.removeFirst()
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

    fun clear() = samples.clear()
}
