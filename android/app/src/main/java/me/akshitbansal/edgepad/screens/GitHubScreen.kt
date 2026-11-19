package me.akshitbansal.edgepad.screens

import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.DashWire
import me.akshitbansal.edgepad.link.GithubRow
import me.akshitbansal.edgepad.link.GithubSection
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.protocol.CiToken
import me.akshitbansal.edgepad.protocol.Feature

/**
 * The pull requests waiting on the laptop's owner and the ones they opened, with the checks on each, and how many
 * notifications are unread. Text only: nothing here opens a link or takes a tap, so a title is shown and never acted
 * on, and every string the laptop sent goes in with `setText`.
 *
 * Upright the cards follow one another; sideways the notifications are on top and the two lists side by side. Nothing
 * polls: the laptop sends the list while the phone says this screen is open, and the activity calls [refresh] when a
 * new one arrived.
 *
 * @param state The laptop this screen shows, read afresh on each [refresh].
 */
class GitHubScreen(
    private val ui: Ui,
    private val state: () -> LaptopState,
    onBack: () -> Unit,
) {
    private val holder = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }

    val view: View =
        ui.page(ui.bar(ui.string(R.string.gh_title), onBack, backLabel = ui.string(R.string.back_to_surface))) {
            add(holder)
        }

    init {
        refresh()
    }

    /** Draws the list the laptop sent last. */
    fun refresh() {
        holder.removeAllViews()
        Column(ui, holder).render(state())
    }

    private fun Column.render(s: LaptopState) {
        if (Feature.GITHUB !in s.features) {
            card { note(ui.string(R.string.gh_signin)) }
            return
        }
        val count = s.github.firstOrNull { it.section == GithubSection.NOTIFICATIONS }?.number ?: 0
        card { add(ui.field(ui.string(R.string.gh_notifs, count))) }
        val review = s.github.filter { it.section == GithubSection.REVIEW }
        val mine = s.github.filter { it.section == GithubSection.MINE }
        columns(
            first = { pulls(R.string.gh_review, review) },
            second = { pulls(R.string.gh_mine, mine) },
        )
    }

    private fun Column.pulls(
        title: Int,
        rows: List<GithubRow>,
    ) {
        section(ui.string(title))
        card {
            if (rows.isEmpty()) note(ui.string(R.string.gh_none))
            rows.forEachIndexed { index, row ->
                if (index > 0) hairline()
                add(pull(row))
            }
        }
    }

    private fun Column.note(text: String) {
        body(text).setPadding(0, ui.dp(Space.M), 0, ui.dp(Space.M))
    }

    /** A pull request: its title over `owner/repo #123`, and at the far side the state of its checks and Draft. */
    private fun pull(row: GithubRow): LinearLayout {
        val title =
            ui.text(DashWire.cleanText(row.title), Type.BODY, ui.palette.ink).apply {
                maxLines = TITLE_LINES
                ellipsize = TextUtils.TruncateAt.END
            }
        val where = ui.secondary("${row.repo} #${row.number}")
        val lines =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(0, ui.dp(Space.M), ui.dp(Space.M), ui.dp(Space.M))
                addView(title)
                addView(
                    where,
                    LinearLayout
                        .LayoutParams(
                            ViewGroup.LayoutParams.WRAP_CONTENT,
                            ViewGroup.LayoutParams.WRAP_CONTENT,
                        ).apply { topMargin = ui.dp(Space.SUB_GAP) },
                )
            }
        val badges =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                gravity = Gravity.END
                checks(row.ci)?.let { addView(it) }
                if (row.draft) addView(badge(ui.string(R.string.gh_draft), false), badgeParams())
            }
        return ui.row(lines, badges)
    }

    private fun checks(ci: CiToken): View? =
        when (ci) {
            CiToken.OK -> badge(ui.string(R.string.gh_ci_ok), false)
            CiToken.FAIL -> badge(ui.string(R.string.gh_ci_fail), true)
            CiToken.PENDING -> badge(ui.string(R.string.gh_ci_pending), false)
            CiToken.NONE -> null
        }

    private fun badgeParams() =
        LinearLayout
            .LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { topMargin = ui.dp(Space.XS) }

    /** A small label on a faint pill; danger text for failed checks, and no other colour. */
    private fun badge(
        text: String,
        danger: Boolean,
    ) = ui.text(text, Type.MICRO, if (danger) ui.palette.danger else ui.palette.ink, face = Type.bold).apply {
        background = ui.rounded(ui.palette.faint, Space.TILE_RADIUS)
        setPadding(ui.dp(Space.S), ui.dp(Space.XS), ui.dp(Space.S), ui.dp(Space.XS))
    }

    private companion object {
        const val TITLE_LINES = 2
    }
}
