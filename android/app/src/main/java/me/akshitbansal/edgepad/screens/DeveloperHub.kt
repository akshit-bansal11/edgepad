package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.link.LaptopState

/**
 * Developer: the AI and Git pages of one [Pager] with its [PageDots], under one nav bar each that names the laptop
 * being looked at. The activity keeps the pages ([ai], [git]) to refresh them as frames arrive and is told the
 * page that shows through [onPage], since the laptop sends only the data of the page that is open. [onGitOpen] is
 * handed the `GitOpen` text of a repository or item that was tapped.
 */
class DeveloperHub(
    ui: Ui,
    state: () -> LaptopState,
    laptopName: () -> String,
    onSwitch: () -> Unit,
    onPage: (Int) -> Unit,
    onGitOpen: (String) -> Unit,
    onBack: () -> Unit,
) {
    private val title = ui.string(R.string.dev_title)

    val ai = AiScreen(ui, state, ui.hubBar(title, laptopName(), onSwitch, onBack))
    val git = GitScreen(ui, state, ui.hubBar(title, laptopName(), onSwitch, onBack), onGitOpen)
    val view: View = ui.hubView(listOf(ai.view, git.view), onPage)
}
