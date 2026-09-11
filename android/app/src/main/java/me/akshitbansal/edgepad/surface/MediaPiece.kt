package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.R

/**
 * The three parts of the media controls, each placed anywhere on the surface as a fraction of its width
 * and height, so a layout made in portrait keeps its shape in landscape.
 */
enum class MediaPiece(
    val nameRes: Int,
    val defaultX: Float,
    val defaultY: Float,
) {
    /** The app mark, the title and the time, with the progress line under them. */
    NOW_PLAYING(R.string.piece_now_playing, 0.5f, 0.74f),
    PLAY(R.string.piece_play, 0.5f, 0.86f),
    SKIP(R.string.piece_skip, 0.5f, 0.86f),
}
