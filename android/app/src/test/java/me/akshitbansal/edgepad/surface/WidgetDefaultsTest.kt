package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Every widget's default place clears what the surface keeps for itself (the top buttons, the four corner rulers and
 * the two media pieces at their defaults, as the layout editor draws them as ghosts) and every other widget, on a
 * small and a large phone each way up.
 */
class WidgetDefaultsTest {
    private data class Box(
        val left: Float,
        val top: Float,
        val right: Float,
        val bottom: Float,
    ) {
        fun overlaps(other: Box): Boolean =
            left < other.right && other.left < right && top < other.bottom && other.top < bottom
    }

    @Test
    fun defaultsClearTheSurfaceAndEachOther() {
        for (density in DENSITIES) {
            for ((wDp, hDp) in SCREENS) {
                val w = wDp * density
                val h = hDp * density
                val ghosts = ghosts(w, h, density)
                val widgets = SurfaceWidget.entries.map { it to widgetBox(it, w, h, density) }
                for ((widget, box) in widgets) {
                    val where = "$widget on $wDp x $hDp dp at $density"
                    for (ghost in ghosts) assertFalse("$where $box meets ghost $ghost", box.overlaps(ghost))
                    for ((other, otherBox) in widgets) {
                        if (other != widget) assertFalse("$where $box meets $other $otherBox", box.overlaps(otherBox))
                    }
                }
            }
        }
    }

    @Test
    fun aScaledBoxStaysOnTheScreen() {
        for ((wDp, hDp) in SCREENS) {
            for (widget in SurfaceWidget.entries) {
                val (bw, bh) = WidgetPainter.boxSize(widget, wDp, hDp, 1f, Settings.MAX_WIDGET_SCALE)
                assertTrue(bw <= wDp && bh <= hDp)
                val (base, baseH) = WidgetPainter.boxSize(widget, wDp, hDp, 1f)
                val (small, smallH) = WidgetPainter.boxSize(widget, wDp, hDp, 1f, Settings.MIN_WIDGET_SCALE)
                assertEquals(base * 0.75f, small, EPSILON)
                assertEquals(baseH * 0.75f, smallH, EPSILON)
            }
        }
    }

    /** A widget's default box, kept on the screen as [WidgetPainter.layout] keeps it. */
    private fun widgetBox(
        widget: SurfaceWidget,
        w: Float,
        h: Float,
        density: Float,
    ): Box {
        val (bw, bh) = WidgetPainter.boxSize(widget, w, h, density)
        val fx = if (w > h) widget.landscapeX else widget.portraitX
        val fy = if (w > h) widget.landscapeY else widget.portraitY
        val cx = (fx * w).coerceIn(bw / 2, maxOf(w - bw / 2, bw / 2))
        val cy = (fy * h).coerceIn(bh / 2, maxOf(h - bh / 2, bh / 2))
        return Box(cx - bw / 2, cy - bh / 2, cx + bw / 2, cy + bh / 2)
    }

    /** What the layout editor draws as ghosts, with a dial in every corner and the media at its defaults. */
    private fun ghosts(
        w: Float,
        h: Float,
        density: Float,
    ): List<Box> {
        val out = ArrayList<Box>()
        val touch = Space.TOUCH * density
        val step =
            if (w > h) ControlSurface.TOP_BUTTON_OFFSET_LANDSCAPE_DP else ControlSurface.TOP_BUTTON_OFFSET_PORTRAIT_DP
        val top = ControlSurface.TOP_DP * density
        // The four top buttons sit at -1.5, -0.5, 0.5 and 1.5 steps from the middle, as the surface places them.
        for (i in 0 until TOP_BUTTONS) {
            val cx = w / 2f + (i - 1.5f) * step * density
            out.add(Box(cx - touch / 2, top - touch / 2, cx + touch / 2, top + touch / 2))
        }
        // The battery strip's tap rect: as wide as its room, from the top edge down 48 dp.
        val room = BatteryStrip.roomDp(1.5f * step) * density
        out.add(Box(w / 2f - room / 2, 0f, w / 2f + room / 2, BatteryStrip.HIT_BOTTOM_DP * density))
        val along = Settings.DEFAULT_DIAL_LENGTH * density
        val depth = RULER_DEPTH_DP * density
        for (at in 0 until CORNERS) {
            val right = at == 1 || at == 2
            val bottom = at >= 2
            val x0 = if (right) w - along else 0f
            val x1 = if (right) w else along
            val y0 = if (bottom) h - along else 0f
            val y1 = if (bottom) h else along
            out.add(Box(x0, if (bottom) h - depth else 0f, x1, if (bottom) h else depth))
            out.add(Box(if (right) w - depth else 0f, y0, if (right) w else depth, y1))
        }
        val nowPlaying = MediaPiece.NOW_PLAYING
        out.add(
            piece(
                nowPlaying.defaultX * w,
                (if (w > h) nowPlaying.landscapeY else nowPlaying.defaultY) * h,
                ControlSurface.NOW_PLAYING_WIDTH_DP * density,
                ControlSurface.NOW_PLAYING_HEIGHT_DP * density,
            ),
        )
        val transport = MediaPiece.TRANSPORT
        out.add(
            piece(
                transport.defaultX * w,
                transport.defaultY * h,
                (ControlSurface.SKIP_GAP_DP * 2 + Space.TOUCH) * density,
                ControlSurface.PLAY_DP * density,
            ),
        )
        return out
    }

    private fun piece(
        cx: Float,
        cy: Float,
        pw: Float,
        ph: Float,
    ): Box = Box(cx - pw / 2, cy - ph / 2, cx + pw / 2, cy + ph / 2)

    private companion object {
        const val RULER_DEPTH_DP = 44f
        const val CORNERS = 4
        const val TOP_BUTTONS = 4
        const val EPSILON = 0.001f
        val DENSITIES = floatArrayOf(1f, 2.75f)
        val SCREENS =
            listOf(
                360f to 640f,
                360f to 780f,
                411f to 890f,
                640f to 360f,
                800f to 360f,
                915f to 412f,
            )
    }
}
