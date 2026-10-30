package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Test

class KeyRepeatTest {
    private var now = 0L
    private val queue = ArrayList<Pair<Long, Runnable>>()
    private val fired = ArrayList<Int>()
    private val repeat =
        KeyRepeat(
            post = { task, delay -> queue.add(now + delay to task) },
            cancel = { task -> queue.removeAll { it.second === task } },
            fire = { fired.add(it) },
        )

    /** Moves the clock forward, running whatever falls due on the way, in order. */
    private fun advance(ms: Long) {
        val end = now + ms
        while (true) {
            val next = queue.minByOrNull { it.first }?.takeIf { it.first <= end } ?: break
            queue.remove(next)
            now = next.first
            next.second.run()
        }
        now = end
    }

    @Test
    fun nothingFiresBeforeTheDelay() {
        repeat.press(A)
        advance(REPEAT_DELAY_MS - 1)
        assertEquals(emptyList<Int>(), fired)
    }

    @Test
    fun firesAtTheDelayAndThenEveryInterval() {
        repeat.press(A)
        advance(REPEAT_DELAY_MS)
        assertEquals(listOf(A), fired)
        advance(REPEAT_EVERY_MS * 2)
        assertEquals(listOf(A, A, A), fired)
    }

    @Test
    fun releaseStopsIt() {
        repeat.press(A)
        advance(REPEAT_DELAY_MS)
        repeat.release(A)
        advance(1_000)
        assertEquals(listOf(A), fired)
    }

    @Test
    fun onlyTheNewestKeyRepeatsAndReleasingTheOlderChangesNothing() {
        repeat.press(A)
        repeat.press(B)
        repeat.release(A)
        advance(REPEAT_DELAY_MS + REPEAT_EVERY_MS)
        assertEquals(listOf(B, B), fired)
    }

    @Test
    fun releasingTheNewestStopsEvenWithTheOlderStillDown() {
        repeat.press(A)
        repeat.press(B)
        repeat.release(B)
        advance(1_000)
        assertEquals(emptyList<Int>(), fired)
    }

    @Test
    fun stopClearsIt() {
        repeat.press(A)
        repeat.stop()
        advance(1_000)
        assertEquals(emptyList<Int>(), fired)
    }

    @Test
    fun aKeyThatWasNeverPressedReleasesHarmlessly() {
        repeat.release(A)
        repeat.press(B)
        repeat.release(A)
        advance(REPEAT_DELAY_MS)
        assertEquals(listOf(B), fired)
    }

    private companion object {
        const val A = 65
        const val B = 66
    }
}
