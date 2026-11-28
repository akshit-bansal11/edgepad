package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space

/** What is set for the Controls popup and nowhere else: how much of a tile its picture fills, names, tiles a page. */
object ControlsSettingsScreen {
    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View =
        ui.page(ui.bar("", onBack, backLabel = ui.string(R.string.controls_title))) {
            val min = ControlsPlan.PER_PAGE.first
            val first = ControlsPlan.ICON_PERCENT.first
            val step = ControlsPlan.ICON_PERCENT_STEP
            largeTitle(ui.string(R.string.controls_settings_title))
            card {
                add(
                    ui.slider(
                        ui.string(R.string.controls_icon_size),
                        (ControlsPlan.ICON_PERCENT.last - first) / step,
                        (settings.controlsIconPercent - first) / step,
                        { at -> ui.string(R.string.controls_icon_size_value, first + at * step) },
                    ) { at -> settings.controlsIconPercent = first + at * step },
                )
                hairline()
                add(
                    ui.toggle(ui.string(R.string.controls_show_labels), settings.controlsLabels) {
                        settings.controlsLabels = it
                    },
                )
            }
            card(Space.XL) {
                add(
                    ui.slider(
                        ui.string(R.string.controls_per_page),
                        ControlsPlan.PER_PAGE.last - min,
                        settings.controlsPerPage - min,
                        { at -> ui.string(R.string.controls_per_page_value, at + min) },
                    ) { at -> settings.controlsPerPage = at + min },
                )
            }
        }
}
