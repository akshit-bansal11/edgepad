package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.DeviceOs
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.PadStatus
import me.akshitbansal.edgepad.protocol.SignStatus
import me.akshitbansal.edgepad.protocol.TextKind
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Base64

class LaptopStateTest {
    private val state = LaptopState()

    @Test
    fun keepsTheLatestLevelAndFlagPerControl() {
        assertNull(state.level(ControlId.VOLUME))
        assertTrue(state.take(Frame.StateReport(ControlId.VOLUME.id, 42, 1)))
        assertEquals(42, state.level(ControlId.VOLUME))
        assertTrue(state.flag(ControlId.VOLUME))
        state.take(Frame.StateReport(ControlId.VOLUME.id, 43, 0))
        assertEquals(43, state.level(ControlId.VOLUME))
        assertFalse(state.flag(ControlId.VOLUME))
    }

    @Test
    fun keepsTheLaptopsLastWordOnItsController() {
        assertNull(state.padStatus)
        assertTrue(state.take(Frame.Text(TextKind.PAD_STATUS.id, PadStatus.READY.token)))
        assertEquals(PadStatus.READY, state.padStatus)
        // A token this build does not know means the pad is unusable and the phone cannot say why, which
        // is exactly what NO_DRIVER already means to the screen.
        state.take(Frame.Text(TextKind.PAD_STATUS.id, "unplugged-by-the-cat"))
        assertEquals(PadStatus.NO_DRIVER, state.padStatus)
    }

    @Test
    fun readsTheFeaturesTheLaptopNames() {
        assertEquals(emptySet<Feature>(), state.features)
        assertTrue(state.take(Frame.Text(TextKind.FEATURES.id, "annotate/capture")))
        assertEquals(setOf(Feature.ANNOTATE, Feature.CAPTURE), state.features)
        state.take(Frame.Text(TextKind.FEATURES.id, "capture"))
        assertEquals(setOf(Feature.CAPTURE), state.features)
        // Nothing named means nothing on, and a token this build does not know is ignored, not an error.
        state.take(Frame.Text(TextKind.FEATURES.id, ""))
        assertEquals(emptySet<Feature>(), state.features)
        state.take(Frame.Text(TextKind.FEATURES.id, "junk"))
        assertEquals(emptySet<Feature>(), state.features)
        state.take(Frame.Text(TextKind.FEATURES.id, "junk/capture"))
        assertEquals(setOf(Feature.CAPTURE), state.features)
    }

    @Test
    fun readsTheScreenSizeAndKeepsTheLastGoodOne() {
        assertNull(state.screenSize)
        assertTrue(state.take(Frame.Text(TextKind.SCREEN_SIZE.id, "1920/1080")))
        assertEquals(1920 to 1080, state.screenSize)
        // A size that cannot be a screen is not taken, and the last good one stays rather than going to zero.
        for (bad in listOf("", "1920", "0/1080", "1920/-1", "a/b", "1/2/3")) {
            assertFalse(bad, state.take(Frame.Text(TextKind.SCREEN_SIZE.id, bad)))
        }
        assertEquals(1920 to 1080, state.screenSize)
    }

    @Test
    fun keepsHowTheLastSignatureSaveEnded() {
        assertNull(state.signStatus)
        assertTrue(state.take(Frame.Text(TextKind.SIGN_STATUS.id, "saved")))
        assertEquals(SignStatus.SAVED, state.signStatus)
        state.take(Frame.Text(TextKind.SIGN_STATUS.id, "empty"))
        assertEquals(SignStatus.EMPTY, state.signStatus)
        // A token this build does not know must not look like a success.
        state.take(Frame.Text(TextKind.SIGN_STATUS.id, "uploaded"))
        assertEquals(SignStatus.FAILED, state.signStatus)
    }

    @Test
    fun anUnknownControlIsNotKept() {
        assertFalse(state.take(Frame.StateReport(9, 1, 0)))
    }

    @Test
    fun readsWhatIsPlayingAndWhere() {
        state.take(Frame.Text(TextKind.NOW_PLAYING.id, "Polygon Window · Aphex Twin"))
        state.take(Frame.Text(TextKind.APP.id, "Spotify"))
        state.take(Frame.Text(TextKind.TIMELINE.id, "84/227"))
        assertEquals("Polygon Window · Aphex Twin", state.nowPlaying)
        assertEquals("Spotify", state.app)
        assertEquals(84, state.position)
        assertEquals(227, state.duration)
    }

