package me.akshitbansal.edgepad.link

/**
 * The last [CAPACITY] STATS samples, one fixed [IntArray] per series, allocated once. -1 stays -1 (unknown).
 * Touched on the UI thread only.
 */
class StatsRing {
    private val data = Array(SERIES) { IntArray(CAPACITY) }
    private var next = 0

    /** How many samples are held, 0..[CAPACITY]. */
    var size = 0
        private set

    /** Moves on every [push] and [clear], so a reader can tell the ring changed without reading it. */
    var version = 0
        private set

    fun push(s: StatsSample) {
        version++
        data[CPU][next] = s.cpu
        data[RAM][next] = s.ram
        data[GPU][next] = s.gpu
        data[VRAM][next] = s.vram
        data[DOWN][next] = s.down
        data[UP][next] = s.up
        data[PING][next] = s.ping
        data[LOSS][next] = s.loss
        data[CPU_TEMP][next] = s.cpuTemp
        data[GPU_TEMP][next] = s.gpuTemp
        data[GPU_POWER][next] = s.gpuPower
        next = (next + 1) % CAPACITY
        if (size < CAPACITY) size++
    }

    /** The [index]-th oldest value of [series], 0 until [size]. */
    fun value(
        series: Int,
        index: Int,
    ): Int = data[series][(next - size + index + CAPACITY) % CAPACITY]

    /** The newest value of [series], or -1 when nothing is held. */
    fun latest(series: Int): Int = if (size == 0) -1 else value(series, size - 1)

    fun clear() {
        version++
        size = 0
        next = 0
    }

    companion object {
        const val CAPACITY = 60
        const val CPU = 0
        const val RAM = 1
        const val GPU = 2
        const val VRAM = 3
        const val DOWN = 4
        const val UP = 5
        const val PING = 6
        const val LOSS = 7
        const val CPU_TEMP = 8
        const val GPU_TEMP = 9
        const val GPU_POWER = 10
        const val SERIES = 11
    }
}
