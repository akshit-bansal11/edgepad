package me.akshitbansal.edgepad.surface

import kotlin.math.abs
import kotlin.math.floor
import kotlin.math.roundToInt

/**
 * Where a dial sits on the screen's edge, on a scale that runs clockwise from corner to corner: 0 is the
 * top-left corner, 1 top-right, 2 bottom-right, 3 bottom-left, and 4 wraps back to 0. A dial on a whole
 * number wraps its corner as one L; anywhere else it is a straight ruler along one edge. Each edge is one
 * unit whatever its length, so a dial keeps its place relative to the corners in portrait and landscape.
 */
data class Placement(
    val at: Float,
) {
    init {
        require(at >= 0f && at < CORNERS) { "Placement out of range: $at" }
    }

    val atCorner: Boolean get() = abs(at - at.roundToInt()) < EPSILON

    /** The corner index 0-3 (top-left clockwise) for a corner dial, or null. */
    val corner: Int? get() = if (atCorner) at.roundToInt() % CORNERS.toInt() else null

    /** The edge index 0-3 (top, right, bottom, left) for an edge dial, or null. */
    val edge: Int? get() = if (atCorner) null else floor(at).toInt()

    companion object {
        const val CORNERS = 4f

        /** Positions this close to a corner snap onto it, so a corner is easy to land on when dragging. */
        const val SNAP = 0.06f

        private const val EPSILON = 1e-3f

        /** Wraps [raw] into range and snaps it onto a corner when it is within [SNAP] of one. */
        fun of(raw: Float): Placement {
            val wrapped = ((raw % CORNERS) + CORNERS) % CORNERS
            val nearest = wrapped.roundToInt()
            val snapped = if (abs(wrapped - nearest) < SNAP) nearest.toFloat() else wrapped
            return Placement(snapped % CORNERS)
        }
    }
}
