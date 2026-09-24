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

    @Test
    fun textKindsMatchTheSharedTable() {
        assertEquals(expected("TEXT"), TextKind.entries.associate { it.id to it.name })
    }

    @Test
    fun padButtonMasksMatchTheSharedTable() {
        assertEquals(expected("PAD_BUTTON"), PadButton.entries.associate { it.mask to it.name })
    }

    @Test
    fun padStatusTokensMatchTheSharedTable() {
        // The token is what crosses the wire, so the file holds it and this holds the enum to it. A row's
        // third column is the name both apps use; its second is the bytes, which is the part that matters.
        assertEquals(
            rows("PAD_STATUS").associate { it[2] to it[1] },
            PadStatus.entries.associate { it.name to it.token },
        )
    }

    @Test
    fun anUnknownPadStatusTokenReadsAsNoDriver() {
        assertEquals(PadStatus.NO_DRIVER, PadStatus.of("something-a-later-laptop-says"))
    }

    @Test
    fun theHandshakeMatchesTheSharedTable() {
        val handshake = rows("HANDSHAKE").associate { it[2] to it[1] }
        assertEquals(ProtocolConstants.VERSION.toString(), handshake["VERSION"])
        assertEquals(ProtocolConstants.SERVICE_ID.toString(), handshake["SERVICE_ID"])
    }

    private fun expected(kind: String): Map<Int, String> = rows(kind).associate { it[1].toInt() to it[2] }

    private fun rows(kind: String): List<List<String>> {
        val table =
            fixture()
                .readLines()
                .filter { it.isNotBlank() && !it.startsWith("#") }
                .map { it.split(" ") }
                .filter { it[0] == kind }
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
