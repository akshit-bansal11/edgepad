package me.akshitbansal.edgepad.link

import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** The provider serves only the names [ClipFile] gives out: every other path, however spelled, is refused. */
class ClipProviderTest {
    private val name = ClipFile.newName()

    @Test
    fun anImagesOwnNameIsServed() {
        assertTrue(isClipPath(listOf(name)))
    }

    // One fixed name let an app that kept the URI read whichever image came next.
    @Test
    fun everyImageGetsItsOwnName() {
        assertNotEquals(ClipFile.newName(), ClipFile.newName())
    }

    @Test
    fun noPathIsRefused() {
        assertFalse(isClipPath(emptyList()))
    }

    @Test
    fun parentSegmentsAreRefused() {
        assertFalse(isClipPath(listOf("..")))
        assertFalse(isClipPath(listOf("..", name)))
        assertFalse(isClipPath(listOf(name, "..")))
        assertFalse(isClipPath(listOf("clip", "..", name)))
    }

    @Test
    fun anotherNameIsRefused() {
        assertFalse(isClipPath(listOf("clip.tmp")))
        assertFalse(isClipPath(listOf(ClipFile.DISPLAY_NAME)))
        assertFalse(isClipPath(listOf("clip-.png")))
        assertFalse(isClipPath(listOf(name.replaceFirst("clip", "Clip"))))
        assertFalse(isClipPath(listOf("$name ")))
        assertFalse(isClipPath(listOf("$name\n")))
        assertFalse(isClipPath(listOf("shared_prefs")))
        assertFalse(isClipPath(listOf("")))
    }

    @Test
    fun aFolderAroundItIsRefused() {
        assertFalse(isClipPath(listOf("clip", name)))
        assertFalse(isClipPath(listOf(name, name)))
        assertFalse(isClipPath(listOf("../$name")))
    }
}
