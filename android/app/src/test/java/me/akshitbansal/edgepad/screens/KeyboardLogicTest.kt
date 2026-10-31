package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.DeviceOs
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class KeyboardLogicTest {
    private val keys = ArrayList<String>()
    private val actions = ArrayList<ActionId>()

    private fun wire(
        code: Int,
        down: Boolean,
    ) = sendWire(code, down, { c, d -> keys.add("$c$d") }, { actions.add(it) })

    private fun cap(
        layout: KeyboardSize,
        label: String,
    ): Cap =
        layoutFor(layout)
            .rows
            .flatten()
            .filterIsInstance<Cap>()
            .first { it.label == label }

    @Test
    fun anFRowKeyWithFnPrintsItsActionAndSendsFOnTheOtherSide() {
        val bri = cap(KeyboardSize.SEVENTY_FIVE, "Bri-")
        val printed = KeyOut.Act(ActionId.BRIGHTNESS_DOWN)
        val f1 = KeyOut.Code(0x70)
        assertEquals(printed, resolve(bri, false, FnMode.WITH_FN))
        assertEquals(f1, resolve(bri, true, FnMode.WITH_FN))
        assertEquals(f1, resolve(bri, false, FnMode.DIRECT))
        assertEquals(printed, resolve(bri, true, FnMode.DIRECT))
    }

    @Test
    fun aNumberKeyOnASmallBoardTypesTheDigitAndFnGivesF() {
        val two = cap(KeyboardSize.SIXTY_FIVE, "2")
        assertEquals(KeyOut.Code(0x32), resolve(two, false, FnMode.WITH_FN))
        assertEquals(KeyOut.Code(0x71), resolve(two, true, FnMode.WITH_FN))
        assertEquals(KeyOut.Code(0x71), resolve(two, false, FnMode.DIRECT))
        assertEquals(KeyOut.Code(0x32), resolve(two, true, FnMode.DIRECT))
    }

    @Test
    fun aPlainKeyWithAnFnUsesItOnlyWhileFnIsActiveInEitherMode() {
        val k = cap(KeyboardSize.SIXTY, "K")
        for (mode in FnMode.entries) {
            assertEquals(KeyOut.Code(0x4B), resolve(k, false, mode))
            assertEquals(KeyOut.Code(0x26), resolve(k, true, mode))
        }
    }

    @Test
    fun aPlainKeyWithoutOneIgnoresFn() {
        val l = cap(KeyboardSize.SIXTY, "L")
        for (mode in FnMode.entries) {
            assertEquals(KeyOut.Code(0x4C), resolve(l, true, mode))
        }
    }

    @Test
    fun fnNeverReachesAKeyFrameAndAnActionGoesOutOnceOnDownOnly() {
        wire(FN_CODE, true)
        wire(FN_CODE, false)
        assertTrue(keys.isEmpty() && actions.isEmpty())

        val code = ACTION_BASE + ActionId.MUTE_TOGGLE.id
        wire(code, true)
        wire(code, false)
        assertEquals(listOf(ActionId.MUTE_TOGGLE), actions)
        assertTrue(keys.isEmpty())

        wire(0x41, true)
        wire(0x41, false)
        assertEquals(listOf("65true", "65false"), keys)
    }

    @Test
    fun wireValuesSitAboveU16() {
        val fn = Cap("Fn", 1f, KeyOut.None, role = KeyRole.FN)
        assertEquals(FN_CODE, wireOf(fn, fn.base))
        val mute = Cap("Mute", 1f, KeyOut.Act(ActionId.MUTE_TOGGLE))
        assertEquals(ACTION_BASE + ActionId.MUTE_TOGGLE.id, wireOf(mute, mute.base))
        assertEquals(null, wireOf(mute, KeyOut.None))
        assertEquals(0x41, wireOf(mute, KeyOut.Code(0x41)))
        assertTrue(ACTION_BASE > 0xFFFF && FN_CODE > 0xFFFF)
    }

    @Test
    fun onlyVolumeAndBrightnessActionsRepeatAndCapsAndWinNever() {
        val caps = cap(KeyboardSize.FULL, "Caps")
        val win = cap(KeyboardSize.FULL, "Win")
        val a = cap(KeyboardSize.FULL, "A")
        assertFalse(repeats(caps, caps.base))
        assertFalse(repeats(win, win.base))
        assertTrue(repeats(a, a.base))
        assertTrue(repeats(a, KeyOut.Act(ActionId.VOLUME_UP)))
        assertTrue(repeats(a, KeyOut.Act(ActionId.BRIGHTNESS_DOWN)))
        assertFalse(repeats(a, KeyOut.Act(ActionId.PLAY_PAUSE)))
        assertFalse(repeats(a, KeyOut.None))
    }

    @Test
    fun altAndWinAreNamedForTheConnectedLaptop() {
        val alt = cap(KeyboardSize.FULL, "Alt")
        val win = cap(KeyboardSize.FULL, "Win")
        val ctrl = cap(KeyboardSize.FULL, "Ctrl")
        assertEquals(
            listOf("Alt", "Alt", "Option", "Alt"),
            listOf(null, DeviceOs.WINDOWS, DeviceOs.MACOS, DeviceOs.LINUX).map { labelFor(alt, it) },
        )
        assertEquals(
            listOf("Meta", "Win", "Super", "Cmd"),
            listOf(null, DeviceOs.WINDOWS, DeviceOs.LINUX, DeviceOs.MACOS).map { labelFor(win, it) },
        )
        assertEquals("Ctrl", labelFor(ctrl, DeviceOs.MACOS))
    }
}
