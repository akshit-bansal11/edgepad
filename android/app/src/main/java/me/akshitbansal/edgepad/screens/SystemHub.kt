package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.PhoneStats

/**
 * System: the Laptop, Phone and Network pages of one [Pager] with its [PageDots], under one nav bar each that names
 * the laptop being looked at. The activity keeps the pages ([laptop], [network]) to refresh them as frames arrive and
 * is told the page that shows through [onPage], since the laptop sends only the data of the page that is open.
 *
 * The phone's own numbers are read only while the Phone or Network page shows and the app is in front: [setActive]
 * says whether it is, and every callback and timer [PhoneStats] holds is closed otherwise.
 */
class SystemHub(
    private val ui: Ui,
    state: () -> LaptopState,
    private val laptopName: () -> String,
    private val onSwitch: () -> Unit,
    rtt: () -> Int,
    onSpeed: (Boolean) -> Unit,
    onPage: (Int) -> Unit,
    private val onBack: () -> Unit,
) {
    private fun bar(): View = ui.hubBar(ui.string(R.string.sys_title), laptopName(), onSwitch, onBack)

    val laptop = SystemScreen(ui, state, bar())
    val phone = PhoneScreen(ui, bar())
    val network = NetworkScreen(ui, bar(), state, rtt, onSpeed)

    private val stats =
        PhoneStats(ui.context) {
            phone.show(it)
            network.phoneChanged(it)
        }
    private var page = 0
    private var active = true

    val view: View =
        ui.hubView(listOf(laptop.view, phone.view, network.view)) { at ->
            page = at
            sync()
            onPage(at)
        }

    /** Whether the app is in front. Out of it the phone's numbers are not read. */
    fun setActive(on: Boolean) {
        active = on
        sync()
    }

    private fun sync() {
        if (active && page != LAPTOP_PAGE) stats.observe() else stats.stop()
    }

    companion object {
        const val LAPTOP_PAGE = 0
        const val PHONE_PAGE = 1
        const val NETWORK_PAGE = 2
    }
}
