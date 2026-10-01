package me.akshitbansal.edgepad.update

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class UpdatesTest {
    private val tag = "https://github.com/akshit-bansal11/edgepad/releases/tag/v"

    @Test
    fun readsTheVersionFromTheTagRedirect() {
        assertEquals("3.2.1", Updates.versionIn(tag + "3.2.1"))
    }

    @Test
    fun refusesARedirectThatIsNotThisRepositorysTagPage() {
        assertNull(Updates.versionIn(null))
        assertNull(Updates.versionIn("https://github.com/someone-else/edgepad/releases/tag/v9.9.9"))
        assertNull(Updates.versionIn("https://github.com/akshit-bansal11/edgepad/releases"))
    }

    @Test
    fun refusesATagThatIsNotAPlainVersion() {
        assertNull(Updates.versionIn(tag + "3.2"))
        assertNull(Updates.versionIn(tag + "3.2.1/../../evil"))
        assertNull(Updates.versionIn(tag + "3.2.1-draft.4"))
    }

    @Test
    fun aReleasesApkIsNamedForItsVersion() {
        assertEquals(
            "https://github.com/akshit-bansal11/edgepad/releases/download/v3.2.1/Edgepad-3.2.1.apk",
            Updates.apkUrl("3.2.1"),
        )
    }

    @Test
    fun aLaterReleaseIsNewer() {
        assertTrue(Updates.isNewer("3.2.1", "3.2.0"))
        assertTrue(Updates.isNewer("3.10.0", "3.9.5"))
        assertTrue(Updates.isNewer("4.0.0", "3.99.99"))
    }

    @Test
    fun theSameOrAnEarlierReleaseIsNot() {
        assertFalse(Updates.isNewer("3.2.0", "3.2.0"))
        assertFalse(Updates.isNewer("3.1.9", "3.2.0"))
    }

    @Test
    fun aSuffixOnTheRunningBuildIsIgnored() {
        assertFalse(Updates.isNewer("3.3.0", "3.3.0-draft.38"))
        assertTrue(Updates.isNewer("3.2.0", "0.0.0-dev"))
    }

    @Test
    fun anUnreadableVersionNeverOffersAnUpdate() {
        assertFalse(Updates.isNewer("latest", "3.2.0"))
        assertFalse(Updates.isNewer("3.2.1", ""))
    }
}
