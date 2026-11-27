package me.akshitbansal.edgepad.link

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ForegroundWatchPolicyTest {
    private fun wanted(
        connected: Boolean = true,
        offered: Boolean = true,
        profilesOn: Boolean = false,
        hasProfiles: Boolean = false,
        onProfilesScreen: Boolean = false,
        automatic: Boolean = false,
    ) = ForegroundWatchPolicy.wanted(connected, offered, profilesOn, hasProfiles, onProfilesScreen, automatic)

    @Test
    fun noProfilesWithTheScreenOpenWatches() = assertTrue(wanted(profilesOn = true, onProfilesScreen = true))

    @Test
    fun noProfilesElsewhereDoesNot() = assertFalse(wanted(profilesOn = true))

    @Test
    fun aProfileWithProfilesOnWatches() = assertTrue(wanted(profilesOn = true, hasProfiles = true))

    @Test
    fun notConnectedDoesNot() = assertFalse(wanted(connected = false, profilesOn = true, onProfilesScreen = true))

    @Test
    fun notOfferedDoesNot() = assertFalse(wanted(offered = false, profilesOn = true, hasProfiles = true))

    @Test
    fun automaticAloneWatches() = assertTrue(wanted(automatic = true))
}
