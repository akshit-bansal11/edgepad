package me.akshitbansal.edgepad.screens

import android.app.UiModeManager
import android.content.res.Configuration
import android.content.res.Resources
import android.net.Uri
import android.view.View
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space

/**
 * The app's settings: only what holds everywhere. How the laptops stay connected, what is shared with them, the theme
 * and orientation, help, and the version with its updates. The laptops themselves are on the Devices screen, which the
 * first row opens: connecting, disconnecting and forgetting live there and nowhere else. Every feature's own settings
 * sit behind that feature's gear instead: the surface's, the keyboard's, the gamepad's and the macros'. Opened from the
 * Devices list's gear and from the last row of the surface's settings. Every change is saved as it is made. Sideways,
 * the sections sit in two columns.
 */
object SettingsScreen {
    /** Where the rows lead. */
    class Routes(
        val devices: () -> Unit,
        val guide: () -> Unit,
        val documentation: () -> Unit,
        val update: () -> Unit,
        val licences: () -> Unit,
        val sideways: (Boolean) -> Unit,
        val clipboard: () -> Unit,
        val stayConnected: () -> Unit,
        val back: () -> Unit,
    )

    /**
     * The Updates row: what its summary says as the page opens, and [onSummary], which is handed the
     * summary's view. A check finishes while the page is open, and the activity rewrites that one view
     * rather than rebuild the page, which would throw the scroll position back to the top.
     */
    class Update(
        val summary: String,
        val onSummary: (TextView) -> Unit,
    )

    /** [backLabel] names the screen the back link returns to: Devices, the surface or the surface's settings. */
    fun build(
        ui: Ui,
        settings: Settings,
        version: String,
        backLabel: String,
        update: Update,
        routes: Routes,
    ): View =
        // The nav bar draws no title: the large title under it is the screen's name, and says so to a screen reader.
        ui.page(ui.bar("", routes.back, backLabel = backLabel)) {
            largeTitle(ui.string(R.string.settings_system))
            columns(
                {
                    section(ui.string(R.string.paired_laptops))
                    card { add(ui.link(ui.string(R.string.pairing_title), null, routes.devices)) }

                    card(Space.XL) {
                        add(
                            ui.toggle(ui.string(R.string.reconnect_automatically), settings.reconnect) {
                                settings.reconnect = it
                            },
                        )
                    }
                    footnote(ui.string(R.string.reconnect_caption))
                    card(Space.XL) {
                        add(
                            ui.toggle(ui.string(R.string.stay_connected), settings.stayConnected) {
                                settings.stayConnected = it
                                routes.stayConnected()
                            },
                        )
                    }
                    footnote(ui.string(R.string.stay_connected_caption))

                    section(ui.string(R.string.settings_clipboard))
                    card {
                        add(
                            ui.toggle(ui.string(R.string.clipboard_share), settings.clipboardSync) {
                                settings.clipboardSync = it
                                routes.clipboard()
                            },
                        )
                    }
                    footnote(ui.string(R.string.clipboard_share_caption))

                    section(ui.string(R.string.settings_appearance))
                    card {
                        val themes =
                            listOf(
                                ui.string(R.string.theme_system),
                                ui.string(R.string.theme_dark),
                                ui.string(R.string.theme_light),
                            )
                        add(
                            ui.field(
                                ui.string(R.string.theme),
                                ui.segmented(themes, theme(ui, settings).ordinal) { i ->
                                    setTheme(ui, settings, Settings.Theme.entries[i])
                                },
                            ),
                        )
                        hairline()
                        val held =
                            listOf(ui.string(R.string.orientation_upright), ui.string(R.string.orientation_sideways))
                        add(
                            ui.field(
                                ui.string(R.string.orientation),
                                ui.segmented(held, if (settings.landscape) 1 else 0) { i -> routes.sideways(i == 1) },
                            ),
                        )
                    }
                },
                {
                    section(ui.string(R.string.settings_help))
                    card {
                        add(ui.link(ui.string(R.string.guide_title), null, routes.guide))
                        hairline()
                        add(
                            ui.link(
                                ui.string(R.string.documentation_title),
                                // The site's own address, from the one string that holds it.
                                Uri.parse(ui.string(R.string.documentation_url)).host,
                                routes.documentation,
                            ),
                        )
                    }

                    section(ui.string(R.string.settings_about))
                    card {
                        add(
                            ui.link(
                                ui.string(R.string.updates_title),
                                update.summary,
                                routes.update,
                                update.onSummary,
                            ),
                        )
                        hairline()
                        add(
                            ui.toggle(ui.string(R.string.updates_auto), settings.autoUpdateCheck) {
                                settings.autoUpdateCheck = it
                            },
                        )
                        hairline()
                        add(ui.link(ui.string(R.string.licences_title), null, routes.licences))
                    }
                    footnote(version)
                    footnote(ui.string(R.string.about_sends))
                },
            )
        }

    /**
     * The Theme choice as last made. Before 3.2 only Dark and Light existed and neither was written down, so
     * for a choice made then it is worked out: a theme that differs from the phone's was chosen, and one that
     * matches it is shown as System, which from then on is also what it does.
     */
    private fun theme(
        ui: Ui,
        settings: Settings,
    ): Settings.Theme {
        settings.theme?.let { return it }
        val phoneDark =
            (Resources.getSystem().configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) ==
                Configuration.UI_MODE_NIGHT_YES
        return when {
            ui.palette.dark == phoneDark -> Settings.Theme.SYSTEM
            ui.palette.dark -> Settings.Theme.DARK
            else -> Settings.Theme.LIGHT
        }
    }

    private fun setTheme(
        ui: Ui,
        settings: Settings,
        theme: Settings.Theme,
    ) {
        settings.theme = theme
        // The system keeps the choice for this app and rebuilds the activity when it changes what is drawn;
        // the link survives the rebuild. MODE_NIGHT_AUTO is how an app hands the choice back: the system then
        // sets no night mode of its own for the app, and the phone's applies.
        ui.context.getSystemService(UiModeManager::class.java)?.setApplicationNightMode(
            when (theme) {
                Settings.Theme.SYSTEM -> UiModeManager.MODE_NIGHT_AUTO
                Settings.Theme.DARK -> UiModeManager.MODE_NIGHT_YES
                Settings.Theme.LIGHT -> UiModeManager.MODE_NIGHT_NO
            },
        )
    }
}
