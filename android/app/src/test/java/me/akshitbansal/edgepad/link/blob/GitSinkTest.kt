package me.akshitbansal.edgepad.link.blob

import me.akshitbansal.edgepad.link.GitData
import me.akshitbansal.edgepad.link.GitSection
import me.akshitbansal.edgepad.link.GitState
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.CiToken
import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class GitSinkTest {
    private val got = ArrayList<GitData>()
    private val sink = GitSink { got.add(it) }

    private fun send(
        kind: BlobKind,
        body: ByteArray,
    ) {
        sink.take(Frame.BlobBegin(kind.id, body.size.toLong(), "git"))
        body.toList().chunked(255).forEachIndexed { seq, part -> sink.take(Frame.BlobChunk(seq, part.toByteArray())) }
        sink.take(Frame.BlobEnd(sha256(body)))
    }

    private fun lines(vararg rows: String) = rows.joinToString("\n").toByteArray()

    private val page =
        lines(
            "H\tgithub\toctocat\tMona Lisa\t24\t12\t7",
            "N\t\t3\t\t\tnone",
            "P\tocto/cat\t\tKotlin\t31\t3\t1",
            "P\tocto/web\tp\t\t4\t0\t0",
            "M\tocto/cat\t12\tFix the thing\td\tfail\topen\t29000000",
            "R\tocto/web\t5\tAdd a page\t\tok\tmerged\t0",
            "A\tocto/cat\t9\tCrash on start\ti\tnone\tclosed\t29000100",
        )

    @Test
    fun aPageArrivesParsed() {
        send(BlobKind.GIT, page)
        val data = got.single()
        assertEquals("github", data.host)
        assertEquals("octocat", data.login)
        assertEquals("Mona Lisa", data.name)
        assertEquals(24, data.repoCount)
        assertEquals(12, data.followers)
        assertEquals(7L, data.rev)
        assertEquals(listOf("octo/cat", "octo/web"), data.repos.map { it.name })
        assertEquals(listOf(0, 1), data.repos.map { it.index })
        assertEquals(listOf("Kotlin", ""), data.repos.map { it.language })
        assertEquals(listOf(31, 4), data.repos.map { it.stars })
        val mine = data.items[0]
        assertEquals(GitSection.MINE, mine.section)
        assertEquals(2, mine.index)
        assertEquals("12", mine.ref)
        assertTrue(mine.draft)
        assertEquals(CiToken.FAIL, mine.ci)
        assertEquals(GitState.OPEN, mine.state)
        assertEquals(29000000L, mine.updatedMin)
        assertEquals(GitState.MERGED, data.items[1].state)
        assertTrue(data.items[2].issue)
        assertEquals(GitState.CLOSED, data.items[2].state)
    }

    @Test
    fun aSecondBlobIsAnotherDelivery() {
        send(BlobKind.GIT, lines("H\tgithub\ta\t\t1\t1\t1"))
        send(BlobKind.GIT, lines("H\tgitlab\tb\t\t1\t1\t2"))
        assertEquals(listOf("github", "gitlab"), got.map { it.host })
        assertEquals(2L, got.last().rev)
    }

    @Test
    fun overTheCapAndOtherKindsAreDropped() {
        send(BlobKind.GIT, ByteArray(GitSink.MAX_BYTES + 1) { 'a'.code.toByte() })
        send(BlobKind.PERM_ASK, page)
        assertTrue(got.isEmpty())
    }

    @Test
    fun hostileTitlesStayLiteralAndInvisibleOnesAreShown() {
        send(BlobKind.GIT, lines("M\tme/app\t1\t<b>bold</b> \u202e x\t\tok\topen\t0"))
        assertEquals("<b>bold</b> <U+202E> x", got.single().items.single().title)
    }

    @Test
    fun aBrokenLineKeepsItsPlaceInTheIndexCount() {
        send(
            BlobKind.GIT,
            lines(
                "H\tgithub\ta\t\t0\t0\t1",
                "P\tbroken",
                "M\tme/app\tx\tt\t\tok\topen\t0\textra",
                "M\tme/app\t4\tfine\t\tok\topen\t0",
            ),
        )
        val data = got.single()
        assertTrue(data.repos.isEmpty())
        assertEquals(2, data.items.single().index)
    }

    @Test
    fun aTextIdIsKeptAsTheNumber() {
        send(BlobKind.GIT, lines("M\tme/app\tPR-7a\tt\t\tnone\topen\t0"))
        assertEquals("PR-7a", got.single().items.single().ref)
    }

    @Test
    fun theOlderShapeShowsItsPullRequestsWithoutAnIndex() {
        send(BlobKind.GIT, lines("N\t\t3\t\t\tnone", "R\tme/app\t12\tFix the thing\td\tfail"))
        val data = got.single()
        assertEquals("github", data.host)
        assertEquals(0L, data.rev)
        val row = data.items.single()
        assertEquals(GitSection.REVIEW, row.section)
        assertEquals("12", row.ref)
        assertEquals(-1, row.index)
        assertEquals(CiToken.FAIL, row.ci)
    }
}