    @Test
    fun aTimelineThatDoesNotParseMeansNone() {
        state.take(Frame.Text(TextKind.TIMELINE.id, "84/227"))
        state.take(Frame.Text(TextKind.TIMELINE.id, ""))
        assertEquals(-1, state.position)
        assertEquals(0, state.duration)
        state.take(Frame.Text(TextKind.TIMELINE.id, "5/0"))
        assertEquals(0, state.duration)
    }

    @Test
    fun readsTheRefreshRatesTheLaptopOffers() {
        assertTrue(state.take(Frame.Text(TextKind.REFRESH_RATES.id, "60/120/144")))
        assertEquals(listOf(60, 120, 144), state.refreshRates)
    }

    @Test
    fun aRateListWithAnUnreadableEntryIsNoList() {
        state.take(Frame.Text(TextKind.REFRESH_RATES.id, "60/120/144"))
        // Dropping the bad entry would slide every index after it onto the wrong rate.
        state.take(Frame.Text(TextKind.REFRESH_RATES.id, "60//144"))
        assertEquals(emptyList<Int>(), state.refreshRates)
    }

    @Test
    fun readsTheMacroNamesTheLaptopOffers() {
        assertTrue(state.take(Frame.Text(TextKind.MACROS.id, "Chrome/Spotify/ Notes ")))
        assertEquals(listOf("Chrome", "Spotify", "Notes"), state.macros)
    }

    @Test
    fun aBlankMacroNameKeepsItsSlot() {
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome//Notes"))
        // Dropping the empty name would send index 1 for Notes, and the laptop would launch slot 1 instead.
        assertEquals(listOf("Chrome", "", "Notes"), state.macros)
        assertEquals(2, state.macros.indexOf("Notes"))
    }

    @Test
    fun noMacrosAtAllIsAnEmptyList() {
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome/Spotify"))
        state.take(Frame.Text(TextKind.MACROS.id, ""))
        assertEquals(emptyList<String>(), state.macros)
    }

    @Test
    fun namesPastTheReservedBlockAreDropped() {
        // The grid holds fifteen, so a sixteenth name has no button to sit on. The reserved action block is
        // wider than that on purpose, but the laptop never fills past the grid. Fifteen is written out
        // rather than read from LaptopState: the number is what this test exists to pin down, and a test
        // that asks the code what it does agrees with it whatever it does.
        state.take(Frame.Text(TextKind.MACROS.id, (1..40).joinToString("/") { "M$it" }))
        assertEquals(15, state.macros.size)
        assertEquals("M15", state.macros.last())
    }

    @Test
    fun readsWhichMacroAppsAreOpen() {
        assertTrue(state.take(Frame.Text(TextKind.MACRO_OPEN.id, "-012")))
        assertEquals(listOf(OpenState.UNTRACKED, OpenState.CLOSED, OpenState.OPEN, OpenState.FRONT), state.macroOpen)
        assertEquals(listOf(false, false, true, true), (0..3).map(state::isOpen))
        assertFalse("a slot past the list is not open", state.isOpen(4))
    }

    @Test
    fun anUnknownOpenCharacterReadsAsUntracked() {
        state.take(Frame.Text(TextKind.MACRO_OPEN.id, "1x"))
        assertEquals(listOf(OpenState.OPEN, OpenState.UNTRACKED), state.macroOpen)
        assertFalse(state.isOpen(1))
    }

    @Test
    fun nothingIsOpenUntilTheLaptopSays() {
        assertEquals(emptyList<OpenState>(), state.macroOpen)
        assertFalse(state.isOpen(0))
    }

    @Test
    fun aChangedMacroListForgetsWhatWasOpen() {
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome/Notes"))
        state.take(Frame.Text(TextKind.MACRO_OPEN.id, "12"))

        // The same list sent again moves no slot, and the laptop would not say the same state twice.
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome/Notes"))
        assertTrue(state.isOpen(0))

        state.take(Frame.Text(TextKind.MACROS.id, "Notes/Chrome"))
        assertEquals(emptyList<OpenState>(), state.macroOpen)
    }

    @Test
    fun forgettingWhatWasOpenLeavesNothingOpen() {
        state.take(Frame.Text(TextKind.MACRO_OPEN.id, "2"))
        state.forgetMacroOpen()
        assertFalse(state.isOpen(0))
    }

