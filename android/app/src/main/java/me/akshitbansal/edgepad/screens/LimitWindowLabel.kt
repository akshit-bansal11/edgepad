package me.akshitbansal.edgepad.screens

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
            LimitWindow.Context -> LimitLabel(LimitLabel.Key.CONTEXT)
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

    /** A length of time: minutes under an hour, hours under a day, else days. */
    fun duration(minutes: Int): LimitLabel =
        when {
            minutes < HOUR -> LimitLabel(LimitLabel.Key.MINUTES, minutes)
            minutes < DAY -> LimitLabel(LimitLabel.Key.HOURS, minutes / HOUR)
            else -> LimitLabel(LimitLabel.Key.DAYS, minutes / DAY)
        }
}
