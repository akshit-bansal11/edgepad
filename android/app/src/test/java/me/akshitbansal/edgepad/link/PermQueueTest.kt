package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.AgentToken
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PermQueueTest {
    private fun ask(
        n: Int,
        text: String = "ls",
    ) = PermAsk("%012x".format(n), true, AgentToken.CLAUDE, "Bash", text)

    private fun PermQueue.ids() = asks.map { it.id }

    @Test
    fun keepsOrderAndIgnoresADuplicateId() {
        val q = PermQueue()
        assertTrue(q.add(ask(1)))
        assertTrue(q.add(ask(2)))
        assertFalse(q.add(ask(1, "rm -rf /")))
        assertEquals(listOf(ask(1).id, ask(2).id), q.ids())
        assertEquals("ls", q.current?.text)
    }

    @Test
    fun dropsTheFifth() {
        val q = PermQueue()
        (1..4).forEach { assertTrue(q.add(ask(it))) }
        assertFalse(q.add(ask(5)))
        assertEquals(PermQueue.MAX_WAITING, q.asks.size)
        assertEquals(ask(1).id, q.current?.id)
    }

    @Test
    fun goneRemovesOnlyThatId() {
        val q = PermQueue()
        q.add(ask(1))
        q.add(ask(2))
        assertTrue(q.gone(ask(1).id))
        assertFalse(q.gone(ask(1).id))
        assertEquals(listOf(ask(2).id), q.ids())
    }

    @Test
    fun answersOnceAndNeverQueuesTheIdAgain() {
        val q = PermQueue()
        q.add(ask(1))
        assertEquals(ask(1).id + "/a", q.answer(ask(1).id, allow = true))
        assertNull(q.answer(ask(1).id, allow = true))
        assertNull(q.answer(ask(1).id, allow = false))
        assertFalse(q.add(ask(1)))
        q.sync(listOf(ask(1)))
        assertNull(q.current)
    }

    @Test
    fun denyIsTheDText() {
        val q = PermQueue()
        q.add(ask(7))
        assertEquals(ask(7).id + "/d", q.answer(ask(7).id, allow = false))
    }

    @Test
    fun unknownIdAnswersNothing() {
        assertNull(PermQueue().answer(ask(9).id, allow = true))
    }

    @Test
    fun syncFollowsTheLaptopList() {
        val q = PermQueue()
        q.sync(listOf(ask(1), ask(2)))
        assertEquals(listOf(ask(1).id, ask(2).id), q.ids())
        q.sync(listOf(ask(2), ask(3)))
        assertEquals(listOf(ask(2).id, ask(3).id), q.ids())
        q.sync(emptyList())
        assertNull(q.current)
    }

    @Test
    fun anAnsweredIdTheLaptopForgotMayComeBack() {
        val q = PermQueue()
        q.sync(listOf(ask(1)))
        q.answer(ask(1).id, allow = false)
        q.sync(emptyList())
        q.sync(listOf(ask(1)))
        assertEquals(ask(1).id, q.current?.id)
    }

    @Test
    fun allowEnabledTruthTable() {
        assertFalse(allowEnabled(complete = false, fits = false, atEnd = false))
        assertFalse(allowEnabled(complete = false, fits = false, atEnd = true))
        assertFalse(allowEnabled(complete = false, fits = true, atEnd = false))
        assertFalse(allowEnabled(complete = false, fits = true, atEnd = true))
        assertFalse(allowEnabled(complete = true, fits = false, atEnd = false))
        assertTrue(allowEnabled(complete = true, fits = false, atEnd = true))
        assertTrue(allowEnabled(complete = true, fits = true, atEnd = false))
        assertTrue(allowEnabled(complete = true, fits = true, atEnd = true))
    }
}
