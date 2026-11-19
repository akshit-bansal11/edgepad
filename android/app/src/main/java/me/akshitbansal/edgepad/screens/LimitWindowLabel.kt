package me.akshitbansal.edgepad.screens

import android.content.Context
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.link.LimitEntry
import me.akshitbansal.edgepad.link.LimitWindow

/** How a limit window is named: which string, and what goes in it. The screen turns the key into a resource. */
data class LimitLabel(
    val key: Key,
    val number: Int = 0,
    val text: String = "",
) {
    enum class Key {
        WEEK,
        MONTH,
        SPEND,
        CONTEXT,
        MINUTES,
        HOURS,
        DAYS,
        TEXT,
    }
}

/** Pure, with no Context, so a table can test it. */
object LimitWindowLabel {
    private const val WEEK = 10_080
    private const val MONTH = 43_200
    private const val HOUR = 60
    private const val DAY = 1_440
    private const val SPEND_LIMIT = "spend_limit"

    fun of(window: LimitWindow): LimitLabel =
        when (window) {
            LimitWindow.Context -> {
                LimitLabel(LimitLabel.Key.CONTEXT)
            }

            is LimitWindow.Named -> {
                if (window.name == SPEND_LIMIT) {
                    LimitLabel(LimitLabel.Key.SPEND)
                } else {
                    LimitLabel(LimitLabel.Key.TEXT, text = window.name)
                }
            }

            is LimitWindow.Minutes -> {
                when (window.minutes) {
                    WEEK -> LimitLabel(LimitLabel.Key.WEEK)
                    MONTH -> LimitLabel(LimitLabel.Key.MONTH)
                    else -> duration(window.minutes)
                }
            }
        }

    /** Shortest window first, then the named limits, then the context. */
    val shortestFirst: Comparator<LimitEntry> =
        compareBy<LimitEntry> {
            when (it.window) {
                is LimitWindow.Minutes -> 0
                is LimitWindow.Named -> 1
                LimitWindow.Context -> 2
            }
        }.thenBy { (it.window as? LimitWindow.Minutes)?.minutes ?: 0 }

    /** A length of time: minutes under an hour, hours under a day, else days. */
    fun duration(minutes: Int): LimitLabel =
        when {
            minutes < HOUR -> LimitLabel(LimitLabel.Key.MINUTES, minutes)
            minutes < DAY -> LimitLabel(LimitLabel.Key.HOURS, minutes / HOUR)
            else -> LimitLabel(LimitLabel.Key.DAYS, minutes / DAY)
        }
}

/** The words for a label, from the app's strings. */
fun LimitLabel.words(context: Context): String =
    when (key) {
        LimitLabel.Key.WEEK -> context.getString(R.string.ai_win_week)
        LimitLabel.Key.MONTH -> context.getString(R.string.ai_win_month)
        LimitLabel.Key.SPEND -> context.getString(R.string.ai_win_spend)
        LimitLabel.Key.CONTEXT -> context.getString(R.string.ai_ctx)
        LimitLabel.Key.MINUTES -> context.getString(R.string.dur_min, number)
        LimitLabel.Key.HOURS -> context.getString(R.string.dur_hr, number)
        LimitLabel.Key.DAYS -> context.getString(R.string.dur_day, number)
        LimitLabel.Key.TEXT -> text
    }
