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
}
