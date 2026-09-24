package me.akshitbansal.edgepad.gamepad

import android.content.Context

/**
 * Every layout the phone knows and which one is in play, as one value.
 *
 * Pure — no Android type reaches it — so the rules that actually decide what the user sees (what a name
 * collision costs, what is left current after a deletion, what RESET goes back to) are tested on the JVM
 * the way the layout's own encoding is. [GamepadStore] is then nothing but the two strings this reads
 * from and writes to SharedPreferences.
 */
data class GamepadLibrary(
    val saved: List<GamepadLayout>,
    val currentName: String,
) {
    /**
     * What a picker lists: the layouts the user has saved, then the layouts built into Edgepad that none
     * of them has taken the name of.
     *
     * Saving under a built-in name therefore shadows that layout rather than adding a second row called
     * the same thing, which is what dragging a control on "Xbox" does the first time; deleting the shadow
     * brings the built-in one straight back, and so does RESET. A built-in layout can never be removed,
     * because it lives in the code rather than in the store, so this list is never empty.
     */
    val all: List<GamepadLayout>
        get() = saved + GamepadLayout.presets.filter { preset -> saved.none { it.name == preset.name } }

    /** Never null: [all] always holds at least the built-in layouts, so there is always something to play. */
    val current: GamepadLayout
        get() = all.firstOrNull { it.name == currentName } ?: all.first()

    /** Plays the layout called [name]; a name nothing answers to changes nothing. */
    fun choose(name: String): GamepadLibrary = if (all.any { it.name == name }) copy(currentName = name) else this

    /** Stores [layout] under its own name, replacing the saved layout of that name, and plays it. */
    fun save(layout: GamepadLayout): GamepadLibrary =
        GamepadLibrary(saved.filterNot { it.name == layout.name } + layout, layout.name)

    /**
     * [layout] again under [wanted] — the "save this as Elden Ring" the whole feature is for. The name is
     * cleaned, and numbered if something already answers to it; see [GamepadLayout.freeName] for why a
     * collision never overwrites. A [wanted] with nothing usable left in it changes nothing.
     */
    fun saveAs(
        layout: GamepadLayout,
        wanted: String,
    ): GamepadLibrary {
        val cleaned = GamepadLayout.clean(wanted, GamepadLayout.MAX_NAME) ?: return this
        return save(layout.copy(name = GamepadLayout.freeName(cleaned, all.map { it.name })))
    }

    /**
     * The layout called [from] under [wanted] instead, and played. Renaming a built-in layout saves a copy
     * under the new name and leaves the built-in one where it was, because nothing can remove something
     * that lives in the code — which is also the honest outcome, since the built-in layout is what the
     * copy was made from.
     */
    fun rename(
        from: String,
        wanted: String,
    ): GamepadLibrary {
        val layout = all.firstOrNull { it.name == from } ?: return this
        val cleaned = GamepadLayout.clean(wanted, GamepadLayout.MAX_NAME) ?: return this
        val taken = all.map { it.name }.filterNot { it == from }
        val renamed = layout.copy(name = GamepadLayout.freeName(cleaned, taken))
        return copy(saved = saved.filterNot { it.name == from }).save(renamed)
    }

    /**
     * Forgets the saved layout called [name].
     *
     * What is played afterwards is [name] itself when a built-in layout of that name has just come back
     * out from under it — deleting your edited "Xbox" leaves you on Edgepad's "Xbox", which is the only
     * answer that is not a surprise — and otherwise the first layout still on the list.
     */
    fun delete(name: String): GamepadLibrary {
        val left = copy(saved = saved.filterNot { it.name == name })
        if (left.all.any { it.name == left.currentName }) return left
        return left.copy(currentName = left.all.first().name)
    }

    /**
     * The current layout back to the way Edgepad ships it.
     *
     * A layout of the user's own has no shipped version to go back to, so RESET moves off it to the first
     * built-in layout and leaves it saved rather than silently deleting work nobody asked to lose.
     * Deleting is its own action, and it says so before it acts.
     */
    fun reset(): GamepadLibrary {
        val preset = GamepadLayout.presets.firstOrNull { it.name == currentName }
        if (preset == null) return choose(GamepadLayout.presets.first().name)
        return GamepadLibrary(saved.filterNot { it.name == preset.name }, preset.name)
    }
}

/**
 * Persists every gamepad layout the player has saved, and which one is in play, in the encoded plain-text
 * form. One key for the whole set and one for the name, read and written together the way
 * [me.akshitbansal.edgepad.Settings.shapes] is: a half-saved set of layouts is worse than none, and a
 * malformed one yields no layouts rather than the subset that happened to parse.
 */
class GamepadStore(
    context: Context,
) {
    private val prefs = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)

    init {
        // Before this release the pad kept exactly one layout, under [KEY_LAYOUT]. It becomes the first
        // saved layout, once, so an update does not throw away a pad that had already been arranged. A
        // layout older than 3.0 is refused by [GamepadLayout.decode] and leaves nothing to carry over,
        // which is the trade that decode's own comment already explains.
        if (!prefs.contains(KEY_LAYOUTS)) {
            val one = prefs.getString(KEY_LAYOUT, null)?.let { GamepadLayout.decode(it) }
            write(GamepadLibrary(listOfNotNull(one), one?.name ?: GamepadLayout.presets.first().name))
        }
    }

    private val library: GamepadLibrary
        get() {
            val saved = GamepadLayout.decodeAll(prefs.getString(KEY_LAYOUTS, "").orEmpty()) ?: emptyList()
            return GamepadLibrary(saved, prefs.getString(KEY_CURRENT, "").orEmpty())
        }

    /** The layout the pad plays and the editor opens. */
    val current: GamepadLayout
        get() = library.current

    /** Every layout that can be switched to, the user's own first and Edgepad's own after them. */
    val all: List<GamepadLayout>
        get() = library.all

    /** True when [name] is a layout Edgepad ships rather than one the user made, which cannot be deleted. */
    fun isBuiltIn(name: String): Boolean = GamepadLayout.presets.any { it.name == name }

    fun save(layout: GamepadLayout) = edit { it.save(layout) }

    fun saveAs(
        layout: GamepadLayout,
        name: String,
    ) = edit { it.saveAs(layout, name) }

    fun rename(
        from: String,
        to: String,
    ) = edit { it.rename(from, to) }

    fun delete(name: String) = edit { it.delete(name) }

    fun choose(name: String) = edit { it.choose(name) }

    fun reset() = edit { it.reset() }

    private fun edit(change: (GamepadLibrary) -> GamepadLibrary) {
        write(change(library))
    }

    private fun write(value: GamepadLibrary) {
        prefs
            .edit()
            .putString(KEY_LAYOUTS, GamepadLayout.encodeAll(value.saved))
            .putString(KEY_CURRENT, value.currentName)
            .apply()
    }

    private companion object {
        const val NAME = "edgepad.gamepad"

        /** The single layout a build before this one kept; read once, by the migration above. */
        const val KEY_LAYOUT = "layout"
        const val KEY_LAYOUTS = "layouts"
        const val KEY_CURRENT = "current"
    }
}
