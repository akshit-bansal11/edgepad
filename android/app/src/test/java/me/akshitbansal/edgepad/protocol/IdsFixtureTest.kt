package me.akshitbansal.edgepad.protocol

import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.File

/** Holds [ActionId] and [ControlId] to protocol/actions.txt, which the Windows suite reads too. */
class IdsFixtureTest {
    @Test
    fun actionIdsMatchTheSharedTable() {
        assertEquals(expected("ACTION"), ActionId.entries.associate { it.id to it.name })
    }

    @Test
    fun controlIdsMatchTheSharedTable() {
        assertEquals(expected("CONTROL"), ControlId.entries.associate { it.id to it.name })
    }

    private fun expected(kind: String): Map<Int, String> {
        val table =
            fixture()
                .readLines()
                .filter { it.isNotBlank() && !it.startsWith("#") }
                .map { it.split(" ") }
                .filter { it[0] == kind }
                .associate { it[1].toInt() to it[2] }
        check(table.isNotEmpty()) { "no $kind rows in actions.txt" }
        return table
    }

    private fun fixture(): File {
        var dir: File? = File("").absoluteFile
        while (dir != null) {
            val candidate = File(dir, "protocol/actions.txt")
            if (candidate.isFile) return candidate
            dir = dir.parentFile
        }
        error("protocol/actions.txt was not found above ${File("").absolutePath}")
    }
}
