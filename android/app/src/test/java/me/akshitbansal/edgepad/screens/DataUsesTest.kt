package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** The "What Edgepad uses" screen must list every permission the manifest declares, and nothing the manifest dropped. */
class DataUsesTest {
    private fun declared(): Set<String> {
        val comments = Regex("<!--.*?-->", RegexOption.DOT_MATCHES_ALL)
        val manifest = manifestFile().readText(Charsets.UTF_8).replace(comments, "")
        return Regex("""<uses-permission\s[^>]*android:name="([^"]+)"""")
            .findAll(manifest)
            .map { it.groupValues[1] }
            .toSet()
    }

    private fun manifestFile(): File {
        var dir: File? = File("").absoluteFile
        while (dir != null) {
            for (path in listOf("src/main/AndroidManifest.xml", "android/app/src/main/AndroidManifest.xml")) {
                val file = File(dir, path)
                if (file.isFile) return file
            }
            dir = dir.parentFile
        }
        throw AssertionError("AndroidManifest.xml not found")
    }

    @Test
    fun theManifestDeclaresPermissions() {
        assertTrue(declared().isNotEmpty())
    }

    @Test
    fun everyDeclaredPermissionIsListed() {
        val listed = DataUses.PERMISSIONS.map { DataUses.PREFIX + it.name }.toSet()
        assertEquals("declared in the manifest but missing from DataUses", emptySet<String>(), declared() - listed)
    }

    @Test
    fun everyListedPermissionIsDeclared() {
        val listed = DataUses.PERMISSIONS.map { DataUses.PREFIX + it.name }.toSet()
        assertEquals("listed in DataUses but not in the manifest", emptySet<String>(), listed - declared())
    }
}
