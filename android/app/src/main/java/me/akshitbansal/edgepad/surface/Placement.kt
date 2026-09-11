package me.akshitbansal.edgepad.surface

/** Which screen edge a dial sits on, or none. */
enum class Edge { OFF, TOP, RIGHT, BOTTOM, LEFT }

/**
 * Where a dial sits: an edge and how far along it, 0 to 1, clockwise from the edge's first corner (left
 * to right along the top and bottom, top to bottom along the sides). The ends are the corners, where the
 * dial is a quarter turn; anywhere else it is a half turn centred on the edge, which also lets it sit just
 * inside a rounded screen corner instead of under it.
 */
data class Placement(
    val edge: Edge,
    val along: Float,
) {
    val atCorner: Boolean get() = along <= CORNER_SNAP || along >= 1f - CORNER_SNAP

    private val nearStart: Boolean get() = along < HALF

    fun centreX(width: Float): Float =
        when (edge) {
            Edge.TOP, Edge.BOTTOM -> width * along
            Edge.RIGHT -> width
            Edge.LEFT, Edge.OFF -> 0f
        }

    fun centreY(height: Float): Float =
        when (edge) {
            Edge.LEFT, Edge.RIGHT -> height * along
            Edge.BOTTOM -> height
            Edge.TOP, Edge.OFF -> 0f
        }

    /** Where the fixed indicator points, in screen degrees (0 = right, 90 = down): into the screen. */
    val indicatorDeg: Float
        get() =
            when (edge) {
                Edge.TOP -> {
                    if (!atCorner) {
                        DOWN
                    } else if (nearStart) {
                        TOP_LEFT
                    } else {
                        TOP_RIGHT
                    }
                }

                Edge.RIGHT -> {
                    if (!atCorner) {
                        LEFT
                    } else if (nearStart) {
                        TOP_RIGHT
                    } else {
                        BOTTOM_RIGHT
                    }
                }

                Edge.BOTTOM -> {
                    if (!atCorner) {
                        UP
                    } else if (nearStart) {
                        BOTTOM_LEFT
                    } else {
                        BOTTOM_RIGHT
                    }
                }

                Edge.LEFT -> {
                    if (!atCorner) {
                        RIGHT
                    } else if (nearStart) {
                        TOP_LEFT
                    } else {
                        BOTTOM_LEFT
                    }
                }

                Edge.OFF -> {
                    0f
                }
            }

    /** Half the visible arc: a quarter turn at a corner, a half turn along an edge. */
    val halfSpanDeg: Float get() = if (atCorner) QUARTER / 2 else QUARTER

    /**
     * The sign of a clockwise drag, chosen so that sweeping up raises the value on the sides and sweeping
     * right raises it along the top and bottom. At a corner, up wins.
     */
    val direction: Int
        get() =
            when (edge) {
                Edge.RIGHT -> {
                    1
                }

                Edge.LEFT -> {
                    -1
                }

                Edge.TOP, Edge.BOTTOM -> {
                    if (atCorner) {
                        (if (nearStart) -1 else 1)
                    } else if (edge == Edge.TOP) {
                        -1
                    } else {
                        1
                    }
                }

                Edge.OFF -> {
                    1
                }
            }

    companion object {
        const val CORNER_SNAP = 0.02f
        private const val HALF = 0.5f
        private const val QUARTER = 90f
        private const val RIGHT = 0f
        private const val DOWN = 90f
        private const val LEFT = 180f
        private const val UP = 270f
        private const val TOP_LEFT = 45f
        private const val TOP_RIGHT = 135f
        private const val BOTTOM_RIGHT = 225f
        private const val BOTTOM_LEFT = 315f
    }
}
