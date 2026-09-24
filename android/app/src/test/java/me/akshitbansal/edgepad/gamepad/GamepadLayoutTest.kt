package me.akshitbansal.edgepad.gamepad

import me.akshitbansal.edgepad.protocol.PadButton
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
    fun everyBindingFitsTheControlItIsOn() {
        for (preset in GamepadLayout.presets) {
            for (control in preset.controls) {
                assertTrue("${preset.name}.${control.id}", control.binding.fits(control.kind))
            }
        }
    }

    @Test
    fun presetsExistAndAreNonEmpty() {
        assertTrue(GamepadLayout.presets.isNotEmpty())
        for (preset in GamepadLayout.presets) assertNotNull(GamepadLayout.decode(preset.encode()))
    }

    @Test
    fun everyBindingRoundTripsThroughItsStoredForm() {
        val bindings =
            listOf(
                Binding.Keys(listOf(0x41)),
                Binding.Keys(listOf(0x57, 0x53, 0x41, 0x44)),
                Binding.Button(PadButton.A),
                Binding.Dpad,
                Binding.Stick(Binding.Side.RIGHT),
                Binding.Trigger(Binding.Side.LEFT),
            )
        for (binding in bindings) assertEquals(binding, Binding.decode(binding.encode()))
    }

    @Test
    fun anUnreadableBindingIsNull() {
        assertNull(Binding.decode(""))
        assertNull(Binding.decode("K:"))
        assertNull(Binding.decode("K:32,up"))
        assertNull(Binding.decode("B:TRIANGLE"))
        assertNull(Binding.decode("S:MIDDLE"))
        assertNull(Binding.decode("Q:1"))
    }

    @Test
    fun aLayoutSavedByTwoPointXIsRefusedRatherThanHalfRead() {
        // 2.x wrote the key codes bare in the last field, with no tag in front of them. Nothing here reads
        // that as keys: the whole layout is refused and GamepadStore falls back to the first preset.
        assertNull(GamepadLayout.decode("Platformer\njump|BUTTON|Jump|0.85|0.6|72.0|32"))
        assertNull(GamepadLayout.decode("Platformer\ndpad|DPAD|D|0.18|0.6|150.0|38,40,37,39"))
    }

    @Test
    fun aBindingTheControlCannotDriveIsRefused() {
        // A stick cannot hold one button down, and a d-pad needs four keys rather than one.
        assertNull(GamepadLayout.decode("Odd\nls|STICK|L|0.13|0.46|150.0|B:A"))
        assertNull(GamepadLayout.decode("Odd\ndpad|DPAD|D|0.3|0.78|150.0|K:37"))
    }

    @Test
    fun aStoredControllerLayoutReadsBackExactlyAsItWasWritten() {
        val text = "Mine\nlt|SHOULDER|LT|0.1|0.12|110.0|T:LEFT\nls|STICK|L|0.13|0.46|150.0|S:LEFT"
        val decoded = GamepadLayout.decode(text)
        assertNotNull(decoded)
        assertEquals(text, decoded?.encode())
    }

    @Test
    fun theFirstPresetIsTheControllerAndTheRestAreKeyboards() {
        // The first preset is what a fresh install and a refused stored layout both land on, so which one
        // it is decides what the pad does out of the box.
        val xbox = GamepadLayout.presets.first()
        assertEquals("Xbox", xbox.name)
        assertTrue(xbox.controls.none { it.binding is Binding.Keys })
        for (preset in GamepadLayout.presets.drop(1)) {
            for (control in preset.controls) {
                assertTrue("${preset.name}.${control.id}", control.binding is Binding.Keys)
            }
        }
    }

    @Test
    fun theControllerPresetOffersEveryStandardButtonExactlyOnce() {
        val xbox = GamepadLayout.presets.first()
        val bound = xbox.controls.mapNotNull { (it.binding as? Binding.Button)?.button }
        // NONE is the empty mask and GUIDE is the Xbox button itself, which no game reads; the four d-pad
        // bits belong to the d-pad control rather than to buttons of their own.
        val dpad =
            setOf(PadButton.DPAD_UP, PadButton.DPAD_DOWN, PadButton.DPAD_LEFT, PadButton.DPAD_RIGHT)
        val expected =
            PadButton.entries.filter { it != PadButton.NONE && it != PadButton.GUIDE && it !in dpad }
        assertEquals(expected.toSet(), bound.toSet())
        assertEquals(expected.size, bound.size)
        assertTrue(xbox.controls.any { it.binding == Binding.Dpad })
        assertTrue(xbox.controls.any { it.binding == Binding.Stick(Binding.Side.LEFT) })
        assertTrue(xbox.controls.any { it.binding == Binding.Stick(Binding.Side.RIGHT) })
        assertTrue(xbox.controls.any { it.binding == Binding.Trigger(Binding.Side.LEFT) })
        assertTrue(xbox.controls.any { it.binding == Binding.Trigger(Binding.Side.RIGHT) })
    }
}
