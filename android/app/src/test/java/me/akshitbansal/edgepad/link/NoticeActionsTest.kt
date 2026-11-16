package me.akshitbansal.edgepad.link

import org.junit.Assert.assertEquals
import org.junit.Test

/** Which buttons the notification carries. */
class NoticeActionsTest {
    @Test
    fun connectedHasFourInOrder() {
        assertEquals(
            listOf(
                NoticeAction.VOLUME_DOWN,
                NoticeAction.PLAY_PAUSE,
                NoticeAction.VOLUME_UP,
                NoticeAction.DISCONNECT,
            ),
            noticeActions(LinkNotice("Desk", 0)),
        )
    }

    @Test
    fun reconnectingHasOnlyDisconnect() {
        assertEquals(listOf(NoticeAction.DISCONNECT), noticeActions(LinkNotice("Desk", 0, reconnecting = true)))
    }
}
