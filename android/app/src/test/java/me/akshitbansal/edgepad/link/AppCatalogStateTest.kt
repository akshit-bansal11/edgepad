package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.AppAddStatus
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.TextKind
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * What the phone keeps of a laptop's app list. The same caps as the shared core's AppPicker (400 apps, 40 characters, 8
 * KB an icon), applied to what a laptop sends, which is untrusted: each break drops the row or the picture and nothing
 * else.
 */
class AppCatalogStateTest {
    private val catalog = AppCatalogState()

    private fun blob(vararg rows: String): ByteArray = rows.joinToString("\n", postfix = "\n").toByteArray()

    private fun names() = catalog.apps.map { it.name }

    private fun tokens() = catalog.apps.map { it.token }

    private fun app(name: String) = AppCatalogState.App("1.0", name)

    @Test
    fun aListIsOneTokenAndNameARow() {
        assertTrue(catalog.setList(blob("7.0\tAlpha", "7.1\tBeta Two", "7.2\tGamma")))

        assertEquals(listOf("Alpha", "Beta Two", "Gamma"), names())
        assertEquals(listOf("7.0", "7.1", "7.2"), tokens())
        assertEquals(7, catalog.epoch)
        assertTrue(catalog.loaded)
    }

    @Test
    fun nothingIsLoadedUntilAListArrives() {
        assertFalse(catalog.loaded)
        assertNull(catalog.epoch)
        assertTrue(catalog.apps.isEmpty())
        assertNull(catalog.lastStatus)
    }

    @Test
    fun anEmptyListIsAnAnswerAndNotNothing() {
        assertTrue(catalog.setList(ByteArray(0)))

        assertTrue(catalog.loaded)
        assertTrue(catalog.apps.isEmpty())
        assertNull(catalog.epoch)
    }

    @Test
    fun aLineWithoutATabIsSkipped() {
        catalog.setList(blob("1.0\tAlpha", "1.1 no tab here", "", "1.2\tGamma"))

        assertEquals(listOf("Alpha", "Gamma"), names())
    }

    @Test
    fun aTabInsideANameIsDropped() {
        catalog.setList(blob("1.0\tAlpha\tBeta"))

        assertEquals(listOf("AlphaBeta"), names())
    }

    @Test
    fun aDuplicateTokenKeepsItsPlaceAndTakesTheLastName() {
        catalog.setList(blob("1.0\tAlpha", "1.1\tBeta", "1.0\tAlpha Again", "1.2\tGamma"))

        assertEquals(listOf("1.0", "1.1", "1.2"), tokens())
        assertEquals(listOf("Alpha Again", "Beta", "Gamma"), names())
    }

    @Test
    fun aListPastFourHundredAppsIsCapped() {
        val rows = (0 until AppCatalogState.MAX_APPS + 50).map { "3.$it\tApp $it" }

        catalog.setList(blob(*rows.toTypedArray()))

        assertEquals(AppCatalogState.MAX_APPS, catalog.apps.size)
        assertEquals("3.0", catalog.apps.first().token)
        assertEquals("3.399", catalog.apps.last().token)
    }

    @Test
    fun aDuplicateOfAKeptRowStillUpdatesItAtTheCap() {
        val rows = (0 until AppCatalogState.MAX_APPS).map { "3.$it\tApp $it" } + "3.5\tRenamed" + "3.400\tOver"

        catalog.setList(blob(*rows.toTypedArray()))

        assertEquals(AppCatalogState.MAX_APPS, catalog.apps.size)
        assertEquals("Renamed", catalog.apps[5].name)
        assertTrue(catalog.apps.none { it.token == "3.400" })
    }

    @Test
    fun aRowWhoseTokenCouldNeverBeAnsweredIsSkipped() {
        val rows = arrayOf("x.1\tLetters", "1\tNoDot", "1.2.3\tTwoDots", "-1.0\tSigned", ".1\tNoEpoch", "1.\tNoIndex")

        catalog.setList(blob(*rows))

        assertTrue(catalog.apps.isEmpty())
    }

