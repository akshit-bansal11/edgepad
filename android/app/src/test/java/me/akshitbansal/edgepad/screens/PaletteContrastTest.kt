package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import kotlin.math.pow

class PaletteContrastTest {
    private fun colour(
        file: String,
        name: String,
    ): Int {
        val text = File("src/main/res/$file/colors.xml").readText()
        val hex = Regex("""<color name="$name">#([0-9A-Fa-f]{6})</color>""").find(text)!!.groupValues[1]
        return hex.toInt(16)
    }

    private fun luminance(rgb: Int): Double {
        fun channel(shift: Int): Double {
            val c = ((rgb shr shift) and 0xFF) / 255.0
            return if (c <= 0.03928) c / 12.92 else ((c + 0.055) / 1.055).pow(2.4)
        }
        return 0.2126 * channel(16) + 0.7152 * channel(8) + 0.0722 * channel(0)
    }

    private fun ratio(
        a: Int,
        b: Int,
    ): Double {
        val x = luminance(a)
        val y = luminance(b)
        return (maxOf(x, y) + 0.05) / (minOf(x, y) + 0.05)
    }

    @Test
    fun everyLevelColourPassesOnTheCardInBothThemes() {
        for (folder in listOf("values", "values-night")) {
            val card = colour(folder, "card")
            for (name in listOf("level_green", "level_yellow", "level_orange", "level_red")) {
                val r = ratio(colour(folder, name), card)
                assertTrue("$folder $name $r", r >= 4.5)
            }
        }
    }
}
