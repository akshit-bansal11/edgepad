package me.akshitbansal.edgepad.surface

import kotlin.math.abs

/**
 * How a corner splits into two slots, one on each edge it joins ([Perimeter.lengthAtSlot]), and which
 * half carries the corner's existing setting. Slots are numbered clockwise, two a corner, so slot 0 is
 * the left edge of the top-left corner and slot 1 its top edge. Pure, so it is tested on the JVM.
 */
object CornerSlots {
    /** The corner a slot belongs to. */
    fun corner(slot: Int): Int = slot / 2

    /**
     * The half of [corner] that inherits what the unsplit corner already stored. It is the side-edge half:
     * thumbs grip the side edges, and the top edge is where the button row lives. That is slot 0 (left),
     * 3 (right), 4 (right) and 7 (left) for corners 0 to 3.
     */
    fun inheritedSlot(corner: Int): Int =
        when (corner) {
            0 -> 0
            1 -> 3
            2 -> 4
            else -> 7
        }

    /**
     * The corner whose stored kind [slot] falls back on when it has none of its own: its corner for the
     * inherited half, null for the other half, which starts empty.
     */
    fun fallbackFor(slot: Int): Int? = corner(slot).takeIf { inheritedSlot(it) == slot }
}

/**
 * Where the label and number of one half of a split corner sit. Pure, so the promise that the two halves of a
 * corner never draw over each other is tested on the JVM at the smallest screen.
 *
 * The geometry, for a top-left corner with a half on each edge. A half's indicator sits on its own edge, a
 * distance `along` from the corner point (the bend's radius plus the offset past the arc). Its text goes
 * inward of the indicator, clear of the longest mark: [depth] in from the edge.
 *
 *     corner
 *       +-----------------------------> top edge
 *       |      ind(top) = (along, 0)
 *       |        [ TOP ]   text from y = depth to y = depth + block, centred on the indicator
 *       |  [SIDE]          text from x = depth, left aligned, centred on ind(side) = (0, along)
 *       v
 *   side edge
 *
 * Both texts want the square just inside the bend, and with `along` about the same on both edges they land
 * on it together. The top-edge text keeps its place, under its indicator and clear of the top row of buttons.
 * The side-edge text slides down its own edge, away from the bend, until its block starts a [separation]
 * below the top-edge block's end: centre >= depth + 1.5 * block + separation. Separation is vertical, so it
 * does not depend on how wide either text is, and it is the cheaper way out because a block is narrower
 * than it is wide. The same holds at the bottom corners, mirrored, so the side-edge text slides up, and
 * on the right edge, so the text is right aligned. The block is sized for the held value, the larger one, so
 * arming a dial moves nothing.
 */
object SplitLabel {
    /**
     * Writes the centre of the text block of the half whose indicator is at path length [centre] into [out]
     * as x, y. [pt] is scratch for the point. [depth] is the distance in from the edge where the text starts,
     * and [block] the height of the label plus the number. A side-edge block is aligned by its inner edge at
     * x, so it does not move as the number's width changes; a top or bottom one is centred on x.
     */
    fun anchor(
        perimeter: Perimeter,
        centre: Float,
        depth: Float,
        block: Float,
        separation: Float,
        pt: FloatArray,
        out: FloatArray,
    ) {
        perimeter.point(centre, pt)
        val nx = pt[2]
        val ny = pt[3]
        val side = isSideEdge(nx, ny)
        val here = perimeter.fromBend(centre)
        // Whether the path's clockwise direction, (ny, -nx), leads away from the corner this half belongs to.
        val away = if (perimeter.fromBend(centre + 1f) > here) 1f else -1f
        var slide = 0f
        if (side) {
            val needed = depth + 1.5f * block + separation
            slide = maxOf(0f, needed - (here + perimeter.radius))
        }
        val inward = depth + if (side) 0f else block / 2
        out[0] = pt[0] + nx * inward + away * ny * slide
        out[1] = pt[1] + ny * inward - away * nx * slide
    }

    /** Whether a normal belongs to a left or right edge. */
    fun isSideEdge(
        nx: Float,
        ny: Float,
    ): Boolean = abs(nx) > abs(ny)
}
