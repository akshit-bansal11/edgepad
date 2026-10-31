package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.link.LaptopState

/**
 * The rules behind the macro deck, kept apart from the views so they can be tested. Everything here is
 * phone-side: the order only decides where a tile is drawn, and a tile still sends the laptop slot it
 * came from.
 */
object DeckLayout {
    /**
     * The (rows, columns) grids that fit [count] tiles. A row count is offered only if every row has a
     * tile in it: 15 tiles in 6 rows of 3 would leave the sixth empty, and that is the same grid as 5 rows.
     * Empty past the slot ceiling or below one, where there is nothing to lay out.
     */
    fun choices(count: Int): List<Pair<Int, Int>> {
        if (count !in 1..LaptopState.MACRO_SLOTS) return emptyList()
        return (1..count).mapNotNull { rows ->
            val columns = (count + rows - 1) / rows
            if ((rows - 1) * columns < count) rows to columns else null
        }
    }

    /**
     * The columns of the [choices] grid that has [rows] rows, or null when [rows] is not one of them. A stored
     * row count that the page size has since made impossible then reads as "not chosen" instead of as a grid
     * with an empty row.
     */
    fun columnsFor(
        count: Int,
        rows: Int,
    ): Int? = choices(count).firstOrNull { it.first == rows }?.second

    /**
     * Whether [rows] rows stand in [down] pixels at [touch] each, [gap] apart. A row under a touch target is not
     * drawn shorter, it is drawn past the bottom, and a pager has no scroll to reach it by.
     */
    fun rowsFit(
        rows: Int,
        down: Int,
        touch: Int,
        gap: Int,
    ): Boolean = rows * touch + (rows - 1) * gap <= down

    /** The side a picture is drawn at: [zoom] of the [box] it has room for, never under [minPx]. */
    fun iconSide(
        box: Int,
        zoom: Float,
        minPx: Int,
    ): Int = maxOf(minPx, (box * zoom).toInt())

    /**
     * The side of the area that answers for a tile's close cross, on a tile whose shorter side is [side]: a full
     * [touch] target where the tile has room for one, half the tile where it has not, so most of a small tile still
     * runs the macro, and never under [floor].
     */
    fun crossHit(
        side: Int,
        touch: Int,
        floor: Int,
    ): Int = minOf(touch, side / 2).coerceAtLeast(floor)

    /**
     * The pages [count] tiles fall on when a page holds [perPage]: each is the range of display positions it
     * shows. Every page is planned as a grid of [perPage] cells, so only the last can be short, and it leaves
     * its cells empty at the end rather than growing its tiles.
     */
    fun pages(
        count: Int,
        perPage: Int,
    ): List<IntRange> = (0 until count).chunked(perPage) { it.first()..it.last() }

    /**
     * Keys for [names] that tell equal names apart: `Chrome`, then `Chrome#2`. The laptop does not forbid
     * two macros with one name, and a stored order keyed by bare name could not say which is which.
     */
    fun keys(names: List<String>): List<String> {
        val seen = HashMap<String, Int>()
        return names.map { name ->
            val n = (seen[name] ?: 0) + 1
            seen[name] = n
            if (n == 1) name else "$name#$n"
        }
    }

    /**
     * Indexes into [current] in the order to draw them: macros the owner has already placed first, in
     * their stored order, then any the stored order has not seen, in laptop order. A stored key that no
     * longer matches (the macro was deleted or renamed) is dropped, which is why a renamed macro lands
     * at the end rather than keeping its place.
     */
    fun order(
        stored: List<String>,
        current: List<String>,
    ): List<Int> {
        val keys = keys(current)
        val placed = stored.mapNotNull { key -> keys.indexOf(key).takeIf { it >= 0 } }.distinct()
        return placed + keys.indices.filter { it !in placed }
    }

    /** [order] with the entry at [from] moved to [to]; unchanged if either is out of range. */
    fun move(
        order: List<Int>,
        from: Int,
        to: Int,
    ): List<Int> {
        if (from !in order.indices || to !in order.indices) return order
        return order.toMutableList().apply { add(to, removeAt(from)) }
    }

    /**
     * The slot whose cell holds the point ([x], [y]): cell i is [width] by [height] from ([lefts] i, [tops] i).
     * -1 in a gap between cells or outside all of them, which a drag reads as "stay where you are".
     */
    fun slotAt(
        x: Float,
        y: Float,
        lefts: IntArray,
        tops: IntArray,
        width: Int,
        height: Int,
    ): Int =
        lefts.indices.firstOrNull {
            x >= lefts[it] && x < lefts[it] + width && y >= tops[it] && y < tops[it] + height
        } ?: -1

    /**
     * Where each of [count] tiles stands while the one at [from] is held over [to]: entry p is the slot the
     * tile drawn at p slides to. It is [move] seen from the tiles rather than from the slots, so what the
     * drag shows is exactly what the drop will store.
     */
    fun places(
        count: Int,
        from: Int,
        to: Int,
    ): IntArray {
        val places = IntArray(count)
        move(List(count) { it }, from, to).forEachIndexed { slot, tile -> places[tile] = slot }
        return places
    }
}
