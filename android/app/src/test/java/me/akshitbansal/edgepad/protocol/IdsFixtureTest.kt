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
    fun featureTokensMatchTheSharedTable() {
        assertEquals(
            rows("FEATURE").associate { it[2] to it[1] },
            Feature.entries.associate { it.name to it.token },
        )
    }

    @Test
    fun signStatusTokensMatchTheSharedTable() {
        assertEquals(
            rows("SIGN_STATUS").associate { it[2] to it[1] },
            SignStatus.entries.associate { it.name to it.token },
        )
    }

    @Test
    fun captureStatusTokensMatchTheSharedTable() {
        assertEquals(
            rows("CAPTURE_STATUS").associate { it[2] to it[1] },
            CaptureStatus.entries.associate { it.name to it.token },
        )
    }

    @Test
    fun anUnknownCaptureStatusTokenReadsAsFailed() {
        assertEquals(CaptureStatus.FAILED, CaptureStatus.of("something-a-later-laptop-says"))
    }

    @Test
    fun deviceOsTokensMatchTheSharedTable() {
        assertEquals(
            rows("DEVICE_OS").associate { it[2] to it[1] },
            DeviceOs.entries.associate { it.name to it.token },
        )
    }

    @Test
    fun appAddStatusTokensMatchTheSharedTable() {
        assertEquals(
            rows("APP_ADD_STATUS").associate { it[2] to it[1] },
            AppAddStatus.entries.associate { it.name to it.token },
        )
    }

    @Test
    fun anUnknownAppAddStatusTokenReadsAsFailed() {
        assertEquals(AppAddStatus.FAILED, AppAddStatus.of("something-a-later-laptop-says"))
    }

    @Test
    fun blobKindsMatchTheSharedTable() {
        assertEquals(expected("BLOB_KIND"), BlobKind.entries.associate { it.id to it.name })
    }

    @Test
    fun fileTypesMatchTheSharedTable() {
        assertEquals(
            rows("FILE_TYPE").associate { it[1].toInt() to listOf(it[2], it[3], it[4]) },
            FileType.entries.associate { it.id to listOf(it.name, it.mime, it.extension) },
        )
    }

    @Test
    fun fileLimitsMatchTheSharedTable() {
        assertEquals(
            rows("FILE_LIMIT").associate { it[2] to it[1].toLong() },
            mapOf("IMAGE" to FileType.IMAGE_LIMIT, "VIDEO" to FileType.VIDEO_LIMIT),
        )
    }

    @Test
    fun fileStatusesMatchTheSharedTable() {
        assertEquals(expected("FILE_STATUS"), FileStatus.entries.associate { it.id to it.name })
    }

    @Test
    fun anUnknownSignStatusTokenReadsAsFailed() {
        assertEquals(SignStatus.FAILED, SignStatus.of("something-a-later-laptop-says"))
    }

    @Test
    fun penColorsMatchTheSharedPalette() {
        // Index, name and RRGGBB, so a swatch the phone previews cannot differ from the colour the laptop draws.
        assertEquals(
            rows("PALETTE").associate { it[1].toInt() to (it[2] to it[3]) },
            PenColor.entries.associate { it.index to (it.name to "%06X".format(it.rgb)) },
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
