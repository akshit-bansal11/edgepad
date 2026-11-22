package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.CiToken
import me.akshitbansal.edgepad.protocol.Feature

/** The git hosts the phone can show, in the laptop's display order. The wire's provider id is the feature token. */
enum class GitHost(
    val feature: Feature,
    val label: String,
) {
    GITHUB(Feature.GITHUB, "GitHub"),
    GITLAB(Feature.GITLAB, "GitLab"),
    GITEA(Feature.GITEA, "Gitea"),
    BITBUCKET(Feature.BITBUCKET, "Bitbucket"),
    GITEE(Feature.GITEE, "Gitee"),
    ;

    /** The provider id: the host field of the blob and of a [GitWire.openText]. */
    val id: String
        get() = feature.token
}

/** Which of the three lists an item is in. */
enum class GitSection(
    val letter: String,
) {
    MINE("M"),
    REVIEW("R"),
    ASSIGNED("A"),
    ;

    companion object {
        fun of(letter: String): GitSection? = entries.firstOrNull { it.letter == letter }
    }
}

/** An item's state on the host. */
enum class GitState(
    val token: String,
) {
    OPEN("open"),
    CLOSED("closed"),
    MERGED("merged"),
    ;

    companion object {
        /** An unknown token reads as [OPEN]. */
        fun of(token: String): GitState = entries.firstOrNull { it.token == token } ?: OPEN
    }
}

/** What the badge of an item says. */
enum class GitBadge {
    CHECKS_FAILED,
    CHECKS_PASSED,
    CHECKS_RUNNING,
    DRAFT,
    MERGED,
    CLOSED,
    OPEN,
}

/** One repository line. [index] is its place among the blob's P, M, R and A lines, the number `GitOpen` names. */
data class GitRepo(
    val index: Int,
    val name: String,
    val language: String,
    val stars: Int,
    val issues: Int,
    val pulls: Int,
    val hidden: Boolean,
)

/**
 * One pull request or issue line. [ref] is its number, or the host's own text id (at most 20 characters). An [index] of
 * -1 has no place a laptop could open: a line of the older shape.
 */
data class GitItem(
    val index: Int,
    val section: GitSection,
    val repo: String,
    val ref: String,
    val title: String,
    val draft: Boolean,
    val issue: Boolean,
    val ci: CiToken,
    val state: GitState,
    val updatedMin: Long,
) {
    /** The one thing the badge says: a finished item says so, then failed checks, then draft, then the rest. */
    fun badge(): GitBadge =
        when {
            state == GitState.MERGED -> GitBadge.MERGED
            state == GitState.CLOSED -> GitBadge.CLOSED
            ci == CiToken.FAIL -> GitBadge.CHECKS_FAILED
            draft -> GitBadge.DRAFT
            ci == CiToken.OK -> GitBadge.CHECKS_PASSED
            ci == CiToken.PENDING -> GitBadge.CHECKS_RUNNING
            else -> GitBadge.OPEN
        }
}

/** One host's page as the laptop last sent it. [rev] is what `GitOpen` quotes back. */
data class GitData(
    val host: String,
    val login: String,
    val name: String,
    val repoCount: Int,
    val followers: Int,
    val rev: Long,
    val repos: List<GitRepo>,
    val items: List<GitItem>,
) {
    /** The items of [section], grouped by repository in the order each repository first appears. */
    fun grouped(section: GitSection): List<Pair<String, List<GitItem>>> =
        items.filter { it.section == section }.groupBy { it.repo }.toList()
}

/** The reader of BLOB GIT, and the text of `GitOpen`. */
object GitWire {
    private const val MAX_REPOS = 30
    private const val MAX_PER_SECTION = 20
    private const val H_FIELDS = 7
    private const val REPO_FIELDS = 7
    private const val ITEM_FIELDS = 8
    private const val MAX_REF = 20
    private const val OLD_ITEM_FIELDS = 6
    private const val DEFAULT_HOST = "github"

    /**
     * The page in [body]. A line that does not fit its shape is dropped but still takes its place in the count of P, M,
     * R and A lines, so every kept line keeps the index the laptop gave it. A laptop that still sends the older shape
     * (no H line, six fields to an item) gets its pull requests shown as GitHub's, with no index to open.
     */
    fun parse(body: ByteArray): GitData {
        var host = DEFAULT_HOST
        var login = ""
        var name = ""
        var repoCount = 0
        var followers = 0
        var rev = 0L
        var index = 0
        val repos = ArrayList<GitRepo>()
        val items = ArrayList<GitItem>()
        val perSection = HashMap<GitSection, Int>()
        val old = StringBuilder()
        for (line in String(body, Charsets.UTF_8).split('\n')) {
            val p = line.split('\t')
            val section = GitSection.of(p[0])
            when {
                p[0] == "H" && p.size == H_FIELDS -> {
                    host = p[1]
                    login = DashWire.cleanText(p[2])
                    name = DashWire.cleanText(p[3])
                    repoCount = count(p[4])
                    followers = count(p[5])
                    rev = p[6].toLongOrNull()?.coerceAtLeast(0) ?: 0
                }

                p[0] == "P" -> {
                    if (p.size == REPO_FIELDS && p[1].isNotEmpty() && repos.size < MAX_REPOS) {
                        val language = DashWire.cleanText(p[3])
                        val hidden = p[2] == "p"
                        repos += GitRepo(index, p[1], language, count(p[4]), count(p[5]), count(p[6]), hidden)
                    }
                    index++
                }

                section != null && p.size == OLD_ITEM_FIELDS -> {
                    old.append(line).append('\n')
                }

                section != null -> {
                    val seen = perSection.getOrDefault(section, 0)
                    val ref = p.getOrNull(2)?.let { DashWire.cleanText(it).take(MAX_REF) }.orEmpty()
                    val fits = p.size == ITEM_FIELDS && '/' in p[1] && ref.isNotBlank() && seen < MAX_PER_SECTION
                    if (fits) {
                        perSection[section] = seen + 1
                        items += item(index, section, p, ref)
                    }
                    index++
                }
            }
        }
        if (old.isNotEmpty()) items += oldItems(old.toString())
        return GitData(host, login, name, repoCount, followers, rev, repos, items)
    }

    /** The text of a `GitOpen` for the line at [index] of the page [data]: `host/rev/index`, never an address. */
    fun openText(
        data: GitData,
        index: Int,
    ): String = "${data.host}/${data.rev}/$index"

    private fun count(text: String): Int = text.toIntOrNull()?.coerceAtLeast(0) ?: 0

    private fun item(
        index: Int,
        section: GitSection,
        p: List<String>,
        ref: String,
    ) = GitItem(
        index = index,
        section = section,
        repo = p[1],
        ref = ref,
        title = DashWire.cleanText(p[3]),
        draft = 'd' in p[4],
        issue = 'i' in p[4],
        ci = CiToken.of(p[5]),
        state = GitState.of(p[6]),
        updatedMin = p[7].toLongOrNull()?.coerceAtLeast(0) ?: 0,
    )

    private fun oldItems(lines: String): List<GitItem> =
        DashWire.github(lines.toByteArray(Charsets.UTF_8)).mapNotNull { row ->
            val section = GitSection.of(row.section.char.toString())
            section?.let {
                GitItem(-1, it, row.repo, row.number.toString(), row.title, row.draft, false, row.ci, GitState.OPEN, 0)
            }
        }
}
