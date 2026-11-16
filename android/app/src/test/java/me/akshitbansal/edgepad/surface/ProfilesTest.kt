package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ProfilesTest {
    private val custom =
        Profile(
            listOf(DialKind.ZOOM, null, DialKind.MEDIA, DialKind.TABS),
            mapOf(Gesture.THREE_TAP to GestureAction.PLAY_PAUSE),
        )

    @Test
    fun roundTrip() {
        val set = Profiles().with("chrome.exe", "Chrome", custom).with("vlc.exe", "VLC", Profile())
        val back = Profiles(set.encode())
        assertEquals(custom, back.forApp("chrome.exe"))
        assertEquals(Profile(), back.forApp("vlc.exe"))
        assertEquals(listOf("chrome.exe", "vlc.exe"), back.all.map { it.first.token })
        assertEquals("Chrome", back.all.first().first.name)
    }

    @Test
    fun allDefaultsIsAProfileWithNoOverrides() {
        val back = Profiles(Profiles().with("vlc.exe", "VLC", Profile()).encode())
        assertTrue(back.forApp("vlc.exe")!!.isDefault)
    }

    @Test
    fun garbageIsEmpty() {
        assertTrue(Profiles("not a profile").all.isEmpty())
        assertTrue(Profiles("bad token\u001FName\u001F-\u001F").all.isEmpty())
    }

    @Test
    fun twentyFifthProfileIsRefused() {
        var set = Profiles()
        for (i in 0 until Profiles.MAX + 1) set = set.with("app$i", "App $i", Profile())
        assertEquals(Profiles.MAX, set.all.size)
        assertNull(set.forApp("app${Profiles.MAX}"))
    }

    @Test
    fun existingProfileCanBeChangedAtTheCap() {
        var set = Profiles()
        for (i in 0 until Profiles.MAX) set = set.with("app$i", "App", Profile())
        assertEquals(custom, set.with("app0", "App", custom).forApp("app0"))
    }

    @Test
    fun unknownNamesAreIgnored() {
        val raw = "a.exe\u001FA\u001F-\u001FTHREE_TAP=PLAY_PAUSE,NOPE=MUTE,FOUR_TAP=NOPE"
        val profile = Profiles(raw).forApp("a.exe")!!
        assertEquals(mapOf(Gesture.THREE_TAP to GestureAction.PLAY_PAUSE), profile.gestures)
        assertNull(Profiles("a.exe\u001FA\u001FZOOM,NOPE,NONE,NONE\u001F").forApp("a.exe")!!.corners)
    }

    @Test
    fun wrongLengthCornersFollowTheDefault() {
        val profile = Profiles("a.exe\u001FA\u001FZOOM,NONE\u001F").forApp("a.exe")!!
        assertNull(profile.corners)
        assertEquals(DialKind.VOLUME, profile.corner(0, DialKind.VOLUME))
    }

    @Test
    fun overridesResolveOverTheDefault() {
        assertEquals(DialKind.ZOOM, custom.corner(0, DialKind.VOLUME))
        assertNull(custom.corner(1, DialKind.BRIGHTNESS))
        assertEquals(GestureAction.PLAY_PAUSE, custom.gesture(Gesture.THREE_TAP, GestureAction.SEARCH))
        assertEquals(GestureAction.SEARCH, custom.gesture(Gesture.FOUR_TAP, GestureAction.SEARCH))
    }

    @Test
    fun withoutRemoves() {
        val set = Profiles().with("a.exe", "A", Profile()).without("a.exe")
        assertTrue(set.all.isEmpty())
    }
}
