package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

class KeyboardLayoutsTest {
    private val layouts = KeyboardSize.entries.map { layoutFor(it) }

    private fun caps(layout: Layout) = layout.rows.flatten().filterIsInstance<Cap>()

    @Test
    fun everyRowFillsTheLayoutWidth() {
        for (layout in layouts) {
            layout.rows.forEachIndexed { i, row ->
                val units = row.sumOf { unitsOf(it).toDouble() }
                assertEquals("${layout.size} row $i", layout.width.toDouble(), units, 1e-4)
            }
        }
    }

    @Test
    fun printScreenNeverAutoRepeats() {
        for (layout in layouts) {
            for (cap in caps(layout).filter { it.label == "PrtSc" }) assertTrue("${layout.size}", cap.oneShot)
        }
    }

    @Test
    fun aTallKeyHasASpacerOfItsWidthUnderIt() {
        for (layout in layouts) {
            layout.rows.forEachIndexed { i, row ->
                var at = 0f
                for (item in row) {
                    if (item is Cap && item.rows == 2) {
                        val spacer = itemAt(layout.rows[i + 1], at)
                        assertTrue("${layout.size} row $i", spacer is Gap && spacer.units == item.units)
                    }
                    at += unitsOf(item)
                }
            }
        }
    }

    private fun itemAt(
        row: List<Item>,
        start: Float,
    ): Item? {
        var at = 0f
        for (item in row) {
            if (Math.abs(at - start) < 1e-4f) return item
            at += unitsOf(item)
        }
        return null
    }

    private fun unitsOf(item: Item): Float =
        when (item) {
            is Cap -> item.units
            is Gap -> item.units
        }

    @Test
    fun fiveRowsAtSixtyFiveAndSixtyAndSixElsewhere() {
        for (layout in layouts) {
            val expected = if (layout.size == KeyboardSize.SIXTY_FIVE || layout.size == KeyboardSize.SIXTY) 5 else 6
            assertEquals("${layout.size}", expected, layout.rows.size)
        }
    }

    @Test
    fun f1ToF12AreReachableOnEveryLayout() {
        for (layout in layouts) {
            val reachable =
                caps(layout)
                    .flatMap { cap ->
                        listOf(false, true).flatMap { fn -> FnMode.entries.map { resolve(cap, fn, it) } }
                    }.filterIsInstance<KeyOut.Code>()
                    .map { it.code }
                    .toSet()
            for (f in 0x70..0x7B) assertTrue("${layout.size} F${f - 0x6F}", f in reachable)
        }
    }

    @Test
    fun everyLetterOnceAndDigitsInOrder() {
        for (layout in layouts) {
            val bases = caps(layout).mapNotNull { (it.base as? KeyOut.Code)?.code }
            for (letter in 0x41..0x5A) assertEquals("${layout.size} $letter", 1, bases.count { it == letter })
            assertEquals((0x31..0x39).toList() + 0x30, bases.filter { it in 0x30..0x39 })
        }
    }

    @Test
    fun everyCodeUsedIsInKeysTxtAndFitsU16() {
        val known = keysTxt()
        for (layout in layouts) {
            for (cap in caps(layout)) {
                for (out in listOfNotNull(cap.base, cap.fn)) {
                    if (out !is KeyOut.Code) continue
                    assertTrue("${layout.size} ${cap.label} 0x${out.code.toString(16)}", out.code in known)
                    assertTrue(out.code <= 0xFFFF)
                }
            }
        }
    }

    private fun keysTxt(): Set<Int> {
        var dir: File? = File("").absoluteFile
        while (dir != null) {
            val candidate = File(dir, "protocol/keys.txt")
            if (candidate.isFile) {
                return candidate
                    .readLines()
                    .filter { it.startsWith("KEY ") }
                    .map { Integer.decode(it.split(" ")[1]) }
                    .toSet()
            }
            dir = dir.parentFile
        }
        error("protocol/keys.txt was not found above ${File("").absolutePath}")
    }
}
