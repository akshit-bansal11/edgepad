package me.akshitbansal.edgepad.screens

import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.InsetDrawable
import android.text.TextUtils
import android.text.format.DateUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import me.akshitbansal.edgepad.Logos
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.GitBadge
import me.akshitbansal.edgepad.link.GitData
import me.akshitbansal.edgepad.link.GitHost
import me.akshitbansal.edgepad.link.GitItem
import me.akshitbansal.edgepad.link.GitRepo
import me.akshitbansal.edgepad.link.GitSection
import me.akshitbansal.edgepad.link.GitWire
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.protocol.Feature
import me.akshitbansal.edgepad.protocol.UnavailableReason

/**
 * The git page of Developer: the repositories of the account on the laptop, and its pull requests and issues grouped by
 * repository under a Mine, Review and Assigned switch, for each connected git host (a chip each when there is more than
 * one). Sideways the profile and repositories take a fixed column and the lists the rest; upright they stack.
 *
 * The phone only asks. A tap on a repository or an item hands [onOpen] the `GitOpen` text `host/revision/index`, and
 * the laptop opens the page in its own browser from an address it fetched itself: no address is ever in what the phone
 * receives, and no picture is fetched here (an avatar is a drawn initial). Every string the laptop sent goes in with
 * `setText`.
 *
 * Nothing polls: the laptop sends the page while the phone says this screen is open, and the activity calls [refresh]
 * when a new one arrived.
 *
 * @param state The laptop this screen shows, read afresh on each [refresh].
 * @param onOpen Handed the `GitOpen` text of a tapped repository or item.
 */
