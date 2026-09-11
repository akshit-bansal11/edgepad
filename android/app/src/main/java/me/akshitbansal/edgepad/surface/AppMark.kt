package me.akshitbansal.edgepad.surface

import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF

/**
 * A simple monochrome mark for the app playing, drawn here rather than shipped as a logo file: a few
 * strokes that read as the player at a glance. Anything unknown gets its first letter.
 */
class AppMark {
    private val path = Path()
    private val box = RectF()

    /** True when [app] has a mark of its own. */
    fun known(app: String): Boolean = key(app) != null

    /** Draws the mark for [app] centred on ([cx], [cy]) inside a box [size] across, in [paint]'s colour. */
    fun draw(
        canvas: Canvas,
        app: String,
        cx: Float,
        cy: Float,
        size: Float,
        paint: Paint,
    ) {
        val r = size / 2
        val stroke = size * STROKE
        paint.strokeWidth = stroke
        paint.strokeCap = Paint.Cap.ROUND
        when (key(app)) {
            "spotify" -> {
                // A disc with three arcs, the middle one longest.
                paint.style = Paint.Style.STROKE
                canvas.drawCircle(cx, cy, r - stroke / 2, paint)
                for (i in 0..2) {
                    val y = cy - r * ARC_TOP + i * r * ARC_GAP
                    val half = r * (ARC_WIDE - i * ARC_SHRINK)
                    box.set(cx - half, y - r * ARC_BOW, cx + half, y + r * ARC_BOW)
                    canvas.drawArc(box, ARC_START, ARC_SWEEP, false, paint)
                }
            }

            "youtube", "youtube music" -> {
                // A rounded rectangle (or a disc for Music) with a play triangle cut in.
                paint.style = Paint.Style.FILL
                if (key(app) == "youtube") {
                    box.set(cx - r, cy - r * TUBE_HEIGHT, cx + r, cy + r * TUBE_HEIGHT)
                    canvas.drawRoundRect(box, r * TUBE_ROUND, r * TUBE_ROUND, paint)
                } else {
                    canvas.drawCircle(cx, cy, r, paint)
                }
                triangle(cx + r * PLAY_SHIFT, cy, r * PLAY_SIZE)
                paint.color = paint.color.let { it and ALPHA_MASK or INVERT_ALPHA }
                canvas.drawPath(path, paint)
                paint.color = paint.color or ALPHA_FULL
                paint.style = Paint.Style.STROKE
                paint.strokeWidth = stroke * HAIR
                canvas.drawPath(path, paint)
            }

            "netflix" -> {
                // Two uprights joined by a diagonal.
                paint.style = Paint.Style.STROKE
                val x0 = cx - r * N_HALF
                val x1 = cx + r * N_HALF
                canvas.drawLine(x0, cy - r, x0, cy + r, paint)
                canvas.drawLine(x1, cy - r, x1, cy + r, paint)
                canvas.drawLine(x0, cy - r, x1, cy + r, paint)
            }

            "prime video" -> {
                // A smile with an arrowhead, under a straight lid.
                paint.style = Paint.Style.STROKE
                canvas.drawLine(cx - r * SMILE_HALF, cy - r * LID, cx + r * SMILE_HALF, cy - r * LID, paint)
                box.set(cx - r * SMILE_HALF, cy - r * SMILE_TOP, cx + r * SMILE_HALF, cy + r * SMILE_BOTTOM)
                canvas.drawArc(box, SMILE_START, SMILE_SWEEP, false, paint)
                val ax = cx + r * SMILE_HALF
                val ay = cy + r * SMILE_TIP
                canvas.drawLine(ax, ay, ax - r * ARROW, ay - r * ARROW * ARROW_FLAT, paint)
                canvas.drawLine(ax, ay, ax - r * ARROW * ARROW_FLAT, ay - r * ARROW, paint)
            }

            "vlc" -> {
                // The cone.
                paint.style = Paint.Style.STROKE
                path.reset()
                path.moveTo(cx - r * CONE_TOP, cy - r)
                path.lineTo(cx + r * CONE_TOP, cy - r)
                path.lineTo(cx + r, cy + r * CONE_BASE)
                path.lineTo(cx - r, cy + r * CONE_BASE)
                path.close()
                canvas.drawPath(path, paint)
                canvas.drawLine(cx - r * CONE_BAND, cy, cx + r * CONE_BAND, cy, paint)
            }

            "chrome", "edge", "firefox" -> {
                // A ring with a dot: a browser.
                paint.style = Paint.Style.STROKE
                canvas.drawCircle(cx, cy, r - stroke / 2, paint)
                paint.style = Paint.Style.FILL
                canvas.drawCircle(cx, cy, r * BROWSER_DOT, paint)
            }

            "apple music" -> {
                // A beamed pair of notes.
                paint.style = Paint.Style.STROKE
                val x0 = cx - r * NOTE_HALF
                val x1 = cx + r * NOTE_HALF
                canvas.drawLine(x0, cy - r * NOTE_TOP, x1, cy - r, paint)
                canvas.drawLine(x0, cy - r * NOTE_TOP, x0, cy + r * NOTE_STEM, paint)
                canvas.drawLine(x1, cy - r, x1, cy + r * NOTE_STEM_SHORT, paint)
                paint.style = Paint.Style.FILL
                canvas.drawCircle(x0 - r * NOTE_HEAD, cy + r * NOTE_STEM, r * NOTE_HEAD, paint)
                canvas.drawCircle(x1 - r * NOTE_HEAD, cy + r * NOTE_STEM_SHORT, r * NOTE_HEAD, paint)
            }

            "disney+" -> {
                // A swoosh over a plus.
                paint.style = Paint.Style.STROKE
                box.set(cx - r, cy - r * SWOOSH_TOP, cx + r * SWOOSH_RIGHT, cy + r * SWOOSH_BOTTOM)
                canvas.drawArc(box, SWOOSH_START, SWOOSH_SWEEP, false, paint)
                val px = cx + r * PLUS_X
                val py = cy + r * PLUS_Y
                canvas.drawLine(px - r * PLUS, py, px + r * PLUS, py, paint)
                canvas.drawLine(px, py - r * PLUS, px, py + r * PLUS, paint)
            }

            "media player", "films & tv" -> {
                // A play triangle in a ring.
                paint.style = Paint.Style.STROKE
                canvas.drawCircle(cx, cy, r - stroke / 2, paint)
                paint.style = Paint.Style.FILL
                triangle(cx + r * PLAY_SHIFT, cy, r * PLAY_SIZE)
                canvas.drawPath(path, paint)
            }

            else -> {
                paint.style = Paint.Style.FILL
                paint.textAlign = Paint.Align.CENTER
                paint.textSize = size * LETTER
                canvas.drawText(app.take(1).uppercase(), cx, cy + paint.textSize * CAP_CENTRE, paint)
            }
        }
    }

