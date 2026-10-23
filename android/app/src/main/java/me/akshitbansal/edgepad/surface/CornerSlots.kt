package me.akshitbansal.edgepad.surface

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
