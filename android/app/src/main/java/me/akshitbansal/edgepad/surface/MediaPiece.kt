package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.R

/**
 * The two parts of the media controls, each placed anywhere on the surface as a fraction of its width
 * and height. Portrait and landscape each keep a layout of their own. By default they stack at the
 * bottom: the player and the track, then previous, play and next under them.
 */
enum class MediaPiece(
    val nameRes: Int,
    val defaultX: Float,
    val defaultY: Float,
) {
    /** The app's logo, the title and the time, with the progress line under them when no corner scrubs. */
    NOW_PLAYING(R.string.piece_now_playing, 0.5f, 0.78f),

    /** Previous, play/pause and next in a row. */
    TRANSPORT(R.string.piece_transport, 0.5f, 0.9f),
}
