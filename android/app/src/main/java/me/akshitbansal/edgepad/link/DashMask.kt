package me.akshitbansal.edgepad.link

/**
 * The bitmask the phone sends in DASH_WATCH: what it draws right now, so the laptop sends only that. Pure, so a JVM
 * test holds its truth table.
 */
object DashMask {
    /** Bit 0: the System or Network widget on the surface. */
    const val STATS_WIDGET = 1

    /** Bit 1: the System screen. */
    const val SYSTEM_SCREEN = 2

    /** Bit 2: the Weather widget. */
    const val WEATHER_WIDGET = 4

    /** Bit 3: the AI widget or the AI screen. */
    const val AI = 8

    /** Bit 4: the GitHub screen. */
    const val GITHUB_SCREEN = 16

    /** The dashboard screens that watch by themselves. */
    enum class Screen { SYSTEM, AI, GITHUB }

    /**
     * What is drawn. Nothing is watched ([active] false: the app is paused or the link is down) gives 0. The widgets
     * count only while the surface is shown ([onSurface]); a dashboard [screen] counts for itself.
     */
    fun of(
        active: Boolean,
        onSurface: Boolean,
        statsWidget: Boolean,
        weatherWidget: Boolean,
        aiWidget: Boolean,
        screen: Screen?,
    ): Int {
        if (!active) return 0
        var mask = 0
        if (onSurface) {
            if (statsWidget) mask = mask or STATS_WIDGET
            if (weatherWidget) mask = mask or WEATHER_WIDGET
            if (aiWidget) mask = mask or AI
        }
        when (screen) {
            Screen.SYSTEM -> mask = mask or SYSTEM_SCREEN
            Screen.AI -> mask = mask or AI
            Screen.GITHUB -> mask = mask or GITHUB_SCREEN
            null -> Unit
        }
        return mask
    }
}