    private fun triangle(
        cx: Float,
        cy: Float,
        size: Float,
    ) {
        path.reset()
        path.moveTo(cx - size * TRI_BACK, cy - size / 2)
        path.lineTo(cx + size * TRI_FRONT, cy)
        path.lineTo(cx - size * TRI_BACK, cy + size / 2)
        path.close()
    }

    private fun key(app: String): String? {
        val name = app.trim().lowercase()
        return when {
            name.isEmpty() -> null
            "spotify" in name -> "spotify"
            "youtube music" in name -> "youtube music"
            "youtube" in name -> "youtube"
            "netflix" in name -> "netflix"
            "prime" in name -> "prime video"
            "vlc" in name -> "vlc"
            "chrome" in name -> "chrome"
            "edge" in name -> "edge"
            "firefox" in name -> "firefox"
            "apple music" in name || "itunes" in name -> "apple music"
            "disney" in name -> "disney+"
            "media player" in name || "groove" in name -> "media player"
            "films" in name || "movies" in name -> "films & tv"
            else -> null
        }
    }

    private companion object {
        const val STROKE = 0.09f
        const val HAIR = 0.5f
        const val LETTER = 0.5f
        const val CAP_CENTRE = 0.35f
        const val ARC_TOP = 0.35f
        const val ARC_GAP = 0.36f
        const val ARC_WIDE = 0.62f
        const val ARC_SHRINK = 0.14f
        const val ARC_BOW = 0.28f
        const val ARC_START = 200f
        const val ARC_SWEEP = 140f
        const val TUBE_HEIGHT = 0.7f
        const val TUBE_ROUND = 0.35f
        const val PLAY_SHIFT = 0.06f
        const val PLAY_SIZE = 0.9f
        const val TRI_BACK = 0.4f
        const val TRI_FRONT = 0.6f
        const val ALPHA_MASK = 0x00FFFFFF
        const val INVERT_ALPHA = 0x40000000
        const val ALPHA_FULL = 0xFF000000.toInt()
        const val N_HALF = 0.42f
        const val SMILE_HALF = 0.85f
        const val LID = 0.55f
        const val SMILE_TOP = 0.9f
        const val SMILE_BOTTOM = 0.75f
        const val SMILE_START = 30f
        const val SMILE_SWEEP = 120f
        const val SMILE_TIP = 0.28f
        const val ARROW = 0.3f
        const val ARROW_FLAT = 0.35f
        const val CONE_TOP = 0.35f
        const val CONE_BASE = 0.9f
        const val CONE_BAND = 0.7f
        const val BROWSER_DOT = 0.35f
        const val NOTE_HALF = 0.35f
        const val NOTE_TOP = 0.7f
        const val NOTE_STEM = 0.45f
        const val NOTE_STEM_SHORT = 0.25f
        const val NOTE_HEAD = 0.22f
        const val SWOOSH_TOP = 1f
        const val SWOOSH_RIGHT = 0.6f
        const val SWOOSH_BOTTOM = 0.4f
        const val SWOOSH_START = 200f
        const val SWOOSH_SWEEP = 150f
        const val PLUS_X = 0.55f
        const val PLUS_Y = 0.5f
        const val PLUS = 0.3f
    }
}
