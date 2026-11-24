package me.akshitbansal.edgepad

import kotlin.math.cos
import kotlin.math.exp

/**
 * The logo's 0.75 s pop as numbers: scripts/logo-motion.ps1 is the source, this is its twin for the opening screen.
 * Geometry is on the 1024-unit logo canvas; angles run clockwise from the right (0 = right, 90 = down). Pure.
 */
object LogoMotion {
    const val CANVAS = 1024f
    const val PIVOT = 881.8f
    const val STROKE = 88f
    const val TOTAL_MS = 760f

    private const val STAGGER = 60f
    private const val PART_MS = 420f
    private const val FROM_SCALE = 0.55f
    private const val FADE_IN = 0.25f
    private const val ARC_RADIUS = 288f
    private const val TICK_IN = 424f
    private const val TICK_OUT = 656f
    private const val NEEDLE_OUT = 720f

    /** One part: the arc (radius [outer]) or a tick from [inner] to [outer] at [angle] degrees. */
    class Stroke(
        val arc: Boolean,
        val angle: Float,
        val inner: Float,
        val outer: Float,
        val accent: Boolean,
    )

    /** The arc, then each tick in angle order (the needle is the middle one), as the pop plays them. */
    val strokes =
        listOf(
            Stroke(true, 180f, 0f, ARC_RADIUS, true),
            Stroke(false, 180f, TICK_IN, TICK_OUT, false),
            Stroke(false, 202.5f, TICK_IN, TICK_OUT, false),
            Stroke(false, 225f, TICK_IN, NEEDLE_OUT, true),
            Stroke(false, 247.5f, TICK_IN, TICK_OUT, false),
            Stroke(false, 270f, TICK_IN, TICK_OUT, false),
        )

    private fun progress(
        part: Int,
        ms: Float,
    ): Float = ((ms - part * STAGGER) / PART_MS).coerceIn(0f, 1f)

    /** How far part [part] has sprung out at [ms]: its radii are multiplied by this, toward the pivot. */
    fun scale(
        part: Int,
        ms: Float,
    ): Float {
        val x = progress(part, ms)
        val spring = if (x >= 1f) 1f else 1f - exp(-6f * x) * cos(9f * x)
        return FROM_SCALE + (1f - FROM_SCALE) * spring
    }

    /** How opaque part [part] is at [ms], 0 to 1. */
    fun opacity(
        part: Int,
        ms: Float,
    ): Float = (progress(part, ms) / FADE_IN).coerceIn(0f, 1f)
}
