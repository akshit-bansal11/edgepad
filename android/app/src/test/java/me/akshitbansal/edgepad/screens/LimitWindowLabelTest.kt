package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.link.LimitEntry
import me.akshitbansal.edgepad.link.LimitWindow
import me.akshitbansal.edgepad.protocol.AgentToken
import me.akshitbansal.edgepad.screens.LimitLabel.Key
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class LimitWindowLabelTest {
    @Test
    fun table() {
        val cases =
            listOf(
                LimitWindow.Minutes(30) to LimitLabel(Key.MINUTES, 30),
                LimitWindow.Minutes(300) to LimitLabel(Key.HOURS, 5),
                LimitWindow.Minutes(1440) to LimitLabel(Key.DAYS, 1),
                LimitWindow.Minutes(4320) to LimitLabel(Key.DAYS, 3),
                LimitWindow.Minutes(10_080) to LimitLabel(Key.WEEK),
                LimitWindow.Minutes(43_200) to LimitLabel(Key.MONTH),
                LimitWindow.Minutes(90) to LimitLabel(Key.HOURS, 1),
                LimitWindow.Minutes(525_600) to LimitLabel(Key.DAYS, 365),
                LimitWindow.Context to LimitLabel(Key.CONTEXT),
                LimitWindow.Named("spend_limit") to LimitLabel(Key.CREDITS),
                LimitWindow.Named("weird_kind") to LimitLabel(Key.TEXT, text = "weird_kind"),
                LimitWindow.Named("seven_day_fable") to LimitLabel(Key.TEXT, text = "Fable"),
                LimitWindow.Named("seven_day_sonnet") to LimitLabel(Key.TEXT, text = "Sonnet"),
                LimitWindow.Named("seven_day_") to LimitLabel(Key.TEXT, text = "seven_day_"),
            )
        for ((window, expected) in cases) assertEquals(window.text, expected, LimitWindowLabel.of(window))
    }

    @Test
    fun aModelNameIsReadableAndTheCreditsLimitSortsLast() {
        assertEquals("Opus 4", LimitWindowLabel.modelName("seven_day_opus_4"))
        assertTrue(LimitWindowLabel.isCredits(LimitWindow.Named("spend_limit")))
        assertFalse(LimitWindowLabel.isCredits(LimitWindow.Named("seven_day_fable")))
        val limits =
            listOf(
                LimitEntry(AgentToken.CLAUDE, LimitWindow.Context, 5, -1, 1),
                LimitEntry(AgentToken.CLAUDE, LimitWindow.Named("spend_limit"), 6, -1, 1),
                LimitEntry(AgentToken.CLAUDE, LimitWindow.Named("seven_day_fable"), 7, -1, 1),
                LimitEntry(AgentToken.CLAUDE, LimitWindow.Minutes(10_080), 8, -1, 1),
                LimitEntry(AgentToken.CLAUDE, LimitWindow.Minutes(300), 9, -1, 1),
            )
        assertEquals(listOf(9, 8, 7, 6, 5), limits.sortedWith(LimitWindowLabel.shortestFirst).map { it.pct })
    }
}