    @Test
    fun aRowOfAnotherEpochThanTheFirstIsSkipped() {
        catalog.setList(blob("4.0\tAlpha", "3.1\tOld", "4.2\tGamma"))

        assertEquals(listOf("Alpha", "Gamma"), names())
        assertEquals(4, catalog.epoch)
    }

    @Test
    fun aBlankNameIsSkippedAndControlCharactersAreDropped() {
        catalog.setList(blob("1.0\t   ", "1.1\t", "1.2\tTab\u0007bed\r"))

        assertEquals(listOf("Tabbed"), names())
    }

    @Test
    fun aNameIsCutToFortyCharactersOnAWholeCharacter() {
        val long = "n".repeat(60)
        val pair = String(Character.toChars(EMOJI))
        val split = "n".repeat(AppCatalogState.MAX_NAME_CHARS - 1) + pair

        catalog.setList(blob("1.0\t$long", "1.1\t$split"))

        assertEquals("n".repeat(AppCatalogState.MAX_NAME_CHARS), names()[0])
        assertEquals("n".repeat(AppCatalogState.MAX_NAME_CHARS - 1), names()[1])
    }

    @Test
    fun aNewListReplacesTheOldOneAndItsIcons() {
        catalog.setList(blob("1.0\tAlpha"))
        catalog.setIcon("1.0", png(10))

        catalog.setList(blob("2.0\tAlpha"))

        assertEquals(2, catalog.epoch)
        assertNull(catalog.icon("1.0"))
        assertNull(catalog.icon("2.0"))
    }

    @Test
    fun aListOverTheByteCapIsKeptOutWhole() {
        catalog.setList(blob("1.0\tAlpha"))

        assertFalse(catalog.setList(ByteArray(AppCatalogState.MAX_LIST_BYTES + 1) { 'a'.code.toByte() }))

        assertEquals(listOf("Alpha"), names())
    }

    @Test
    fun aFullSizeListFitsTheByteCap() {
        val name = "é".repeat(AppCatalogState.MAX_NAME_CHARS)
        val rows = (0 until AppCatalogState.MAX_APPS).map { "123456.$it\t$name" }

        assertTrue(catalog.setList(blob(*rows.toTypedArray())))

        assertEquals(AppCatalogState.MAX_APPS, catalog.apps.size)
    }

    @Test
    fun clearForgetsTheListAndItsIcons() {
        catalog.setList(blob("1.0\tAlpha"))
        catalog.setIcon("1.0", png(10))

        catalog.clear()

        assertFalse(catalog.loaded)
        assertNull(catalog.epoch)
        assertTrue(catalog.apps.isEmpty())
        assertNull(catalog.icon("1.0"))
    }

    @Test
    fun anIconIsKeptByItsToken() {
        catalog.setList(blob("1.0\tAlpha", "1.1\tBeta"))
        val picture = png(100)

        assertTrue(catalog.setIcon("1.1", picture))

        assertArrayEquals(picture, catalog.icon("1.1"))
        assertNull(catalog.icon("1.0"))
    }

    @Test
    fun anIconForATokenNotInTheListIsIgnored() {
        catalog.setList(blob("2.0\tAlpha"))

        assertFalse(catalog.setIcon("1.0", png(10)))
        assertFalse(catalog.setIcon("2.9", png(10)))
        assertFalse(catalog.setIcon("", png(10)))

        assertNull(catalog.icon("1.0"))
        assertNull(catalog.icon("2.9"))
    }

    @Test
    fun anIconOverEightKilobytesIsRejected() {
        catalog.setList(blob("1.0\tAlpha"))

        assertFalse(catalog.setIcon("1.0", png(AppCatalogState.MAX_ICON_BYTES + 1)))
        assertNull(catalog.icon("1.0"))

        assertTrue(catalog.setIcon("1.0", png(AppCatalogState.MAX_ICON_BYTES)))
        assertEquals(AppCatalogState.MAX_ICON_BYTES, catalog.icon("1.0")?.size)
    }

