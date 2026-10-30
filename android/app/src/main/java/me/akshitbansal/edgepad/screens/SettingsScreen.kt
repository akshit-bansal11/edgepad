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
import me.akshitbansal.edgepad.Type

/**
 * The app's settings: only what holds everywhere — the remembered laptop and the paired devices, the theme
 * and orientation, help and updates. Every feature's own settings sit behind that feature's gear instead:
 * the surface's, the keyboard's, the gamepad's and the macros'. Opened from the Devices list's gear and from
 * the last row of the surface's settings. Every change is saved as it is made. Sideways, the sections sit in
 * two columns.
 */
object SettingsScreen {
    /** What the laptop card shows: the remembered laptop, if any, its state, and whether it is connected now. */
    class Connection(
        val name: String?,
        val detail: String,
        val connected: Boolean,
    )

    /** Where the rows lead. [showAll] lifts the Devices list's filter to computers, as its own button does. */
    class Routes(
        val forget: () -> Unit,
        val showAll: (Boolean) -> Unit,
        val guide: () -> Unit,
        val documentation: () -> Unit,
        val update: () -> Unit,
        val sideways: (Boolean) -> Unit,
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
        connection: Connection,
        showingAll: Boolean,
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
                    section(ui.string(R.string.pairing_title))
                    card {
                        add(laptop(ui, connection, routes.forget))
                        hairline()
                        add(
                            ui.toggle(ui.string(R.string.reconnect_automatically), settings.reconnect) {
                                settings.reconnect = it
                            },
                        )
                        hairline()
                        add(ui.toggle(ui.string(R.string.show_laptops_only), !showingAll) { routes.showAll(!it) })
                    }

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
                        hairline()
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
                    }
                    footnote(version).apply { setPadding(paddingLeft, ui.dp(Space.L), paddingRight, 0) }
                },
            )
        }

    /**
     * The remembered laptop, its state under its name, and a way to forget it. Forgetting asks first: it
     * drops the connection there and then, and the laptop is not reconnected to until it is picked again.
     */
    private fun laptop(
        ui: Ui,
        connection: Connection,
        onForget: () -> Unit,
    ): View {
        val on = connection.connected
        val detail =
            connection.detail.takeIf { it.isNotEmpty() }?.let {
                ui.secondary(it, Type.SMALL, if (on) ui.palette.accent else ui.palette.dim)
            }
        val forget =
            connection.name?.let { name ->
                lateinit var chip: TextView
                chip =
                    ui.chip(ui.string(R.string.forget), danger = true) {
                        ui.confirm(
                            chip,
                            ui.string(R.string.forget_laptop_title, name),
                            ui.string(R.string.forget_laptop_body),
                            ui.string(R.string.forget),
                            onForget,
                        )
                    }
                chip
            }
        return ui.laptop(connection.name ?: ui.string(R.string.no_laptop), detail, on, forget, large = true)
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
