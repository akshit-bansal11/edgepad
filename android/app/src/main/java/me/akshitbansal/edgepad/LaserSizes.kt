package me.akshitbansal.edgepad

/**
 * The Presenter laser's three sizes, on the wire's PEN_WIDTH scale: Small, Medium, Large. The laptop draws a dot
 * of [SHARE] x the size x its screen height, so the phone's pad and the settings preview use the same formula.
 */
object LaserSizes {
    const val SMALL = 10
    const val MEDIUM = 15
    const val LARGE = 20
    const val DEFAULT = MEDIUM

    /** The dot radius as a share of the screen height, per wire unit. */
    const val SHARE = 0.0005f

    val ALL = intArrayOf(SMALL, MEDIUM, LARGE)

    /** A saved size from before the three-way choice (5..100) mapped to the nearest of the three. */
    fun nearest(saved: Int): Int =
        when {
            saved <= 12 -> SMALL
            saved <= 17 -> MEDIUM
            else -> LARGE
        }

    /** The laptop's dot radius for [size] on a screen [height] tall, in the same unit as [height]. */
    fun radius(
        height: Float,
        size: Int,
    ): Float = height * SHARE * size
}
