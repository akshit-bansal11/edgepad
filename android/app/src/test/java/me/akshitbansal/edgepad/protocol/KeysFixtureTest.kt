package me.akshitbansal.edgepad.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** Holds the keyboard's key codes to protocol/keys.txt, which the Windows, Linux and macOS suites read too. */
class KeysFixtureTest {
    @Test
    fun theTableParsesAndHasNoDuplicateCode() {
        val codes = keyCodes()
        assertTrue(codes.isNotEmpty())
        assertEquals(codes.size, codes.toSet().size)
    }

    @Test
    fun everyCodeFitsAnUnsignedSixteenBitField() {
        assertTrue(keyCodes().all { it in 1..0xFFFF })
    }

    private fun keyCodes(): List<Int> =
        fixture()
            .readLines()
            .filter { it.startsWith("KEY ") }
            .map { it.split(" ")[1].removePrefix("0x").toInt(16) }

    private fun fixture(): File {
        var dir: File? = File("").absoluteFile
        while (dir != null) {
            val candidate = File(dir, "protocol/keys.txt")
            if (candidate.isFile) return candidate
            dir = dir.parentFile
        }
        error("protocol/keys.txt was not found above ${File("").absolutePath}")
    }
}
