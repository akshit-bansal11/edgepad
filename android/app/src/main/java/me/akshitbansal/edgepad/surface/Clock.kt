package me.akshitbansal.edgepad.surface

import java.util.Locale

/** A track position as a player shows it: 1:24, or 1:02:05 past an hour. */
object Clock {
    private const val MINUTE = 60
    private const val HOUR = 3600

    fun format(seconds: Int): String {
        val s = seconds.coerceAtLeast(0)
        // Digits and a colon in every language, as players show them.
        return if (s >= HOUR) {
            "%d:%02d:%02d".format(Locale.ROOT, s / HOUR, s % HOUR / MINUTE, s % MINUTE)
        } else {
            "%d:%02d".format(Locale.ROOT, s / MINUTE, s % MINUTE)
        }
    }
}

/** What the line under the now-playing title says. */
internal enum class SubLine { APP_AND_TIME, TIME, APP }

/** The time is left off while a Scrub dial is built: that dial's own value text shows it. */
internal fun subLine(
    app: String,
    duration: Int,
    scrubOnADial: Boolean,
): SubLine =
    when {
        duration <= 0 || scrubOnADial -> SubLine.APP
        app.isNotEmpty() -> SubLine.APP_AND_TIME
        else -> SubLine.TIME
    }
