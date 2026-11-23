package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.link.LaptopState

/**
 * Developer: the AI and Git pages of one [Pager] with its [PageDots], under one nav bar each whose chip names the
 * laptop being looked at and opens a dropdown of the connected laptops (the phone is not a developer device). The
 * activity keeps the pages ([ai], [git]) to refresh them as frames arrive and is told the page that shows through
 * [onPage], since the laptop sends only the data of the page that is open. [onGitOpen] is handed the `GitOpen` text
 * of a repository or item that was tapped.
 */
class DeveloperHub(
    private val ui: Ui,
    state: () -> LaptopState,
    private val laptopName: () -> String,
    private val devices: DeviceSource,
    onPage: (Int) -> Unit,
    onGitOpen: (String) -> Unit,
    private val onBack: () -> Unit,
) {
    private val title = ui.string(R.string.dev_title)

    private fun bar(): View = ui.hubBar(title, DeviceChip(ui, laptopName(), false, ::openMenu), onBack)

    val ai = AiScreen(ui, state, bar())
    val git = GitScreen(ui, state, bar(), onGitOpen)
    val view: View = ui.hubView(listOf(ai.view, git.view), onPage)

    private fun openMenu(anchor: View) {
        val rows = DeviceChoices.of(devices.laptops(), devices.selectedId(), null, false)
        ui.showDeviceMenu(anchor, rows) { pick -> devices.select(pick.id) }
    }
}
