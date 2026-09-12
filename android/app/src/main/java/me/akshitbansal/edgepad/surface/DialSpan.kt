package me.akshitbansal.edgepad.surface

/**
 * How far each dial's ruler may run along the edge in each direction, so two rulers never overlap and
 * none runs under the buttons at the top: a ruler is trimmed at its far ends, never at its centre.
 * Lengths are positions along a clockwise path of [length]; pure, so it is tested on the JVM.
 */
object DialSpan {
    /**
     * Fills [after] (clockwise reach) and [before] (anticlockwise reach) for every dial in [centres],
     * starting from [half] and cut back to the midpoint short of any other dial, less [gap], and to the
     * edge of any keep-out range in [keepOut], given as start/end pairs along the path.
     */
    fun compute(
        centres: FloatArray,
        half: Float,
        length: Float,
        keepOut: FloatArray,
        gap: Float,
        after: FloatArray,
        before: FloatArray,
    ) {
        for (i in centres.indices) {
            var a = half
            var b = half
            for (j in centres.indices) {
                if (j == i) continue
                val ahead = forward(centres[i], centres[j], length)
                a = minOf(a, ahead / 2 - gap / 2)
                b = minOf(b, (length - ahead) / 2 - gap / 2)
            }
            var k = 0
            while (k + 1 < keepOut.size) {
                val start = keepOut[k]
                val end = keepOut[k + 1]
                a = minOf(a, forward(centres[i], start, length))
                b = minOf(b, forward(end, centres[i], length))
                k += 2
            }
            after[i] = a.coerceAtLeast(0f)
            before[i] = b.coerceAtLeast(0f)
        }
    }

    /** The clockwise distance from [from] to [to] round the path, in [0, length). */
    private fun forward(
        from: Float,
        to: Float,
        length: Float,
    ): Float = (((to - from) % length) + length) % length
}
