package me.akshitbansal.edgepad.surface

/**
 * Which trackpad hints are still worth showing. A hint is shown until its gesture has been used once, then it
 * fades for good. [seen] is a bitmask by slot (finger count minus one, then [HOLD]); [save] stores it.
 */
class HintFade(
    private var seen: Int,
    private val save: (Int) -> Unit,
) {
    fun visible(slot: Int): Boolean = seen and (1 shl slot) == 0

    /** The gesture in [slot] was used. Returns true when that changed what is shown. */
    fun used(slot: Int): Boolean {
        if (slot < 0 || !visible(slot)) return false
        seen = seen or (1 shl slot)
        save(seen)
        return true
    }

    companion object {
        /** The slot of press-and-hold to drag; slots 0 to 3 are one to four fingers. */
        const val HOLD = 4
    }
}