class GitScreen(
    private val ui: Ui,
    private val state: () -> LaptopState,
    bar: View,
    private val onOpen: (String) -> Unit,
) {
    private val holder = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
    private var host: GitHost? = null
    private var section = GitSection.MINE

    val view: View = ui.page(bar) { add(holder) }

    init {
        refresh()
    }

    /** Draws the page the laptop sent last. */
    fun refresh() {
        holder.removeAllViews()
        Column(ui, holder).render(state())
    }

    private fun Column.render(s: LaptopState) {
        // A host the laptop refused since it said FEATURES (signed out or revoked) is left out; a later UNAVAILABLE
        // without it, once a new token works, brings it back.
        val hosts =
            GitHost.entries.filter {
                it.feature in s.features && s.unavailable[it.feature.token] != UnavailableReason.NO_PERMISSION
            }
        // GitLab's gitlab.com sign-in lapsed (the laptop could not renew it): said here, not dropped silently.
        val gitlabExpired = s.unavailable[Feature.GITLAB.token] == UnavailableReason.NO_PERMISSION
        if (hosts.isEmpty()) {
            val again = s.unavailable[Feature.GITHUB.token] == UnavailableReason.NO_PERMISSION
            val line =
                when {
                    gitlabExpired -> R.string.unavailable_gitlab_expired
                    again -> R.string.unavailable_github_resign
                    else -> R.string.git_signin
                }
            card { note(ui.string(line)) }
            return
        }
        if (gitlabExpired) card { note(ui.string(R.string.unavailable_gitlab_expired)) }
        val current = hosts.firstOrNull { it == host } ?: hosts.first()
        host = current
        if (hosts.size > 1) add(chips(hosts, current))
        val data = s.git[current.id]
        if (data == null) {
            card { note(ui.string(R.string.git_loading)) }
            return
        }
        sideBySide(
            first = { profile(data) },
            second = { lists(data) },
        )
    }

    /** One chip a host, the chosen one on the accent's soft fill. */
    private fun chips(
        hosts: List<GitHost>,
        current: GitHost,
    ): LinearLayout =
        LinearLayout(ui.context).apply {
            val inset = (ui.dp(Space.TOUCH) - ui.dp(CHIP_DP)) / 2
            for (entry in hosts) {
                val on = entry == current
                val label =
                    ui.text(
                        entry.label,
                        Type.SMALL,
                        if (on) ui.palette.accent else ui.palette.ink,
                        face = if (on) Type.bold else Type.face,
                    )
                val chip =
                    LinearLayout(ui.context).apply {
                        gravity = Gravity.CENTER
                        minimumHeight = ui.dp(Space.TOUCH)
                        Logos.drawable(entry.id)?.let { addView(mark(it, entry == GitHost.GITHUB)) }
                        addView(label)
                        val fill = if (on) ui.palette.accentSoft else ui.palette.faint
                        background = InsetDrawable(ui.rounded(fill, CHIP_RADIUS_DP), 0, inset, 0, inset)
                        // After the background, which resets the padding to its insets.
                        setPadding(ui.dp(Space.S), 0, ui.dp(Space.S), 0)
                        ui.tappable(this, CHIP_RADIUS_DP) {
                            host = entry
                            refresh()
                        }
                    }
                addView(chip, LinearLayout.LayoutParams(WRAP, WRAP).apply { marginEnd = ui.dp(Space.S) })
            }
            setPadding(0, 0, 0, ui.dp(Space.XS))
        }

    /** A brand mark beside a chip's name; [tinted] for a one-colour mark that would vanish on a dark page. */
    private fun mark(
        drawable: Int,
        tinted: Boolean,
    ) = ImageView(ui.context).apply {
        setImageResource(drawable)
        if (tinted) setColorFilter(ui.palette.ink)
        importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
        layoutParams = square(MARK_DP, Space.XS)
    }

    /** Two runs: a fixed column and the rest sideways, one after the other upright. */
    private fun Column.sideBySide(
        first: Column.() -> Unit,
        second: Column.() -> Unit,
    ) {
        if (!ui.landscape) {
            first()
            second()
            return
        }
        val pair = LinearLayout(ui.context)
        val left = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        val right = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        Column(ui, left).first()
        Column(ui, right).second()
        pair.addView(left, LinearLayout.LayoutParams(ui.dp(PROFILE_DP), WRAP))
        pair.addView(right, LinearLayout.LayoutParams(0, WRAP, 1f).apply { marginStart = ui.dp(Space.XL) })
        add(pair)
    }

    private fun Column.profile(data: GitData) {
        card {
            add(who(data), Space.M)
            add(
                ui.text(ui.string(R.string.git_repos_label).uppercase(), Type.MICRO, ui.palette.dim, face = Type.bold),
                Space.M,
            )
            if (data.repos.isEmpty()) note(ui.string(R.string.git_empty_repos))
            data.repos.forEach { add(repo(data, it)) }
            if (data.host == GitHost.GITHUB.id) add(installRow(data))
            add(View(ui.context), height = ui.dp(Space.S))
        }
    }

    /** A quiet line under GitHub's repositories: the laptop opens the page where the read-only App is installed. */
    private fun installRow(data: GitData): View =
        ui.secondary(ui.string(R.string.git_install_row), Type.MICRO).apply {
            minimumHeight = ui.dp(Space.TOUCH)
            gravity = Gravity.CENTER_VERTICAL
            ui.tappable(this) { open(GitWire.openText(data, data.installIndex)) }
        }

    /** The avatar (a drawn initial), the name, and `@login · n repos · n followers`. */
    private fun who(data: GitData): LinearLayout {
        val shown = data.name.ifBlank { data.login }
        val initial = if (shown.isEmpty()) "?" else String(Character.toChars(shown.codePointAt(0)))
        val avatar =
            ui.text(initial.uppercase(), Type.LABEL, ui.palette.onAccent, face = Type.bold).apply {
                gravity = Gravity.CENTER
                background =
                    GradientDrawable().apply {
                        shape = GradientDrawable.OVAL
                        setColor(ui.palette.accent)
                    }
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            }
        val resources = ui.context.resources
        val parts =
            listOf(
                "@${data.login}",
                resources.getQuantityString(R.plurals.git_repos_n, data.repoCount, data.repoCount),
                resources.getQuantityString(R.plurals.git_followers_n, data.followers, data.followers),
            )
        val names =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(oneLine(ui.text(shown, Type.VALUE, ui.palette.ink, face = Type.bold)))
                addView(oneLine(ui.secondary(parts.joinToString(SEPARATOR), Type.MICRO)))
            }
        return LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            addView(avatar, square(AVATAR_DP, Space.M))
            addView(names, LinearLayout.LayoutParams(0, WRAP, 1f))
        }
    }

    /** A repository: its name over `language · ★ n · n issues · n PRs`, and an arrow for the laptop's browser. */
    private fun repo(
        data: GitData,
        repo: GitRepo,
    ): LinearLayout {
        val resources = ui.context.resources
        val meta =
            listOfNotNull(
                repo.language.ifEmpty { null },
                "★ ${repo.stars}",
                resources.getQuantityString(R.plurals.git_issues_n, repo.issues, repo.issues),
                resources.getQuantityString(R.plurals.git_prs_n, repo.pulls, repo.pulls),
            )
        val lines =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(nameLine(repo))
                addView(oneLine(ui.secondary(meta.joinToString(SEPARATOR), Type.MICRO)))
            }
        val arrow = ui.text("↗", Type.BODY, ui.palette.accent).apply { importantForAccessibility = IGNORE }
        return LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            minimumHeight = ui.dp(REPO_DP)
            setPadding(0, ui.dp(Space.XS), 0, ui.dp(Space.XS))
            addView(lines, LinearLayout.LayoutParams(0, WRAP, 1f))
            addView(arrow, LinearLayout.LayoutParams(WRAP, WRAP).apply { marginStart = ui.dp(Space.S) })
            ui.tappable(this) { open(GitWire.openText(data, repo.index)) }
        }
    }

    /** The repository's name, and a small dim lock after it when the repository is private. */
    private fun nameLine(repo: GitRepo): LinearLayout =
        LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            addView(oneLine(ui.text(repo.name, NAME_SP, ui.palette.ink, face = Type.bold)))
            if (repo.hidden) {
                val lock =
                    ImageView(ui.context).apply {
                        setImageResource(R.drawable.ic_lock)
                        setColorFilter(ui.palette.dim)
                        importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                    }
                val side = ui.dp(MARK_DP)
                addView(lock, LinearLayout.LayoutParams(side, side).apply { marginStart = ui.dp(Space.XS) })
            }
        }

    /** `Pull requests and issues` with the switch, then the items of the chosen list grouped by repository. */
    private fun Column.lists(data: GitData) {
        val list = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        card {
            val names =
                listOf(R.string.git_mine, R.string.git_review, R.string.git_assigned).map { ui.string(it) }
            val switch =
                ui.segmented(names, GitSection.entries.indexOf(section)) {
                    section = GitSection.entries[it]
                    fill(list, data)
                }
            val title = ui.text(ui.string(R.string.git_lists_title), Type.VALUE, ui.palette.ink, face = Type.bold)
            add(ui.segmentedField(title, switch))
            add(list)
        }
        fill(list, data)
    }

    private fun fill(
        list: LinearLayout,
        data: GitData,
    ) {
        list.removeAllViews()
        val groups = data.grouped(section)
        val column = Column(ui, list)
        if (groups.isEmpty()) {
            val empty =
                when (section) {
                    GitSection.MINE -> R.string.git_empty_mine
                    GitSection.REVIEW -> R.string.git_empty_review
                    GitSection.ASSIGNED -> R.string.git_empty_assigned
                }
            column.note(ui.string(empty))
            return
        }
        for ((repo, rows) in groups) {
            column.add(
                oneLine(ui.text(repo.uppercase(), Type.MICRO, ui.palette.dim, face = Type.bold)),
                Space.M,
            )
            rows.forEach { column.add(item(data, it)) }
        }
    }

    /** A pull request or issue: its icon, title over `#n · updated …`, and the badge, which is one line and whole. */
    private fun item(
        data: GitData,
        item: GitItem,
    ): LinearLayout {
        val icon =
            ImageView(ui.context).apply {
                setImageResource(if (item.issue) R.drawable.ic_git_issue else R.drawable.ic_git_pr)
                setColorFilter(if (item.issue) ui.palette.accent else ui.palette.levelGreen)
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            }
        val lines =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(oneLine(ui.text(item.title, NAME_SP, ui.palette.ink, face = Type.bold)))
                addView(oneLine(ui.secondary(meta(item), Type.MICRO)))
            }
        return LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            minimumHeight = ui.dp(ITEM_DP)
            setPadding(0, ui.dp(Space.XS), 0, ui.dp(Space.XS))
            addView(icon, square(ICON_DP, Space.M))
            addView(lines, LinearLayout.LayoutParams(0, WRAP, 1f))
            addView(badge(item.badge()), LinearLayout.LayoutParams(WRAP, WRAP).apply { marginStart = ui.dp(Space.S) })
            if (item.index >= 0) ui.tappable(this) { open(GitWire.openText(data, item.index)) }
        }
    }

    private fun meta(item: GitItem): String =
        if (item.updatedMin <= 0L) {
            ui.string(R.string.git_item_meta_undated, item.ref)
        } else {
            val ago =
                DateUtils.getRelativeTimeSpanString(
                    item.updatedMin * MS_PER_MIN,
                    System.currentTimeMillis(),
                    DateUtils.MINUTE_IN_MILLIS,
                )
            ui.string(R.string.git_item_meta, item.ref, ago.toString())
        }

    /** The badge: one line, never ellipsized, never narrower than its text. Failed checks are in the danger colour. */
    private fun badge(kind: GitBadge): TextView {
        val (label, color) =
            when (kind) {
                GitBadge.CHECKS_FAILED -> R.string.git_ci_fail to ui.palette.danger
                GitBadge.CHECKS_PASSED -> R.string.git_ci_ok to ui.palette.levelGreen
                GitBadge.CHECKS_RUNNING -> R.string.git_ci_pending to ui.palette.ink
                GitBadge.DRAFT -> R.string.git_draft to ui.palette.ink
                GitBadge.MERGED -> R.string.git_state_merged to ui.palette.ink
                GitBadge.CLOSED -> R.string.git_state_closed to ui.palette.ink
                GitBadge.OPEN -> R.string.git_state_open to ui.palette.ink
            }
        val text = ui.string(label)
        return ui.text(text, Type.MICRO, color, face = Type.bold).apply {
            maxLines = 1
            ellipsize = null
            setHorizontallyScrolling(true)
            background = ui.rounded(ui.palette.faint, BADGE_RADIUS_DP)
            setPadding(ui.dp(Space.S), ui.dp(Space.XS), ui.dp(Space.S), ui.dp(Space.XS))
            minWidth = paint.measureText(text).toInt() + paddingLeft + paddingRight
        }
    }

    private fun open(text: String) {
        Toast.makeText(ui.context, R.string.git_opening, Toast.LENGTH_SHORT).show()
        onOpen(text)
    }

    /** A square of [side] dp with [endDp] dp after it. */
    private fun square(
        side: Float,
        endDp: Float,
    ) = LinearLayout.LayoutParams(ui.dp(side), ui.dp(side)).apply { marginEnd = ui.dp(endDp) }

    private fun oneLine(view: TextView): TextView =
        view.apply {
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
        }

    private fun Column.note(text: String) {
        body(text).setPadding(0, ui.dp(Space.M), 0, ui.dp(Space.M))
    }

    private companion object {
        const val PROFILE_DP = 270f
        const val AVATAR_DP = 40f
        const val REPO_DP = Space.TOUCH
        const val ITEM_DP = Space.TOUCH
        const val ICON_DP = 18f
        const val MARK_DP = 16f
        const val CHIP_DP = 32f
        const val CHIP_RADIUS_DP = 10f
        const val BADGE_RADIUS_DP = 10f
        const val MS_PER_MIN = 60_000L
        const val NAME_SP = 14f
        const val SEPARATOR = " · "
        const val IGNORE = View.IMPORTANT_FOR_ACCESSIBILITY_NO
        const val WRAP = ViewGroup.LayoutParams.WRAP_CONTENT
    }
}
