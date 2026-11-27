package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.AppKindToken
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class AutoProfilesTest {
    @Test
    fun eachKindHasItsDefaultDials() {
        assertEquals(
            listOf(DialKind.MEDIA, DialKind.VOLUME, DialKind.ZOOM, DialKind.BRIGHTNESS),
            AutoProfiles.default(AppKindToken.MEDIA).corners,
        )
        assertEquals(
            listOf(DialKind.SCROLL_H, DialKind.SCROLL, DialKind.ZOOM, DialKind.VOLUME),
            AutoProfiles.default(AppKindToken.TIMELINE).corners,
        )
        assertEquals(
            listOf(DialKind.SCROLL, DialKind.TABS, DialKind.ZOOM, DialKind.VOLUME),
            AutoProfiles.default(AppKindToken.BROWSER).corners,
        )
    }

    @Test
    fun aStoredProfileOverridesTheDefault() {
        val stored = Profile(corners = listOf(null, null, null, DialKind.REFRESH))
        val raw = Profiles().with(AppKindToken.BROWSER.token, "", stored).encode()
        assertEquals(stored, AutoProfiles.profile(raw, AppKindToken.BROWSER))
        assertEquals(AutoProfiles.default(AppKindToken.MEDIA), AutoProfiles.profile(raw, AppKindToken.MEDIA))
    }

    @Test
    fun customWinsOverAutomaticOverNone() {
        val custom = Profile(corners = listOf(null, null, null, null))
        assertEquals(custom, AutoProfiles.resolve(custom, true, true, AppKindToken.MEDIA, ""))
        assertEquals(
            AutoProfiles.default(AppKindToken.MEDIA),
            AutoProfiles.resolve(null, true, true, AppKindToken.MEDIA, ""),
        )
        assertNull(AutoProfiles.resolve(null, true, false, AppKindToken.MEDIA, ""))
        assertNull(AutoProfiles.resolve(null, true, true, null, ""))
    }

    @Test
    fun nothingAppliesWhileTheMasterSwitchIsOff() {
        val custom = Profile(corners = listOf(null, null, null, null))
        assertNull(AutoProfiles.resolve(null, false, true, AppKindToken.MEDIA, ""))
        assertNull(AutoProfiles.resolve(custom, false, true, AppKindToken.MEDIA, ""))
    }
}
