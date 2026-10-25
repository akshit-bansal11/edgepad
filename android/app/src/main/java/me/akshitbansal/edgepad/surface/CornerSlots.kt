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
 * Where the label and number of one half of a split corner sit. Pure, so the promise that none of the eight
 * draws over another, the top buttons or the media is tested on the JVM.
 *
 * Eight numbers at the four-dial size, each pushed in from its own indicator, fought over the same square
 * inside every bend and read as a pile. Split mode uses one rigid system instead:
 *
 * - Every unit, a small label over a smaller number, lives inside one frame inset [anchor]'s depth from every
 *   edge, clear of the longest mark the height allows, so no text crosses a ruler and every corner is inset
 *   the same.
 * - A corner's two units form one column flush with that frame: left aligned in a left corner, right aligned
 *   in a right one, so the column's outer edge never moves as a number changes width.
 * - The top or bottom edge's unit is the row nearest that edge and the side edge's unit the next row in, so
 *   each sits beside its own ruler and the order is the same in all four corners.
 * - Each row is reserved for the held size and the widest number, so arming a dial or a value changing moves
 *   nothing. A half with no dial draws nothing, and the other row stays where it is.
 * - A top corner's column drops below the button row only when its reserved width would reach under it.
 *
 *     +--[ruler]---------   ( buttons )
 *     |  APPS               the top edge's unit, depth in from both edges
 *     |  -
 *    [r] VOL                the side edge's unit, one row in
 *    [u] 32
 *     |
 */
object SplitLabel {
    /**
     * Writes where the unit of the half whose indicator is at path length [centre] starts into [out] as x, top,
     * align: x is the edge the unit is aligned on, top is its block's top, and align is 1 for left aligned and
     * -1 for right. [pt] is scratch. [depth] is the frame's inset, [block] the label plus the held number, [gap]
     * the space between rows and below the buttons, [wide] the widest number's width, and [row] the top
     * buttons' left, right and bottom, all zero when there are none.
     */
    fun anchor(
        perimeter: Perimeter,
        centre: Float,
        depth: Float,
        block: Float,
        gap: Float,
        wide: Float,
        row: FloatArray,
        pt: FloatArray,
        out: FloatArray,
    ) {
        perimeter.point(centre, pt)
        val left = pt[0] < perimeter.width / 2
        val top = pt[1] < perimeter.height / 2
        val x = if (left) depth else perimeter.width - depth
        var edge = depth
        if (top && row[2] > 0f) {
            val from = if (left) x else x - wide
            if (from + wide > row[0] && from < row[1]) edge = maxOf(depth, row[2] + gap)
        }
        val inward = edge + if (isSideEdge(pt[2], pt[3])) block + gap else 0f
        out[0] = x
        out[1] = if (top) inward else perimeter.height - inward - block
        out[2] = if (left) 1f else -1f
    }

    /** Whether a normal belongs to a left or right edge. */
    fun isSideEdge(
        nx: Float,
        ny: Float,
    ): Boolean = abs(nx) > abs(ny)
}
