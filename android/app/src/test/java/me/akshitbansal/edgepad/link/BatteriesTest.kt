package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.BatteryKind
import me.akshitbansal.edgepad.protocol.OutputKind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class BatteriesTest {
    @Test
    fun parsesTheGoldenSnapshot() {
        assertEquals(
            listOf(
                BatteryEntry(BatteryKind.LAPTOP, 87, true, "Desk"),
                BatteryEntry(BatteryKind.HEADPHONES, 60, false, "AirPods Pro"),
            ),
            Batteries.parse("laptop:87:1:Desk|headphones:60:0:AirPods Pro"),
        )
    }

    @Test
    fun emptyIsNoDevicesAndANameMayHoldColons() {
        assertEquals(emptyList<BatteryEntry>(), Batteries.parse(""))
        assertEquals("a:b", Batteries.parse("mouse:5:0:a:b").single().name)
    }

    @Test
    fun skipsMalformedEntriesClampsAndReadsUnknownKindAsOther() {
        val parsed = Batteries.parse("junk|mouse:x:0:M|mouse:150:0:Big|kb:-5:0:K|toaster:50:0:T")
        assertEquals(listOf(100, 0, 50), parsed.map { it.level })
        assertEquals(BatteryKind.OTHER, parsed.last().kind)
    }

    @Test
    fun parsesAMultiByteName() {
        assertEquals("Café", Batteries.parse("keyboard:40:0:Café").single().name)
    }

    @Test
    fun parsesOutput() {
        assertEquals(Output(OutputKind.HEADPHONES, "AirPods Pro"), Output.parse("headphones/AirPods Pro"))
        assertEquals(Output(OutputKind.SPEAKER, "a/b"), Output.parse("speaker/a/b"))
        assertEquals(OutputKind.OTHER, Output.parse("tv/Set")?.kind)
        assertNull(Output.parse(""))
        assertNull(Output.parse("speaker"))
        assertNull(Output.parse("speaker/"))
    }

    @Test
    fun shortensNamesToWholeWords() {
        assertEquals("AirPods", OutputName.short("AirPods Pro"))
        assertEquals("Speakers", OutputName.short("Speakers"))
        assertEquals("Sony", OutputName.short("Sony WH-1000XM5"))
        assertEquals("ABCDEFGHI…", OutputName.short("ABCDEFGHIJKLMNOP"))
        assertEquals("", OutputName.short(""))
    }
}
