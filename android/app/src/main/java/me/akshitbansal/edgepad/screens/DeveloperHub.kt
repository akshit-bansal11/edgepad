package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.link.LaptopState

/**
 * Developer: the AI and Git pages of one [Pager] with its [PageDots], under one nav bar each that names the laptop
 * being looked at. The activity keeps the pages ([ai], [github]) to refresh them as frames arrive and is told the
 * page that shows through [onPage], since the laptop sends only the data of the page that is open.
 */
class DeveloperHub(
    ui: Ui,
    state: () -> LaptopState,
    laptopName: () -> String,
    onSwitch: () -> Unit,
    onPage: (Int) -> Unit,
    onBack: () -> Unit,
) {
    private val title = ui.string(R.string.dev_title)

    val ai = AiScreen(ui, state, ui.hubBar(title, laptopName(), onSwitch, onBack))
    val github = GitHubScreen(ui, state, ui.hubBar(title, laptopName(), onSwitch, onBack))
    val view: View = ui.hubView(listOf(ai.view, github.view), onPage)

    companion object {
        const val GIT_PAGE = 1
    }
}
