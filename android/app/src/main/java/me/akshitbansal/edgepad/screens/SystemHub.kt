package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.link.LaptopState
import me.akshitbansal.edgepad.link.PhoneStats

/**
 * Stats: the System and Network pages of one [Pager] with its [PageDots], under one nav bar each whose chip names the
 * device being looked at and opens a dropdown of the connected laptops and this phone. The activity keeps the pages
 * ([laptop], [network]) to refresh them as frames arrive and is told the page that shows through [onPage] and whether
 * the phone is the device through [onDevice], since the laptop sends only the data of the page that is open and
 * nothing while the phone is the device.
 *
 * The phone's own numbers are read only while the phone is the device and the app is in front: [setActive] says
 * whether it is, and every callback and timer [PhoneStats] holds is closed otherwise.
 */
class SystemHub(
    private val ui: Ui,
    state: () -> LaptopState,
    private val laptopName: () -> String,
    private val devices: DeviceSource,
    private val onDevice: (Boolean) -> Unit,
    rtt: () -> Int,
    onSpeed: (Boolean) -> Unit,
    onPage: (Int) -> Unit,
    private val onBack: () -> Unit,
) {
    private val phoneName = ui.string(R.string.battery_this_phone)
    private val chips = ArrayList<DeviceChip>()
    private var onPhone = false
    private var active = true

    private fun bar(): View {
        val chip = DeviceChip(ui, laptopName(), false, ::openMenu)
        chips.add(chip)
        return ui.hubBar(ui.string(R.string.sys_title), chip, onBack)
    }

    private val phone = PhoneScreen(ui)
    val laptop = SystemScreen(ui, state, bar(), phone)
    val network = NetworkScreen(ui, bar(), state, rtt, onSpeed)

    private val stats =
        PhoneStats(ui.context) {
            phone.show(it)
            network.phoneChanged(it)
        }

    val view: View = ui.hubView(listOf(laptop.view, network.view), onPage)

    private fun openMenu(anchor: View) {
        val rows = DeviceChoices.of(devices.laptops(), devices.selectedId(), phoneName, onPhone)
        ui.showDeviceMenu(anchor, rows) { pick ->
            show(pick.phone)
            if (!pick.phone) devices.select(pick.id)
        }
    }

    private fun show(phoneOn: Boolean) {
        if (phoneOn == onPhone) return
        onPhone = phoneOn
        for (chip in chips) chip.set(if (phoneOn) phoneName else laptopName(), phoneOn)
        laptop.showPhone(phoneOn)
        network.showPhone(phoneOn)
        sync()
        onDevice(phoneOn)
    }

    /** Whether the app is in front. Out of it the phone's numbers are not read. */
    fun setActive(on: Boolean) {
        active = on
        sync()
    }

    private fun sync() {
        if (active && onPhone) stats.observe() else stats.stop()
    }
}