    @Test
    fun framesThatAreNotStateAreNotKept() {
        assertFalse(state.take(Frame.Pong(1)))
        // A kind no build knows, and one this end never receives: WANT_ICONS only ever goes the other way.
        assertFalse(state.take(Frame.Text(99, "x")))
        assertFalse(state.take(Frame.Text(TextKind.WANT_ICONS.id, "")))
    }

    @Test
    fun joinsAnIconFromItsChunks() {
        // The other half of this format is MacroIconChunks.Chunks on the laptop, and the two are only held
        // together by the C# suite and this one agreeing about it. Both are written out by hand for that
        // reason: a helper shared with the code would agree with the code whatever the code did.
        val png = bytes(600)

        val pieces = chunks(slot = 2, png = png)
        val kept = pieces.map { state.take(Frame.Text(TextKind.MACRO_ICON.id, it)) }

        assertTrue("600 bytes should not fit one chunk", pieces.size > 1)
        // Only the last piece is state worth redrawing for; the rest would repaint the same grid.
        assertEquals(List(pieces.size - 1) { false } + true, kept)
        assertArrayEquals(png, state.macroIcon(2))
        // What lets an open grid redraw the one button this finished instead of every button it has.
        assertEquals(2, state.lastIcon)
    }

    @Test
    fun anIconIsNotThereUntilItIsWhole() {
        val png = bytes(600)
        val pieces = chunks(slot = 0, png = png)

        pieces.dropLast(1).forEach { state.take(Frame.Text(TextKind.MACRO_ICON.id, it)) }

        assertNull("a half-arrived icon must not draw", state.macroIcon(0))
        assertEquals("a half-arrived icon finished nothing", -1, state.lastIcon)
    }

    @Test
    fun oneChunkIsAWholeIconWhenItFits() {
        val png = bytes(30)

        val pieces = chunks(slot = 4, png = png)

        assertEquals(1, pieces.size)
        assertTrue(state.take(Frame.Text(TextKind.MACRO_ICON.id, pieces[0])))
        assertArrayEquals(png, state.macroIcon(4))
    }

    @Test
    fun aChunkThatDoesNotParseIsDropped() {
        val nonsense =
            listOf(
                "",
                "2",
                "2/0/1",
                "x/0/1/QUFB",
                "2/x/1/QUFB",
                "2/0/x/QUFB",
                // A slot past the grid, a count of none, and a piece numbered past its own count.
                "99/0/1/QUFB",
                "2/0/0/QUFB",
                "2/5/2/QUFB",
                // A count no icon could need. The guard is what stops a corrupted number sizing an array.
                "2/0/9999999/QUFB",
            )

        nonsense.forEach { assertFalse(it, state.take(Frame.Text(TextKind.MACRO_ICON.id, it))) }
        assertNull(state.macroIcon(2))
    }

    @Test
    fun aChunkThatIsNotBase64IsDroppedRatherThanThrown() {
        assertFalse(state.take(Frame.Text(TextKind.MACRO_ICON.id, "1/0/1/not base64 at all")))
        assertNull(state.macroIcon(1))
    }

    @Test
    fun aNewListRetiresTheIconsThatWentWithIt() {
        // A slot number means a different macro once the list changes, so an icon kept against one would end
        // up on whatever moved into it — the failure that looks like the laptop launching the wrong thing.
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome/Notes"))
        chunks(slot = 1, png = bytes(30)).forEach { state.take(Frame.Text(TextKind.MACRO_ICON.id, it)) }
        assertNull(state.macroIcon(0))
        assertTrue(state.macroIcon(1) != null)

        state.take(Frame.Text(TextKind.MACROS.id, "Notes/Chrome"))

        assertNull(state.macroIcon(1))
    }

    @Test
    fun anUnchangedMacroKeepsItsIconWhenTheListIsSentAgain() {
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome/Notes"))
        val png = bytes(30)
        chunks(slot = 0, png = png).forEach { state.take(Frame.Text(TextKind.MACRO_ICON.id, it)) }

        // A third macro was added: the first two have not changed, so the cache still holds the first one.
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome/Notes/Spotify"))

        assertArrayEquals(png, state.macroIcon(0))
    }

