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

    @Test
    fun cancelReleasesAnArmedModifierAKeyHasAlreadyPressed() {
        latch.modifierDown(CTRL)
        latch.modifierUp(CTRL)
        latch.keyDown(C)
        latch.cancel()
        assertEquals(listOf("17v", "17^", "17v", "67v", "17^"), sent)

        sent.clear()
        latch.keyDown(A)
        assertEquals(listOf("65v"), sent)
    }

    @Test
    fun aSecondKeyBeforeTheFirstIsReleasedDoesNotPressTheModifierAgain() {
        latch.modifierDown(CTRL)
        latch.modifierUp(CTRL)
        sent.clear()
        latch.keyDown(A)
        latch.keyDown(C)
        assertEquals(listOf("17v", "65v", "67v"), sent)
    }

    @Test
    fun aTappedKeyIsActiveForExactlyTheNextKeyAndAHeldOneWhileHeld() {
        latch.modifierDown(FN)
        latch.modifierUp(FN)
        assertTrue(latch.isActive(FN))
        latch.keyDown(A)
        latch.keyUp(A)
        assertFalse(latch.isActive(FN))

        latch.modifierDown(FN)
        assertTrue(latch.isActive(FN))
        latch.keyDown(A)
        latch.keyUp(A)
        assertTrue(latch.isActive(FN))
        latch.modifierUp(FN)
        assertFalse(latch.isActive(FN))
    }

    @Test
    fun twoFingersOnOneModifierKeepItDownUntilTheLastLeaves() {
        latch.modifierDown(FN)
        latch.modifierDown(FN)
        latch.modifierUp(FN)
        assertTrue(latch.isActive(FN))
        latch.modifierUp(FN)
        assertEquals(listOf("131071v", "131071^"), sent)
    }

    @Test
    fun twoFingersOnOneKeyPressAndReleaseItOnce() {
        latch.keyDown(A)
        latch.keyDown(A)
        latch.keyUp(A)
        assertEquals(listOf("65v"), sent)
        latch.keyUp(A)
        assertEquals(listOf("65v", "65^"), sent)
    }

    @Test
    fun aModifierUsedWhileHeldIsNotArmedByAnotherModifiersTap() {
        latch.modifierDown(SHIFT)
        latch.keyDown(A)
        latch.keyUp(A)
        latch.modifierDown(FN)
        latch.modifierUp(FN)
        latch.modifierUp(SHIFT)
        assertFalse(latch.isArmed(SHIFT))
        assertTrue(latch.isArmed(FN))
    }

    private companion object {
        const val FN = 0x1FFFF
        const val A = 0x41
        const val SHIFT = 0x10
        const val CTRL = 0x11
        const val C = 0x43
    }
}
