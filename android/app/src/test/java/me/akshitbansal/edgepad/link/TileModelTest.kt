package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** What the Quick Settings tile shows. */
class TileModelTest {
    @Test
    fun noStatusIsInactive() {
        val model = TileModel.of(null)
        assertFalse(model.active)
        assertNull(model.name)
    }

    @Test
    fun aNameLightsTheTile() {
        val model = TileModel.of(TileStatus("Desk"))
        assertTrue(model.active)
        assertEquals("Desk", model.name)
    }

    @Test
    fun anEmptyNameIsActiveWithoutASubtitle() {
        val model = TileModel.of(TileStatus(""))
        assertTrue(model.active)
        assertNull(model.name)
    }
}
