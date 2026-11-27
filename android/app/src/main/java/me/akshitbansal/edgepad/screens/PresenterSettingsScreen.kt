package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.LaserSizes
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings

/** What is set for the Presenter's laser: size and trail, the same two stored values its own sheet edits. */
object PresenterSettingsScreen {
    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View =
        ui.page(ui.bar("", onBack, backLabel = ui.string(R.string.settings_title))) {
            largeTitle(ui.string(R.string.presenter_settings))
            section(ui.string(R.string.presenter_laser_title))
            card {
                val sizes =
                    listOf(
                        ui.string(R.string.presenter_size_small),
                        ui.string(R.string.presenter_size_medium),
                        ui.string(R.string.presenter_size_large),
                    )
                add(
                    ui.segmentedField(
                        ui.string(R.string.presenter_size_label),
                        ui.segmented(sizes, LaserSizes.ALL.indexOf(settings.laserSize)) { i ->
                            settings.laserSize = LaserSizes.ALL[i]
                        },
                    ),
                )
                hairline()
                add(
                    ui.toggle(ui.string(R.string.presenter_laser_trail), settings.laserTrail) {
                        settings.laserTrail = it
                    },
                )
            }
        }
}
