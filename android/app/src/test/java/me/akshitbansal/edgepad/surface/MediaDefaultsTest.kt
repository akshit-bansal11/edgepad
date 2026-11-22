package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.Space
import org.junit.Assert.assertTrue
import org.junit.Test

/** The two media pieces, at their default places, never overlap at any media scale the setting allows. */
class MediaDefaultsTest {
    @Test
    fun nowPlayingClearsTheTransportAtEveryScale() {
        val now = FloatArray(PieceGeometry.TRANSPORT_RECTS)
        val transport = FloatArray(PieceGeometry.TRANSPORT_RECTS)
        for ((w, h) in listOf(800f to 360f, 360f to 800f)) {
            val landscape = w > h
            val np = MediaPiece.NOW_PLAYING
            val tr = MediaPiece.TRANSPORT
            val ny = if (landscape) np.landscapeY else np.defaultY
            val ty = if (landscape) tr.landscapeY else tr.defaultY
            for (step in 6..14) {
                val scale = step / 10f
                PieceGeometry.nowPlaying(w, h, np.defaultX, ny, 1f, scale, now)
                PieceGeometry.transport(w, h, tr.defaultX, ty, 1f, scale, Space.TOUCH, transport)
                // The transport's box: previous's left to next's right, play's top to play's bottom.
                val above = now[3] <= transport[1]
                val below = transport[3] <= now[1]
                val left = now[2] <= transport[4]
                val right = transport[10] <= now[0]
                val apart = above || below || left || right
                assertTrue("overlap at $w x $h dp, scale $scale", apart)
            }
        }
    }
}
