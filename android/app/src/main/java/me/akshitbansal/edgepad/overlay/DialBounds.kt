package me.akshitbansal.edgepad.overlay

import me.akshitbansal.edgepad.surface.Perimeter
import kotlin.math.ceil
import kotlin.math.floor

/**
 * The rectangle one floating dial's window covers, in screen pixels: the ruler band along the edge and the
 * [of] depth inward of it, never larger than the screen. Pure, so it is tested on the JVM.
 */
data class DialBounds(
    val left: Int,
    val top: Int,
    val right: Int,
    val bottom: Int,
) {
    val width: Int get() = right - left
    val height: Int get() = bottom - top

    companion object {
        /** Spacing of the points sampled along the band; a bend this size is flat enough for a bounding box. */
        private const val STEP_PX = 16f

        /**
         * The box round the path from [centre] - [before] to [centre] + [after] and the inward [depthPx] from it,
         * cut to the [perimeter]'s own screen and at least one pixel each way.
         */
        fun of(
            perimeter: Perimeter,
            centre: Float,
            before: Float,
            after: Float,
            depthPx: Float,
        ): DialBounds {
            val pt = FloatArray(4)
            var minX = Float.MAX_VALUE
            var minY = Float.MAX_VALUE
            var maxX = -Float.MAX_VALUE
            var maxY = -Float.MAX_VALUE
            val start = centre - before.coerceAtLeast(0f)
            val end = centre + after.coerceAtLeast(0f)
            var s = start
            while (true) {
                perimeter.point(s, pt)
                val inX = pt[0] + pt[2] * depthPx
                val inY = pt[1] + pt[3] * depthPx
                minX = minOf(minX, pt[0], inX)
                maxX = maxOf(maxX, pt[0], inX)
                minY = minOf(minY, pt[1], inY)
                maxY = maxOf(maxY, pt[1], inY)
                if (s >= end) break
                s = minOf(s + STEP_PX, end)
            }
            val w = ceil(perimeter.width).toInt().coerceAtLeast(1)
            val h = ceil(perimeter.height).toInt().coerceAtLeast(1)
            val left = floor(minX).toInt().coerceIn(0, w - 1)
            val top = floor(minY).toInt().coerceIn(0, h - 1)
            val right = ceil(maxX).toInt().coerceIn(left + 1, w)
            val bottom = ceil(maxY).toInt().coerceIn(top + 1, h)
            return DialBounds(left, top, right, bottom)
        }
    }
}
