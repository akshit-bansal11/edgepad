package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class AppLogoPickTest {
    @Test
    fun theBundledLogoWinsOverTheLaptopsIcons() {
        assertEquals("bundled", pickIcon("bundled", "app", "art"))
    }

    @Test
    fun theLaptopsIconForTheAppComesBeforeTheArtwork() {
        assertEquals("app", pickIcon(null, "app", "art"))
    }

    @Test
    fun theArtworkIsTheLastResort() {
        assertEquals("art", pickIcon<String>(null, null, "art"))
    }

    @Test
    fun withNothingTheNoteIsDrawn() {
        assertNull(pickIcon<String>(null, null, null))
    }
}
