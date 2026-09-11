package me.akshitbansal.edgepad.surface

import kotlin.math.PI
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.floor
import kotlin.math.hypot
import kotlin.math.sin

/**
 * The screen's edge as one clockwise path: a rounded rectangle whose corners follow the display's own
 * rounding, so a ruler bends round a corner instead of being clipped by it. A position on the path is a
 * length from the left end of the top edge. [lengthAt] maps the corner scale (0 top-left,
 * 1 top-right, 2 bottom-right, 3 bottom-left) onto those lengths.
 *
 * Pure arithmetic, and allocation-free after construction, so it is tested on the JVM and safe to call
 * while drawing.
 */
class Perimeter(
    val width: Float,
    val height: Float,
    radius: Float,
) {
    // Never an empty range: a 1 by 1 placeholder before layout must not throw.
    val radius = radius.coerceIn(MIN_RADIUS, maxOf(MIN_RADIUS, minOf(width, height) / 2))
    private val top = width - 2 * this.radius
    private val side = height - 2 * this.radius
    private val arc = HALF_PI * this.radius

    /** The whole way round. */
    val length = 2 * top + 2 * side + 4 * arc

    /** Where each corner's arc is halfway round, by corner index; index 4 is corner 0 again, a lap on. */
    private val cornerMid =
        floatArrayOf(
            -arc / 2,
            top + arc / 2,
            top + arc + side + arc / 2,
            2 * top + 2 * arc + side + arc / 2,
            length - arc / 2,
        )

    // The eight pieces of the path, clockwise from the left end of the top edge.
    private val pieces: Array<Piece>

    init {
        val r = this.radius
        var start = 0f
        val list = ArrayList<Piece>(PIECES)

        fun line(
            x: Float,
            y: Float,
            ux: Float,
            uy: Float,
            length: Float,
        ) {
            list.add(Line(start, length, x, y, ux, uy))
            start += length
        }

        fun corner(
            cx: Float,
            cy: Float,
            from: Float,
        ) {
            list.add(Arc(start, arc, cx, cy, r, from))
            start += arc
        }
        line(r, 0f, 1f, 0f, top)
        corner(width - r, r, -HALF_PI)
        line(width, r, 0f, 1f, side)
        corner(width - r, height - r, 0f)
        line(width - r, height, -1f, 0f, top)
        corner(r, height - r, HALF_PI)
        line(0f, height - r, 0f, -1f, side)
        corner(r, r, PI.toFloat())
        pieces = list.toTypedArray()
    }

    /** Wraps any length into [0, length). */
    fun wrap(s: Float): Float = ((s % length) + length) % length

    /** The shortest signed way from [from] to [to], so a move across the path's start is not a lap. */
    fun delta(
        from: Float,
        to: Float,
    ): Float {
        val d = wrap(to - from)
        return if (d > length / 2) d - length else d
    }

    /** The length along the path of corner position [at] (0 to 4, clockwise from the top-left corner). */
    fun lengthAt(at: Float): Float {
        val wrapped = ((at % CORNERS) + CORNERS) % CORNERS
        val k = floor(wrapped).toInt()
        val f = wrapped - k
        return wrap(cornerMid[k] + f * (cornerMid[k + 1] - cornerMid[k]))
    }

    /** Writes the point at length [s] and its inward unit normal into [out] as x, y, nx, ny. */
    fun point(
        s: Float,
        out: FloatArray,
    ) {
        val d = wrap(s)
        for (piece in pieces) {
            if (d < piece.start + piece.length) {
                piece.at(d - piece.start, out)
                return
            }
        }
        pieces.last().at(pieces.last().length, out)
    }

    /** Writes the nearest length along the path to ([x], [y]) and the distance to it into [out] as s, distance. */
    fun project(
        x: Float,
        y: Float,
        out: FloatArray,
    ) {
        var bestS = 0f
        var bestDistance = Float.MAX_VALUE
        for (piece in pieces) {
            piece.nearest(x, y, out)
            if (out[1] < bestDistance) {
                bestDistance = out[1]
                bestS = piece.start + out[0]
            }
        }
        out[0] = wrap(bestS)
        out[1] = bestDistance
    }

    private sealed interface Piece {
        val start: Float
        val length: Float

        fun at(
            d: Float,
            out: FloatArray,
        )

        /** Writes the offset along this piece of the nearest point, and the distance to it. */
        fun nearest(
            x: Float,
            y: Float,
            out: FloatArray,
        )
    }

    private class Line(
        override val start: Float,
        override val length: Float,
        val x: Float,
        val y: Float,
        val ux: Float,
        val uy: Float,
    ) : Piece {
        override fun at(
            d: Float,
            out: FloatArray,
        ) {
            out[0] = x + ux * d
            out[1] = y + uy * d
            // Clockwise travel, so inward is the direction turned a quarter clockwise on screen.
            out[2] = -uy
            out[3] = ux
        }

        override fun nearest(
            x: Float,
            y: Float,
            out: FloatArray,
        ) {
            val t = ((x - this.x) * ux + (y - this.y) * uy).coerceIn(0f, length)
            out[0] = t
            out[1] = hypot(x - (this.x + ux * t), y - (this.y + uy * t))
        }
    }

    private class Arc(
        override val start: Float,
        override val length: Float,
        val cx: Float,
        val cy: Float,
        val r: Float,
        val from: Float,
    ) : Piece {
        override fun at(
            d: Float,
            out: FloatArray,
        ) {
            val angle = from + d / r
            val c = cos(angle)
            val s = sin(angle)
            out[0] = cx + r * c
            out[1] = cy + r * s
            out[2] = -c
            out[3] = -s
        }

        override fun nearest(
            x: Float,
            y: Float,
            out: FloatArray,
        ) {
            var rel = atan2(y - cy, x - cx) - from
            while (rel < -PI) rel += (2 * PI).toFloat()
            while (rel >= PI) rel -= (2 * PI).toFloat()
            val clamped = rel.coerceIn(0f, HALF_PI)
            val angle = from + clamped
            out[0] = clamped * r
            out[1] = hypot(x - (cx + r * cos(angle)), y - (cy + r * sin(angle)))
        }
    }

    private companion object {
        const val CORNERS = 4
        const val PIECES = 8
        const val MIN_RADIUS = 1f
        const val HALF_PI = (PI / 2).toFloat()
    }
}
