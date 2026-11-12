package me.akshitbansal.edgepad.screens

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class TextChunksTest {
    @Test
    fun shortTextIsOnePiece() {
        assertEquals(listOf("hello"), TextChunks.split("hello"))
        assertTrue(TextChunks.split("").isEmpty())
    }

    @Test
    fun longTextSplitsUnderTheLimitAndJoinsBack() {
        val text = "é".repeat(400) + "😀".repeat(100)
        val pieces = TextChunks.split(text)
        assertTrue(pieces.all { it.toByteArray(Charsets.UTF_8).size <= TextChunks.MAX_BYTES })
        assertEquals(text, pieces.joinToString(""))
    }
}
