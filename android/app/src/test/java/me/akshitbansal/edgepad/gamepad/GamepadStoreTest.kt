package me.akshitbansal.edgepad.gamepad

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [GamepadLibrary] — the rules [GamepadStore] wraps around SharedPreferences. Nothing here touches
 * Android, which is the point of the library being a value: what a collision costs and what is left
 * playing after a deletion are decisions, and a decision that only a phone can run is a decision nobody
 * checks.
 */
class GamepadStoreTest {
    @Test
    fun aFreshLibraryPlaysTheFirstLayoutEdgepadShipsWith() {
        val fresh = GamepadLibrary(emptyList(), "")
        assertEquals(GamepadLayout.presets.first(), fresh.current)
        assertEquals(GamepadLayout.presets, fresh.all)
    }

    @Test
    fun savingUnderABuiltInNameShadowsItRatherThanListingTheNameTwice() {
        val edited = moved(GamepadLayout.presets.first())
        val library = GamepadLibrary(emptyList(), "").save(edited)
        assertEquals(GamepadLayout.presets.size, library.all.size)
        assertEquals(edited, library.current)
        assertEquals(1, library.all.count { it.name == edited.name })
    }

    @Test
    fun deletingALayoutThatShadowedABuiltInOneBringsTheBuiltInOneBackAndStaysOnIt() {
        val original = GamepadLayout.presets.first()
        val library = GamepadLibrary(emptyList(), "").save(moved(original)).delete(original.name)
        assertEquals(original, library.current)
        assertTrue(library.saved.isEmpty())
    }

    @Test
    fun deletingTheLayoutBeingPlayedLeavesTheFirstOfWhatIsStillThereBeingPlayed() {
        val mine = GamepadLayout.presets.first().copy(name = "Elden Ring")
        val library = GamepadLibrary(listOf(mine), "Elden Ring").delete("Elden Ring")
        assertEquals(library.all.first(), library.current)
        assertFalse(library.all.any { it.name == "Elden Ring" })
    }

    @Test
    fun deletingSomethingElseLeavesWhatIsBeingPlayedAlone() {
        val mine = GamepadLayout.presets.first().copy(name = "Elden Ring")
        val other = GamepadLayout.presets.first().copy(name = "Hades")
        val library = GamepadLibrary(listOf(mine, other), "Elden Ring").delete("Hades")
        assertEquals("Elden Ring", library.current.name)
    }

    @Test
    fun savingUnderATakenNameGetsANumberAndOverwritesNothing() {
        val first = GamepadLayout.presets.first()
        val library = GamepadLibrary(emptyList(), "").saveAs(first, "Elden Ring").saveAs(first, "Elden Ring")
        assertEquals(listOf("Elden Ring", "Elden Ring 2"), library.saved.map { it.name })
        assertEquals("Elden Ring 2", library.currentName)
    }

    @Test
    fun savingUnderTheNameOfALayoutEdgepadShipsWithGetsANumberToo() {
        val name = GamepadLayout.presets.first().name
        val library = GamepadLibrary(emptyList(), "").saveAs(GamepadLayout.presets.first(), name)
        assertEquals("$name 2", library.currentName)
    }

    @Test
    fun aNameWithNothingUsableInItChangesNothing() {
        val library = GamepadLibrary(emptyList(), "")
        assertEquals(library, library.saveAs(GamepadLayout.presets.first(), "  |  "))
        assertEquals(library, library.rename(GamepadLayout.presets.first().name, "|"))
    }

    @Test
    fun renamingMovesTheLayoutAndWhatIsBeingPlayedWithIt() {
        val mine = GamepadLayout.presets.first().copy(name = "Elden Ring")
        val library = GamepadLibrary(listOf(mine), "Elden Ring").rename("Elden Ring", "Hades")
        assertEquals(listOf("Hades"), library.saved.map { it.name })
        assertEquals("Hades", library.currentName)
    }

    @Test
    fun renamingOverATakenNameGetsANumberRatherThanSwallowingTheOtherLayout() {
        val mine = GamepadLayout.presets.first().copy(name = "Elden Ring")
        val other = GamepadLayout.presets.first().copy(name = "Hades")
        val library = GamepadLibrary(listOf(mine, other), "Elden Ring").rename("Elden Ring", "Hades")
        assertEquals(setOf("Hades", "Hades 2"), library.saved.map { it.name }.toSet())
        assertEquals("Hades 2", library.currentName)
    }

    @Test
    fun renamingALayoutEdgepadShipsWithTakesACopyAndLeavesTheBuiltInOneWhereItWas() {
        val built = GamepadLayout.presets.first()
        val library = GamepadLibrary(emptyList(), built.name).rename(built.name, "Elden Ring")
        assertEquals("Elden Ring", library.currentName)
        assertTrue(library.all.contains(built))
        assertEquals(built.controls, library.current.controls)
    }

    @Test
    fun resetPutsABuiltInLayoutBackTheWayEdgepadShipsIt() {
        val built = GamepadLayout.presets.first()
        val library = GamepadLibrary(emptyList(), "").save(moved(built)).reset()
        assertEquals(built, library.current)
        assertTrue(library.saved.isEmpty())
    }

    @Test
    fun resetOnALayoutOfTheUsersOwnMovesOffItRatherThanDeletingIt() {
        val mine = GamepadLayout.presets.first().copy(name = "Elden Ring")
        val library = GamepadLibrary(listOf(mine), "Elden Ring").reset()
        assertEquals(GamepadLayout.presets.first(), library.current)
        assertEquals(listOf(mine), library.saved)
    }

    @Test
    fun choosingSomethingNothingAnswersToChangesNothing() {
        val library = GamepadLibrary(emptyList(), "")
        assertEquals(library, library.choose("Elden Ring"))
    }

    @Test
    fun theUsersOwnLayoutsAreListedBeforeTheOnesEdgepadShipsWith() {
        val mine = GamepadLayout.presets.first().copy(name = "Elden Ring")
        val library = GamepadLibrary(listOf(mine), "Elden Ring")
        assertEquals("Elden Ring", library.all.first().name)
        assertEquals(GamepadLayout.presets.size + 1, library.all.size)
    }

    @Test
    fun aWholeLibraryRoundTripsThroughTheTextItIsStoredAs() {
        val saved =
            listOf(
                GamepadLayout.presets.first().copy(name = "Elden Ring"),
                GamepadLayout.presets.last().copy(name = "Hades"),
            )
        assertEquals(saved, GamepadLayout.decodeAll(GamepadLayout.encodeAll(saved)))
    }

    /** The same layout with one control dragged, which is what the editor stores after a drag. */
    private fun moved(layout: GamepadLayout): GamepadLayout {
        val first = layout.controls.first()
        return layout.copy(controls = listOf(first.copy(x = 0.25f, y = 0.25f)) + layout.controls.drop(1))
    }
}
