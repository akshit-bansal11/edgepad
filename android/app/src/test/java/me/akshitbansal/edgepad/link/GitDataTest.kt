package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.CiToken
import org.junit.Assert.assertEquals
import org.junit.Test

class GitDataTest {
    private fun item(
        repo: String,
        section: GitSection = GitSection.MINE,
        ci: CiToken = CiToken.NONE,
        state: GitState = GitState.OPEN,
        draft: Boolean = false,
    ) = GitItem(0, section, repo, "1", "t", draft, false, ci, state, 0)

    private fun page(vararg items: GitItem) = GitData("github", "me", "Me", 1, 1, 9, emptyList(), items.toList())

    @Test
    fun itemsGroupByRepoInFirstSeenOrderAndFilterBySection() {
        val data =
            page(
                item("a/one"),
                item("b/two", GitSection.REVIEW),
                item("a/one", GitSection.ASSIGNED),
                item("c/three"),
                item("a/one"),
            )
        val mine = data.grouped(GitSection.MINE)
        assertEquals(listOf("a/one", "c/three"), mine.map { it.first })
        assertEquals(listOf(2, 1), mine.map { it.second.size })
        assertEquals(listOf("b/two"), data.grouped(GitSection.REVIEW).map { it.first })
        assertEquals(listOf("a/one"), data.grouped(GitSection.ASSIGNED).map { it.first })
    }

    @Test
    fun theBadgeSaysOneThing() {
        assertEquals(GitBadge.CHECKS_FAILED, item("a/b", ci = CiToken.FAIL).badge())
        assertEquals(GitBadge.CHECKS_PASSED, item("a/b", ci = CiToken.OK).badge())
        assertEquals(GitBadge.CHECKS_RUNNING, item("a/b", ci = CiToken.PENDING).badge())
        assertEquals(GitBadge.DRAFT, item("a/b", draft = true).badge())
        assertEquals(GitBadge.DRAFT, item("a/b", ci = CiToken.OK, draft = true).badge())
        assertEquals(GitBadge.CHECKS_FAILED, item("a/b", ci = CiToken.FAIL, draft = true).badge())
        assertEquals(GitBadge.OPEN, item("a/b").badge())
        assertEquals(GitBadge.MERGED, item("a/b", ci = CiToken.FAIL, state = GitState.MERGED).badge())
        assertEquals(GitBadge.CLOSED, item("a/b", draft = true, state = GitState.CLOSED).badge())
    }

    @Test
    fun gitOpenNamesTheHostTheRevisionAndTheIndex() {
        assertEquals("github/9/4", GitWire.openText(page(), 4))
        assertEquals("gitlab/0/0", GitWire.openText(GitData("gitlab", "", "", 0, 0, 0, emptyList(), emptyList()), 0))
    }

    @Test
    fun theInstallIndexIsTheNumberOfIndexedLines() {
        val body =
            listOf(
                "H\tgithub\tme\tMe\t1\t1\t9",
                "P\ta/one\t\tKotlin\t1\t0\t0",
                "M\ta/one\t3\tFix\t\tnone\topen\t0",
                "M\ta/bad\t3",
                "R\ta/one\t4\tReview\t\tok\topen\t0",
            ).joinToString("\n")
        val data = GitWire.parse(body.toByteArray())
        assertEquals(4, data.installIndex)
        assertEquals("github/9/4", GitWire.openText(data, data.installIndex))
    }

    @Test
    fun everyHostIdIsItsFeatureToken() {
        assertEquals(
            listOf("github", "gitlab", "gitea", "bitbucket", "gitee"),
            GitHost.entries.map { it.id },
        )
    }
}
