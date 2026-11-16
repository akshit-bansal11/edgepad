package me.akshitbansal.edgepad.link.xfer

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.io.File

/** Runs protocol/filenames.txt, the pairs the laptop also runs, and the rules the file only describes. */
class SafeNameTest {
    @Test
    fun everyPairInTheSharedTableHolds() {
        var checked = 0
        for (line in fixture().readLines(Charsets.UTF_8)) {
            if (line.isBlank() || line.startsWith("#")) continue
            val at = line.lastIndexOf(" | ")
            val input = line.substring(0, at)
            val expected = line.substring(at + 3)
            val want = if (expected == "(rejected)") null else expected
            assertEquals("for \"$input\"", want, SafeName.clean(input))
            checked++
        }
        assertEquals(true, checked > 30)
    }

    @Test
    fun controlCharactersAreDropped() {
        assertEquals("ab.txt", SafeName.clean("a\u0000b\n.txt"))
        assertNull(SafeName.clean("\u0001\u007f"))
    }

    @Test
    fun bidiAndFormatCharactersAreDropped() {
        assertEquals("invoiceexe.pdf", SafeName.clean("invoice‮exe.pdf"))
        assertEquals("ab.txt", SafeName.clean("a​b⁦.﻿txt؜"))
        assertNull(SafeName.clean("‮‏"))
    }

    @Test
    fun aClashIsRenamedNeverOverwritten() {
        val taken = setOf("a.txt", "a (1).txt")
        assertEquals("a (2).txt", SafeName.unique("a.txt") { it in taken })
        assertEquals("b.txt", SafeName.unique("b.txt") { it in taken })
        assertEquals("noext (1)", SafeName.unique("noext") { it == "noext" })
        assertEquals(".hidden (1)", SafeName.unique(".hidden") { it == ".hidden" })
    }

    @Test
    fun aLongNameIsCutToBytesKeepingItsExtension() {
        val cut = SafeName.clean("é".repeat(100) + ".jpeg")!!
        assertEquals(true, cut.toByteArray(Charsets.UTF_8).size <= XferLimits.MAX_NAME_BYTES)
        assertEquals(true, cut.endsWith(".jpeg"))
    }

    private fun fixture(): File {
        var dir: File? = File("").absoluteFile
        while (dir != null) {
            val candidate = File(dir, "protocol/filenames.txt")
            if (candidate.isFile) return candidate
            dir = dir.parentFile
        }
        error("protocol/filenames.txt was not found above ${File("").absolutePath}")
    }
}
