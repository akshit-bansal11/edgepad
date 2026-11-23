package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.io.File

class AppLanguagesTest {
    /** The picker offers exactly the languages that have a values-* folder, plus English from values. */
    @Test
    fun tagsAreTheResourceFolders() {
        val res = File("src/main/res")
        val folders =
            res
                .listFiles { f -> f.isDirectory && f.name.startsWith("values-") }
                .orEmpty()
                .map { it.name.removePrefix("values-") }
                .filter { it != "night" && !it.startsWith("night-") }
                .map { it.replace("-r", "-") }
                .toSet() + "en"
        assertEquals(folders, AppLanguages.all.map { it.tag }.toSet())
    }

    @Test
    fun matchesExactThenLanguage() {
        assertEquals("pt-BR", AppLanguages.match("pt-BR")?.tag)
        assertEquals("pt-BR", AppLanguages.match("pt-PT")?.tag)
        assertEquals("de", AppLanguages.match("de-AT")?.tag)
        assertEquals("zh-CN", AppLanguages.match("zh-TW")?.tag)
        assertNull(AppLanguages.match("sv"))
    }

    @Test
    fun everyNameIsFilledAndUnique() {
        val names = AppLanguages.all.map { it.name }
        assertEquals(names.size, names.toSet().size)
        assertEquals(true, names.none { it.isBlank() })
    }
}
