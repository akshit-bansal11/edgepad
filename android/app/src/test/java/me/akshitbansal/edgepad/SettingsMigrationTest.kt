package me.akshitbansal.edgepad

import me.akshitbansal.edgepad.surface.Perimeter
import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * The move from four corner keys to eight slot keys is the one change here that can quietly ruin a setup
 * someone already has: the dials must come back exactly where they were left, and a phone that has been
 * through an update is a slow way to find out otherwise. [Settings.migratedSlots] is pure so this can say
 * instead.
 */
class SettingsMigrationTest {
    private fun move(vararg stored: Pair<String, String>): Map<String, String> {
        val old = stored.toMap()
        return Settings.migratedSlots { old[it] }
    }

    @Test
    fun everyOldCornerLandsOnItsEvenSlot() {
        assertEquals(
            mapOf(
                "slot.0" to "VOLUME",
                "slot.2" to "BRIGHTNESS",
                "slot.4" to "ZOOM",
                "slot.6" to "MEDIA",
            ),
            move(
                "corner.0" to "VOLUME",
                "corner.1" to "BRIGHTNESS",
                "corner.2" to "ZOOM",
                "corner.3" to "MEDIA",
            ),
        )
    }

    @Test
    fun aCornerTurnedOffStaysOff() {
        // "-" is the difference between a dial the user took away and a slot they never touched, which
        // falls back to the kind that calls it home. Dropping the sentinel would hand the dial back.
        assertEquals(mapOf("slot.4" to Settings.OFF), move("corner.2" to Settings.OFF))
    }

    @Test
    fun anInstallWithNoCornerKeysMovesNothing() {
        assertEquals(emptyMap<String, String>(), move())
    }

    @Test
    fun aSlotAlreadyWrittenIsNotOverwritten() {
        // Both keys can only be present if a build with slots wrote one; what the user set last wins.
        assertEquals(emptyMap<String, String>(), move("corner.0" to "VOLUME", "slot.0" to "MIC"))
    }

    @Test
    fun theKeysAreTheOnesSettingsReads() {
        for (corner in 0 until Perimeter.CORNERS) {
            assertEquals("corner.$corner", Settings.cornerKey(corner))
            assertEquals("slot.${corner * 2}", Settings.slotKey(Perimeter.cornerSlot(corner)))
        }
    }
}