    @Test
    fun anEmptyIconIsRejected() {
        catalog.setList(blob("1.0\tAlpha"))

        assertFalse(catalog.setIcon("1.0", ByteArray(0)))
    }

    @Test
    fun aTokenIsDigitsADotAndDigits() {
        assertEquals(12 to 345, AppCatalogState.parseToken("12.345"))
        assertEquals(0 to 0, AppCatalogState.parseToken("0.0"))
        for (bad in listOf("", ".", "1", "1.", ".1", "1.2.3", "a.1", "1.b", "-1.2", "1.-2", " 1.2", "1.2 ", "1,2")) {
            assertNull(bad, AppCatalogState.parseToken(bad))
        }
    }

    @Test
    fun aTokenTooBigForAnIntIsRefused() {
        assertNull(AppCatalogState.parseToken("99999999999.1"))
        assertNull(AppCatalogState.parseToken("1.99999999999"))
    }

    @Test
    fun anEmptyQueryFindsEveryApp() {
        val apps = listOf(app("Alpha"), app("Beta"))

        assertEquals(apps, AppCatalogState.filter(apps, ""))
        assertEquals(apps, AppCatalogState.filter(apps, "   "))
    }

    @Test
    fun aQueryMatchesAnySubstringIgnoringCase() {
        val apps = listOf(app("Google Chrome"), app("Chromium"), app("Notes"))

        assertEquals(listOf("Google Chrome", "Chromium"), AppCatalogState.filter(apps, "CHROM").map { it.name })
        assertEquals(listOf("Google Chrome"), AppCatalogState.filter(apps, "e chr").map { it.name })
        assertEquals(listOf("Notes"), AppCatalogState.filter(apps, "  notes ").map { it.name })
    }

    @Test
    fun aQueryThatMatchesNothingFindsNothing() {
        assertTrue(AppCatalogState.filter(listOf(app("Alpha")), "zzz").isEmpty())
    }

    @Test
    fun accentsAreNotFolded() {
        val apps = listOf(app("Café"), app("Cafe Bar"))

        assertEquals(listOf("Cafe Bar"), AppCatalogState.filter(apps, "cafe").map { it.name })
        assertEquals(listOf("Café"), AppCatalogState.filter(apps, "café").map { it.name })
    }

    @Test
    fun everyStatusTokenMapsToItsStatus() {
        val expected =
            mapOf(
                "added" to AppAddStatus.ADDED,
                "full" to AppAddStatus.FULL,
                "duplicate" to AppAddStatus.DUPLICATE,
                "stale" to AppAddStatus.STALE,
                "failed" to AppAddStatus.FAILED,
            )
        for ((token, status) in expected) {
            catalog.addStatus(token)

            assertEquals(token, status, catalog.lastStatus)
        }
    }

    @Test
    fun anUnknownStatusTokenReadsAsFailed() {
        catalog.addStatus("added")

        catalog.addStatus("a-later-laptop-says-this")

        assertEquals(AppAddStatus.FAILED, catalog.lastStatus)
    }

    @Test
    fun aStatusArrivesAsATextFrame() {
        val state = LaptopState()

        assertTrue(state.take(Frame.Text(TextKind.APP_ADD_STATUS.id, "duplicate")))

        assertEquals(AppAddStatus.DUPLICATE, state.apps.lastStatus)
    }

    // A PNG's signature and then filler: this state keeps bytes and never decodes them.
    private fun png(size: Int): ByteArray = ByteArray(size) { if (it < PNG_START.size) PNG_START[it] else 0 }

    private companion object {
        const val EMOJI = 0x1F600
        val PNG_START = byteArrayOf(-119, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
    }
}
