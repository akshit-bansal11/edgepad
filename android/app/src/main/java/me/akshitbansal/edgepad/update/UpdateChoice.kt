package me.akshitbansal.edgepad.update

import android.content.Context

/**
 * What the user chose about an update notice, and which version has already been announced by notification.
 * Kept in its own preferences file; nothing here runs on a timer.
 */
class UpdateChoice(
    context: Context,
) {
    private val prefs = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)

    /** Whether the notice for [version] may be shown at [now]. */
    fun shouldShow(
        version: String,
        now: Long,
    ): Boolean = shouldShow(prefs.getString(SKIPPED, null), prefs.getLong(LATER_UNTIL, 0L), version, now)

    /** Never offer [version] again; a newer one is still offered. */
    fun skip(version: String) = prefs.edit().putString(SKIPPED, version).apply()

    /** Hide the notice until the next daily check. */
    fun later(now: Long) = prefs.edit().putLong(LATER_UNTIL, now + Updates.CHECK_EVERY_MS).apply()

    /** Whether a system notification was already posted for [version]. */
    fun wasNotified(version: String): Boolean = prefs.getString(NOTIFIED, null) == version

    fun markNotified(version: String) = prefs.edit().putString(NOTIFIED, version).apply()

    companion object {
        private const val NAME = "update_choice"
        private const val SKIPPED = "skipped"
        private const val LATER_UNTIL = "laterUntil"
        private const val NOTIFIED = "notified"

        /** The rule on its own, so it can be tested without preferences. */
        fun shouldShow(
            skipped: String?,
            laterUntil: Long,
            version: String,
            now: Long,
        ): Boolean = skipped != version && now >= laterUntil
    }
}
