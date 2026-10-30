package me.akshitbansal.edgepad.link

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** The provider serves one file and nothing else: every other path, however spelled, is refused. */
class ClipProviderTest {
    @Test
    fun theOneClipIsServed() {
        assertTrue(isClipPath(listOf("clip.png")))
    }

    @Test
    fun noPathIsRefused() {
        assertFalse(isClipPath(emptyList()))
    }

    @Test
    fun parentSegmentsAreRefused() {
        assertFalse(isClipPath(listOf("..")))
        assertFalse(isClipPath(listOf("..", "clip.png")))
        assertFalse(isClipPath(listOf("clip.png", "..")))
        assertFalse(isClipPath(listOf("clip", "..", "clip.png")))
    }

    @Test
    fun anotherNameIsRefused() {
        assertFalse(isClipPath(listOf("clip.tmp")))
        assertFalse(isClipPath(listOf("Clip.png")))
        assertFalse(isClipPath(listOf("clip.png ")))
        assertFalse(isClipPath(listOf("shared_prefs")))
        assertFalse(isClipPath(listOf("")))
    }

    @Test
    fun aFolderAroundItIsRefused() {
        assertFalse(isClipPath(listOf("clip", "clip.png")))
        assertFalse(isClipPath(listOf("clip.png", "clip.png")))
        assertFalse(isClipPath(listOf("../clip.png")))
    }
}
