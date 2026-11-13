package me.akshitbansal.edgepad.surface

import android.content.Context
import android.icu.text.MeasureFormat
import android.icu.text.RelativeDateTimeFormatter
import android.icu.util.Measure
import android.icu.util.MeasureUnit
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

    /** "1 minute, 24 seconds" in the person's language: the unit words come from the platform, not from us. */
    fun remaining(
        context: Context,
        seconds: Int,
    ): String {
        val s = seconds.coerceAtLeast(0)
        val parts =
            listOf(
                Measure(s / HOUR, MeasureUnit.HOUR),
                Measure(s % HOUR / MINUTE, MeasureUnit.MINUTE),
                Measure(s % MINUTE, MeasureUnit.SECOND),
            ).filter { it.number.toInt() != 0 }.ifEmpty { listOf(Measure(0, MeasureUnit.SECOND)) }
        return MeasureFormat
            .getInstance(context.resources.configuration.locales[0], MeasureFormat.FormatWidth.WIDE)
            .formatMeasures(*parts.toTypedArray())
    }

    /** "5 minutes ago" in the person's language, in the largest whole unit. */
    fun ago(
        context: Context,
        seconds: Int,
    ): String {
        val s = seconds.coerceAtLeast(0)
        val (count, unit) =
            when {
                s >= HOUR -> s / HOUR to RelativeDateTimeFormatter.RelativeUnit.HOURS
                s >= MINUTE -> s / MINUTE to RelativeDateTimeFormatter.RelativeUnit.MINUTES
                else -> s to RelativeDateTimeFormatter.RelativeUnit.SECONDS
            }
        return RelativeDateTimeFormatter
            .getInstance(context.resources.configuration.locales[0])
            .format(count.toDouble(), RelativeDateTimeFormatter.Direction.LAST, unit)
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
