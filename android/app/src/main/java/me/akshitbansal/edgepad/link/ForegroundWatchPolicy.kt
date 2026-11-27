package me.akshitbansal.edgepad.link

/**
 * Whether the phone asks the laptop to report the app in front. Pure, so a JVM test holds its truth table. The
 * Profiles screen needs the report even with no profile yet, because its add button is disabled until it has one.
 */
object ForegroundWatchPolicy {
    fun wanted(
        connected: Boolean,
        offered: Boolean,
        profilesOn: Boolean,
        hasProfiles: Boolean,
        onProfilesScreen: Boolean,
        automatic: Boolean = false,
    ): Boolean = connected && offered && (onProfilesScreen || automatic || (profilesOn && hasProfiles))
}
