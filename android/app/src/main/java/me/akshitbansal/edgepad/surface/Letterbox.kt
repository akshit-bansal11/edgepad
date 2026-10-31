package me.akshitbansal.edgepad.surface

/**
 * The part of a drawing area that stands for the laptop's screen: the largest rectangle of the laptop's
 * aspect that fits, centred. The phone's panel is rarely the laptop's shape, and mapping the whole panel onto
 * the screen would stretch every circle drawn on it, so the margins are dead and a touch there clamps to the
 * nearest edge instead. Pure, so it is tested on the JVM.
 */
class Letterbox private constructor(
    val left: Float,
    val top: Float,
    val width: Float,
    val height: Float,
) {
    /** Where a touch at [px], [py] lands on the laptop's screen, 0..65535 on each axis and clamped. */
    fun toWire(
        px: Float,
        py: Float,
    ): Pair<Int, Int> = Pair(axis((px - left) / width), axis((py - top) / height))

    /** The inverse of [toWire]: where a wire coordinate is drawn in the area. */
    fun toPixels(
        wx: Int,
        wy: Int,
    ): Pair<Float, Float> = Pair(left + wx / WIRE_MAX * width, top + wy / WIRE_MAX * height)

    private fun axis(fraction: Float): Int = (fraction.coerceIn(0f, 1f) * WIRE_MAX).toInt()

    companion object {
        const val WIRE_MAX = 65535f

        /** Null while either size is unknown or not positive: there is nothing to draw on yet. */
        fun fit(
            areaW: Float,
            areaH: Float,
            screenW: Int,
            screenH: Int,
        ): Letterbox? {
            if (areaW <= 0f || areaH <= 0f || screenW <= 0 || screenH <= 0) return null
            val scale = minOf(areaW / screenW, areaH / screenH)
            val w = screenW * scale
            val h = screenH * scale
            return Letterbox((areaW - w) / 2f, (areaH - h) / 2f, w, h)
        }
    }
}
