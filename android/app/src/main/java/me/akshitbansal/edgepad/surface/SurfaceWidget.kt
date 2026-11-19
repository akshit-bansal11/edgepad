package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.R

/**
 * The widgets drawn behind the trackpad, each placed anywhere on the surface as a fraction of its width and height
 * like a [MediaPiece]. Every one is off until it is switched on in the surface settings. Portrait and landscape each
 * keep a position of their own, and start in different places so that nothing overlaps by default.
 */
enum class SurfaceWidget(
    val nameRes: Int,
    val portraitX: Float,
    val portraitY: Float,
    val landscapeX: Float,
    val landscapeY: Float,
) {
    CLOCK(R.string.widget_clock, 0.5f, 0.16f, 0.2f, 0.25f),
    WEATHER(R.string.widget_weather, 0.5f, 0.28f, 0.2f, 0.55f),
    SYSTEM(R.string.widget_system, 0.5f, 0.40f, 0.8f, 0.25f),
    NETWORK(R.string.widget_network, 0.5f, 0.50f, 0.8f, 0.55f),
    AI(R.string.widget_ai, 0.5f, 0.62f, 0.5f, 0.30f),
}
