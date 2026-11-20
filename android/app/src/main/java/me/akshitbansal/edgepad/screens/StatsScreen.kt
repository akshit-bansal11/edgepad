package me.akshitbansal.edgepad.screens

import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.link.LaptopState

/**
 * Simple stats: the System, AI and Git screens as pages of one [Pager] with its [PageDots]. The AI and Git pages exist
 * only while Developer mode is on ([developer]); without it the System page stands alone, with no dots. Each page is
 * the screen as it was, with its own bar and title, so the page that is showing names itself and Back leaves them all.
 *
 * The activity keeps the three screens ([system], [ai], [github]) to refresh them as frames arrive, and is told the
 * page that is showing through [onPage], since the laptop sends only the data of the page that is open.
 */
class StatsScreen(
    private val ui: Ui,
    state: () -> LaptopState,
    developer: Boolean,
    private val onPage: (Int) -> Unit,
    onBack: () -> Unit,
) {
    val system = SystemScreen(ui, state, onBack)
    val ai: AiScreen? = if (developer) AiScreen(ui, state, onBack) else null
    val github: GitHubScreen? = if (developer) GitHubScreen(ui, state, onBack) else null

    val view: View =
        if (!developer) {
            system.view
        } else {
            val pages = listOfNotNull(system.view, ai?.view, github?.view)
            val dots = PageDots(ui, pages.size)
            val pager =
                Pager(ui.context) { at ->
                    dots.select(at)
                    onPage(at)
                }
            pages.forEach { pager.row.addView(it, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT)) }
            // Each page is as wide as the pager, which is not known until it is laid out.
            pager.addOnLayoutChangeListener { _, left, _, right, _, _, _, _, _ ->
                val wide = right - left
                for (i in 0 until pager.row.childCount) {
                    val page = pager.row.getChildAt(i)
                    if (page.layoutParams.width != wide) {
                        page.layoutParams = page.layoutParams.apply { width = wide }
                    }
                }
            }
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(pager, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
                addView(dots, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(Space.XL)))
            }
        }
}
