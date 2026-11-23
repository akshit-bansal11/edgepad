package me.akshitbansal.edgepad.surface

/** How the battery strip's text is shrunk to fit its chips, kept apart from the drawing so it can be tested. */
internal object StripFit {
    /** The scale the text is drawn at: the [needed] one to fit, but never past 1 or under [floor]. */
    fun scale(
        needed: Float,
        floor: Float,
    ): Float = needed.coerceIn(floor, 1f)

    /** Whether the device icons go: the text would otherwise have to shrink under [floor]. */
    fun dropsIcons(
        needed: Float,
        floor: Float,
    ): Boolean = needed < floor
}
