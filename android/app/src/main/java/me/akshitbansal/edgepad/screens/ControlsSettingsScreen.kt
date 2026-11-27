package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space

/** What is set for the Controls popup and nowhere else: its tiles' size, whether they are named, how many a page. */
object ControlsSettingsScreen {
    private val sizeNames =
        listOf(R.string.controls_size_small, R.string.controls_size_medium, R.string.controls_size_large)

    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View =
        ui.page(ui.bar("", onBack, backLabel = ui.string(R.string.controls_title))) {
            val sizes = ControlsIconSize.entries
            val min = ControlsPlan.PER_PAGE.first
            largeTitle(ui.string(R.string.controls_settings_title))
            card {
                add(
                    ui.segmentedField(
                        ui.string(R.string.controls_icon_size),
                        ui.segmented(sizeNames.map { ui.string(it) }, sizes.indexOf(settings.controlsIconSize)) { i ->
                            settings.controlsIconSize = sizes[i]
                        },
                    ),
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
                        { step -> ui.string(R.string.controls_per_page_value, step + min) },
                    ) { step -> settings.controlsPerPage = step + min },
                )
            }
        }
}
