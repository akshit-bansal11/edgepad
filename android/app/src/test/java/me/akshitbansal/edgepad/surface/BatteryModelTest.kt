package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.link.BatteryEntry
import me.akshitbansal.edgepad.protocol.BatteryKind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class BatteryModelTest {
    private fun e(
        kind: BatteryKind,
        level: Int = 50,
        charging: Boolean = false,
    ) = BatteryEntry(kind, level, charging, kind.token)

    private fun view(
        id: String,
        vararg entries: BatteryEntry,
    ) = LaptopView(id, id, entries.toList())

    private val phone = BatteryRow("Phone", null, true, 80, false, "")

    private fun chips(
        sel: LaptopView?,
        others: List<LaptopView> = emptyList(),
    ) = BatteryModel.chips(BatteryModel.build(sel, others, phone))

    @Test
    fun noLaptopBatteryGivesTwoChips() {
        val c = chips(view("a", e(BatteryKind.MOUSE)))
        assertNull(c.laptop)
        assertEquals(1, c.plus)
        assertEquals(phone, c.phone)
    }

    @Test
    fun noOthersMeansNoPlus() {
        val c = chips(view("a", e(BatteryKind.LAPTOP)))
        assertEquals(0, c.plus)
        assertEquals(BatteryKind.LAPTOP, c.laptop?.kind)
    }

    @Test
    fun otherLaptopsCountLaptopsAndPeripherals() {
        val others =
            listOf(
                view(
                    "b",
                    e(BatteryKind.LAPTOP),
                    e(BatteryKind.MOUSE),
                ),
                view(
                    "c",
                    e(BatteryKind.LAPTOP),
                    e(BatteryKind.KEYBOARD),
                ),
            )
        assertEquals(4, chips(view("a", e(BatteryKind.LAPTOP)), others).plus)
    }

    @Test
    fun plusLowOnlyForHiddenLowNotCharging() {
        val sel = view("a", e(BatteryKind.LAPTOP, 5))
        assertFalse(chips(sel).plusLow)
        assertTrue(chips(view("a", e(BatteryKind.LAPTOP), e(BatteryKind.MOUSE, 5))).plusLow)
        assertFalse(chips(view("a", e(BatteryKind.LAPTOP), e(BatteryKind.MOUSE, 5, true))).plusLow)
    }

    @Test
    fun lowBoundary() {
        assertTrue(BatteryModel.low(19, false))
        assertFalse(BatteryModel.low(20, false))
        assertFalse(BatteryModel.low(0, true))
    }

    @Test
    fun sameLaptopOverTwoTransportsCountsOnce() {
        val a = view("a", e(BatteryKind.LAPTOP))
        val b = view("b", e(BatteryKind.LAPTOP))
        assertEquals(1, chips(a, listOf(a, b, b)).plus)
    }
}
