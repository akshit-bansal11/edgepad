package me.akshitbansal.edgepad.screens

/**
 * ANDU-6: a screen reader's one-cell move of a layout piece along one axis, landing on the same grid and centre snap
 * as a drag ([LayoutCanvas.snapFraction]). Pure, so it is tested on the JVM.
 */
internal object Nudge {
    /** The fraction one grid cell from [fraction] towards [dir] (-1 or 1) on an axis of [extentPx] pixels, in 0..1. */
    fun step(
        fraction: Float,
        extentPx: Int,
        dir: Int,
        density: Float,
    ): Float {
        if (extentPx <= 0) return fraction
        val px = fraction * extentPx + dir * (LayoutCanvas.GRID_DP * density)
        return LayoutCanvas.snapFraction(px, extentPx, density)
    }
}
