package me.akshitbansal.edgepad.gamepad

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class GamepadLayoutTest {
    @Test
    fun everyPresetRoundTripsThroughEncodeAndDecode() {
        for (preset in GamepadLayout.presets) {
            val decoded = GamepadLayout.decode(preset.encode())
            assertEquals(preset, decoded)
        }
    }

    @Test
    fun garbageDecodesToNull() {
        assertNull(GamepadLayout.decode("garbage"))
    }

    @Test
    fun emptyTextDecodesToNull() {
        assertNull(GamepadLayout.decode(""))
    }

    @Test
    fun idsAreUniquePerPreset() {
        for (preset in GamepadLayout.presets) {
            val ids = preset.controls.map { it.id }
            assertEquals(ids.size, ids.toSet().size)
        }
    }

    @Test
    fun dpadAndStickHaveFourKeysButtonAndShoulderHaveOne() {
        for (preset in GamepadLayout.presets) {
            for (control in preset.controls) {
                val expected = if (control.kind == ControlKind.DPAD || control.kind == ControlKind.STICK) 4 else 1
                assertEquals("${preset.name}.${control.id}", expected, control.keys.size)
            }
        }
    }

    @Test
    fun presetsExistAndAreNonEmpty() {
        assertTrue(GamepadLayout.presets.isNotEmpty())
        for (preset in GamepadLayout.presets) assertNotNull(GamepadLayout.decode(preset.encode()))
    }
}
