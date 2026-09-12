package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ModifierLatchTest {
    private val sent = ArrayList<String>()
    private val latch = ModifierLatch { code, down -> sent.add("$code${if (down) "v" else "^"}") }

    @Test
    fun aTapAloneIsAPlainPressThatArmsTheNextKey() {
        latch.modifierDown(CTRL)
        latch.modifierUp(CTRL)
        assertEquals(listOf("17v", "17^"), sent)
        assertTrue(latch.isArmed(CTRL))

        latch.keyDown(C)
        latch.keyUp(C)
        assertEquals(listOf("17v", "17^", "17v", "67v", "67^", "17^"), sent)
        assertFalse(latch.isArmed(CTRL))
    }

    @Test
    fun heldUnderAFingerItWrapsEveryKeyAndArmsNothing() {
        latch.modifierDown(SHIFT)
        latch.keyDown(C)
        latch.keyUp(C)
        latch.modifierUp(SHIFT)
        assertEquals(listOf("16v", "67v", "67^", "16^"), sent)
        assertFalse(latch.isArmed(SHIFT))
    }

    @Test
    fun aSecondTapDisarms() {
        latch.modifierDown(CTRL)
        latch.modifierUp(CTRL)
        latch.modifierDown(CTRL)
        latch.modifierUp(CTRL)
        assertFalse(latch.isArmed(CTRL))
        assertEquals(listOf("17v", "17^"), sent)
    }

    @Test
    fun cancelReleasesWhatIsHeld() {
        latch.modifierDown(SHIFT)
        latch.cancel()
        assertEquals(listOf("16v", "16^"), sent)
    }

    private companion object {
        const val SHIFT = 0x10
        const val CTRL = 0x11
        const val C = 0x43
    }
}
