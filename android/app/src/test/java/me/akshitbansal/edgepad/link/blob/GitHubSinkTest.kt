package me.akshitbansal.edgepad.link.blob

import me.akshitbansal.edgepad.link.GithubRow
import me.akshitbansal.edgepad.link.GithubSection
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.CiToken
import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class GitHubSinkTest {
    private val got = ArrayList<List<GithubRow>>()
    private val sink = GitHubSink { got.add(it) }

    private fun send(
        kind: BlobKind,
        body: ByteArray,
    ) {
        sink.take(Frame.BlobBegin(kind.id, body.size.toLong(), "gh"))
        body.toList().chunked(255).forEachIndexed { seq, part -> sink.take(Frame.BlobChunk(seq, part.toByteArray())) }
        sink.take(Frame.BlobEnd(sha256(body)))
    }

    private fun lines(vararg rows: String) = rows.joinToString("\n").toByteArray()

    @Test
    fun rowsArriveParsed() {
        send(
            BlobKind.GITHUB,
            lines("N\t\t3\t\t\tnone", "R\tme/app\t12\tFix the thing\td\tfail"),
        )
        val rows = got.single()
        assertEquals(2, rows.size)
        assertEquals(GithubRow(GithubSection.NOTIFICATIONS, "", 3, "", false, CiToken.NONE), rows[0])
        assertEquals(GithubRow(GithubSection.REVIEW, "me/app", 12, "Fix the thing", true, CiToken.FAIL), rows[1])
    }

    @Test
    fun aSecondBlobIsAnotherDelivery() {
        send(BlobKind.GITHUB, lines("N\t\t1\t\t\tnone"))
        send(BlobKind.GITHUB, lines("N\t\t2\t\t\tnone"))
        assertEquals(2, got.size)
        assertEquals(2, got.last().single().number)
    }

    @Test
    fun overTheCapAndOtherKindsAreDropped() {
        send(BlobKind.GITHUB, ByteArray(GitHubSink.MAX_BYTES + 1) { 'a'.code.toByte() })
        send(BlobKind.PERM_ASK, lines("N\t\t1\t\t\tnone"))
        assertTrue(got.isEmpty())
    }

    @Test
    fun hostileTitlesStayLiteral() {
        send(BlobKind.GITHUB, lines("M\tme/app\t1\t<b>bold</b> \u202e x\t\tok"))
        assertEquals("<b>bold</b> \u202e x", got.single().single().title)
    }
}