    @Test
    fun aDeletedMacroLosesItsIconAndTheOthersKeepTheirs() {
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome/Notes/Spotify"))
        val png = bytes(30)
        chunks(slot = 0, png = png).forEach { state.take(Frame.Text(TextKind.MACRO_ICON.id, it)) }
        chunks(slot = 2, png = png).forEach { state.take(Frame.Text(TextKind.MACRO_ICON.id, it)) }

        // Spotify is gone from the list.
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome/Notes"))

        assertArrayEquals(png, state.macroIcon(0))
        assertNull(state.macroIcon(2))
    }

    @Test
    fun aMacroBlankedInTheListLosesItsIcon() {
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome/Notes"))
        chunks(slot = 1, png = bytes(30)).forEach { state.take(Frame.Text(TextKind.MACRO_ICON.id, it)) }

        state.take(Frame.Text(TextKind.MACROS.id, "Chrome/"))

        assertNull(state.macroIcon(1))
    }

    @Test
    fun anEmptyListEmptiesTheCache() {
        state.take(Frame.Text(TextKind.MACROS.id, "Chrome"))
        chunks(slot = 0, png = bytes(30)).forEach { state.take(Frame.Text(TextKind.MACRO_ICON.id, it)) }

        state.take(Frame.Text(TextKind.MACROS.id, ""))

        assertNull(state.macroIcon(0))
    }

    @Test
    fun anIconSentAgainReplacesTheOneBeforeIt() {
        val first = bytes(30)
        val second = bytes(600)
        chunks(slot = 3, png = first).forEach { state.take(Frame.Text(TextKind.MACRO_ICON.id, it)) }

        chunks(slot = 3, png = second).forEach { state.take(Frame.Text(TextKind.MACRO_ICON.id, it)) }

        assertArrayEquals(second, state.macroIcon(3))
    }

    @Test
    fun aFullSizeIconOfNinetySixKilobytesIsAccepted() {
        val png = bytes(96 * 1024)
        val pieces = chunks(slot = 1, png = png)

        pieces.forEach { state.take(Frame.Text(TextKind.MACRO_ICON.id, it)) }

        assertEquals(547, pieces.size)
        assertArrayEquals(png, state.macroIcon(1))
    }

    /** Bytes that do not repeat, so a wrongly joined icon cannot happen to equal the right one. */
    private fun bytes(size: Int): ByteArray = ByteArray(size) { (it * 31 % 251).toByte() }

    /**
     * What MacroIconChunks.Chunks produces on the laptop, written out again here rather than shared: 240 bytes
     * of base64 per frame behind a "slot/chunk/chunks/" header.
     */
    private fun chunks(
        slot: Int,
        png: ByteArray,
    ): List<String> {
        val encoded = Base64.getEncoder().encodeToString(png)
        val count = (encoded.length + CHUNK_BYTES - 1) / CHUNK_BYTES
        return (0 until count).map { i ->
            val start = i * CHUNK_BYTES
            "$slot/$i/$count/" + encoded.substring(start, minOf(start + CHUNK_BYTES, encoded.length))
        }
    }

    @Test
    fun readsTheLaptopsIdentityFromDevice() {
        val id = "0123456789abcdef0123456789abcdef"
        assertTrue(state.take(Frame.Text(TextKind.DEVICE.id, "$id/windows/Office/PC")))
        assertEquals(id, state.laptopId)
        assertEquals(DeviceOs.WINDOWS, state.os)
        assertEquals("Office/PC", state.laptopName)
    }

    @Test
    fun aDeviceTokenThisBuildDoesNotKnowLeavesOsNull() {
        val id = "0123456789abcdef0123456789abcdef"
        assertTrue(state.take(Frame.Text(TextKind.DEVICE.id, "$id/haiku/Desk")))
        assertEquals(null, state.os)
        assertEquals("Desk", state.laptopName)
    }

    @Test
    fun malformedDeviceTextIsIgnored() {
        val id = "0123456789abcdef0123456789abcdef"
        assertTrue(state.take(Frame.Text(TextKind.DEVICE.id, "$id/linux/Good")))
        for (bad in listOf("$id", "$id/linux", "$id/linux/", "0123/linux/Short", "$id-x/linux/Name")) {
            assertFalse(bad, state.take(Frame.Text(TextKind.DEVICE.id, bad)))
        }
        assertEquals(id, state.laptopId)
        assertEquals("Good", state.laptopName)
        assertEquals(DeviceOs.LINUX, state.os)
    }

    private companion object {
        /** MacroIconChunks.ChunkBytes on the laptop. Written out, because agreeing with it is the point. */
        const val CHUNK_BYTES = 240
    }
}
