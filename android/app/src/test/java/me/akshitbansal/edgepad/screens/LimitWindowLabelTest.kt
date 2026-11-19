package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.link.LimitWindow
import me.akshitbansal.edgepad.screens.LimitLabel.Key
import org.junit.Assert.assertEquals
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
                LimitWindow.Named("spend_limit") to LimitLabel(Key.SPEND),
                LimitWindow.Named("weird_kind") to LimitLabel(Key.TEXT, text = "weird_kind"),
            )
        for ((window, expected) in cases) assertEquals(window.text, expected, LimitWindowLabel.of(window))
    }
}
