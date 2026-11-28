package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** Every service name in protocol/media-services.txt that has a logo must resolve to one; the rest get the plain symbol. */
class AppLogoNamesTest {
    @Test
    fun everyNamedServiceWithALogoResolvesToOne() {
        val rows = services()
        assertTrue(rows.isNotEmpty())
        for ((name, hasLogo) in rows) {
            for (dark in listOf(false, true)) {
                if (hasLogo) {
                    assertNotNull("$name has no logo", logoResource(name, dark))
                } else {
                    assertNull("$name should show the plain symbol", logoResource(name, dark))
                }
            }
        }
    }

    @Test
    fun theNamesThatAreEasyToMissResolve() {
        for (name in listOf("X", "Apple TV+", "Sky")) {
            assertNotNull(name, logoResource(name, false))
        }
    }

    @Test
    fun aBrowserOrAnUnknownNameGetsThePlainSymbol() {
        for (name in listOf("Chrome", "Edge", "Firefox", "", "Some Player")) {
            assertNull(name, logoResource(name, false))
        }
    }

    private fun services(): List<Pair<String, Boolean>> =
        fixture()
            .readLines(Charsets.UTF_8)
            .filter { it.startsWith("SERVICE ") }
            .map { line -> line.removePrefix("SERVICE ").substringBefore("|") to !line.contains("# no logo") }

    private fun fixture(): File {
        var dir: File? = File("").absoluteFile
        while (dir != null) {
            val candidate = File(dir, "protocol/media-services.txt")
            if (candidate.isFile) return candidate
            dir = dir.parentFile
        }
        error("protocol/media-services.txt was not found above ${File("").absolutePath}")
    }
}
