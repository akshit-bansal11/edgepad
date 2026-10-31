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
 * draws over another or the top buttons is tested on the JVM.
 *
 * Each half's unit, a small label over a smaller number, sits at the far end of its own ruler, the end away
 * from the bend, [anchor]'s depth in from that ruler's edge, so it is clear of the longest mark the height
 * allows and lies back over the ruler's span, flush with its end:
 *
 * - A side edge's unit is aligned on that edge's frame line, its top at the end in a bottom corner and its
 *   bottom at the end in a top one.
 * - A top or bottom edge's unit is depth in from that edge, right aligned at the end in a left corner and
 *   left aligned in a right one.
 * - Each unit is reserved the held size and the widest number, so arming a dial or a value changing moves
 *   nothing, and kept on its own corner's half of the edge, which only bites when two rulers cross.
 * - Where a short ruler would bring a side unit over the top or bottom edge's band, the side unit moves out
 *   along its edge until it clears that band by a gap; the top or bottom unit stays put.
 * - A top edge's unit moves toward its corner until it clears the buttons' keep-out.
 *
 *     +--[ruler]----   ( buttons )
 *     |       APPS      the top edge's unit, at its ruler's far end
 *    [r]
 *    [u]
 *     |  VOL            the side edge's unit, at its ruler's far end
 *        32
 */
object SplitLabel {
    /**
     * The path length of the far end of the half centred at [centre] that reaches [after] clockwise and
     * [before] anticlockwise: whichever end lies further from the bend.
     */
    fun farEnd(
        perimeter: Perimeter,
        centre: Float,
        after: Float,
        before: Float,
    ): Float {
        val ahead = centre + after
        val back = centre - before
        return if (perimeter.fromBend(ahead) >= perimeter.fromBend(back)) ahead else back
    }

    /**
     * Writes where the unit of the half whose indicator is at path length [centre] and whose ruler ends at [end]
     * ([farEnd]) starts into [out] as x, top, align: x is the edge the unit is aligned on, top is its block's top,
     * and align is 1 for left aligned and -1 for right. [pt] is scratch. [depth] is the inset from the edge,
     * [block] the label plus the held number, [wide] the widest number's width, [gap] the space kept between
     * units, and [keepOut] the buttons' left and right, equal when there are none.
     */
    fun anchor(
        perimeter: Perimeter,
        centre: Float,
        end: Float,
        depth: Float,
        block: Float,
        wide: Float,
        gap: Float,
        keepOut: FloatArray,
        pt: FloatArray,
        out: FloatArray,
    ) {
        val w = perimeter.width
        val h = perimeter.height
        perimeter.point(centre, pt)
        val left = pt[0] < w / 2
        val top = pt[1] < h / 2
        val side = isSideEdge(pt[2], pt[3])
        perimeter.point(end, pt)
        if (side) {
            // Clear of the top or bottom edge's band, which wins over the end and the corner's own half.
            val clear = depth + block + gap
            out[0] = if (left) depth else w - depth
            out[1] =
                if (top) {
                    maxOf(minOf(pt[1], h / 2 - gap / 2), clear + block) - block
                } else {
                    minOf(maxOf(pt[1], h / 2 + gap / 2), h - clear - block)
                }
        } else {
            val buttons = top && keepOut[1] > keepOut[0]
            out[0] =
                if (left) {
                    val x = minOf(pt[0], w / 2 - gap / 2)
                    maxOf(if (buttons) minOf(x, keepOut[0]) else x, wide)
                } else {
                    val x = maxOf(pt[0], w / 2 + gap / 2)
                    minOf(if (buttons) maxOf(x, keepOut[1]) else x, w - wide)
                }
            out[1] = if (top) depth else h - depth - block
        }
        out[2] = if (side == left) 1f else -1f
    }

    /** Whether a normal belongs to a left or right edge. */
    fun isSideEdge(
        nx: Float,
        ny: Float,
    ): Boolean = abs(nx) > abs(ny)
}
