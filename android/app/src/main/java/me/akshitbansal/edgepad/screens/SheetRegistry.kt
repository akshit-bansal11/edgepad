package me.akshitbansal.edgepad.screens

/**
 * The sheets and popups bound to the selected laptop, by name. A switch of laptop calls [dismissAll] so none
 * keeps showing the old laptop's battery, copies or approvals; opening one again under the same name replaces
 * the earlier entry.
 */
class SheetRegistry {
    private val open = LinkedHashMap<String, () -> Unit>()

    val size: Int get() = open.size

    fun add(
        name: String,
        dismiss: () -> Unit,
    ) {
        open[name] = dismiss
    }

    fun remove(name: String) {
        open.remove(name)
    }

    /** Closes every entry, clearing the registry first so a dismiss that calls [remove] cannot disturb the walk. */
    fun dismissAll() {
        val all = open.values.toList()
        open.clear()
        all.forEach { it() }
    }
}
