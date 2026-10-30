package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class StoredDialTest {
    private var asked = 0

    private fun resolve(name: String?) =
        resolveStoredDial(name) {
            asked++
            DialKind.ZOOM
        }

    @Test
    fun aNameNeverStoredTakesTheSuppliedDefault() {
        assertEquals(DialKind.ZOOM, resolve(null))
        assertEquals(1, asked)
    }

    @Test
    fun theDashIsAnEmptyCorner() {
        assertNull(resolve("-"))
        assertEquals(0, asked)
    }

    @Test
    fun aKnownNameIsItsKind() {
        assertEquals(DialKind.VOLUME, resolve("VOLUME"))
        assertEquals(0, asked)
    }

    @Test
    fun aRemovedOrUnknownNameIsEmptyAndNeverTheDefault() {
        assertNull(resolve("MIC"))
        assertNull(resolve("TRACKS"))
        assertNull(resolve("garbage"))
        assertEquals(0, asked)
    }
}
