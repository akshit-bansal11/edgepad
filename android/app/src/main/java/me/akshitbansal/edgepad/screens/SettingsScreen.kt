package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.app.LocaleManager
import android.app.UiModeManager
import android.content.res.Configuration
import android.content.res.Resources
import android.net.Uri
import android.os.Build
import android.os.LocaleList
import android.view.View
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings

/**
 * The app's settings, one page in five groups: Connection (the laptops and how Edgepad stays connected to them),
 * Screens (a row for each page that has settings of its own: the surface, the controls, the keyboard, the gamepad, the
 * macros, the presenter and capture), Appearance, Help and about, and Advanced. A setting here and the same setting on
 * a feature's own screen or sheet edit the same stored value. Opened from the Devices list's gear and from the last row
 * of the surface's settings. Every change is saved as it is made. Sideways, the groups sit in two columns.
 */
object SettingsScreen {
    /** Where the rows lead. */
    class Routes(
        val devices: () -> Unit,
        val guide: () -> Unit,
        val documentation: () -> Unit,
        val update: () -> Unit,
        val licences: () -> Unit,
        val uses: () -> Unit,
        val sideways: (Boolean) -> Unit,
        val clipboard: () -> Unit,
        val stayConnected: () -> Unit,
        val back: () -> Unit,
        /** The notifications switch changed: on asks for the Android 13 permission, and NOTIFY is sent either way. */
        val notify: (Boolean) -> Unit = {},
        /** The Developer mode switch moved: DEVELOPER is sent to the laptop, whose FEATURES then confirm it. */
        val developer: (Boolean) -> Unit = {},
        /** The pages under Screens, each opened from its row. */
        val surface: () -> Unit = {},
        val controls: () -> Unit = {},
        val keyboard: () -> Unit = {},
        val gamepad: () -> Unit = {},
        val macros: () -> Unit = {},
        val presenter: () -> Unit = {},
        val capture: () -> Unit = {},
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

    /**
     * [backLabel] names the screen the back link returns to: Devices, the surface or the surface's settings.
     * [laptops] is the Laptops row's summary. [developer] is developer mode as the laptop last reported it;
     * [developerEditable] is false with no laptop connected, when the switch is dimmed, since only a laptop can
     * change it.
     */
    fun build(
        ui: Ui,
        settings: Settings,
        version: String,
        backLabel: String,
        update: Update,
        routes: Routes,
        laptops: String? = null,
        developer: Boolean = false,
        developerEditable: Boolean = false,
    ): View =
        // The nav bar draws no title: the large title under it is the screen's name, and says so to a screen reader.
        ui.page(ui.bar("", routes.back, backLabel = backLabel)) {
            largeTitle(ui.string(R.string.settings_system))
            columns(
                {
                    section(ui.string(R.string.settings_connection))
                    card {
                        add(ui.link(ui.string(R.string.paired_laptops), laptops, routes.devices))
                        hairline()
                        add(
                            ui.toggle(ui.string(R.string.reconnect_automatically), settings.reconnect) {
                                settings.reconnect = it
                            },
                        )
                        hairline()
                        add(
                            ui.toggle(ui.string(R.string.stay_connected), settings.stayConnected) {
                                settings.stayConnected = it
                                routes.stayConnected()
                            },
                        )
                        hairline()
                        add(
                            ui.toggle(ui.string(R.string.clipboard_share), settings.clipboardSync) {
                                settings.clipboardSync = it
                                routes.clipboard()
                            },
                        )
                        hairline()
                        add(
                            ui.toggle(ui.string(R.string.notify_setting), settings.notifyForward) {
                                settings.notifyForward = it
                                routes.notify(it)
                            },
                        )
                    }
                    footnote(ui.string(R.string.reconnect_caption))
                    footnote(ui.string(R.string.stay_connected_caption))
                    footnote(ui.string(R.string.clipboard_share_caption))
                    footnote(ui.string(R.string.notify_setting_hint))

                    section(ui.string(R.string.settings_screens))
                    card {
                        add(ui.link(ui.string(R.string.settings_surface), null, routes.surface))
                        hairline()
                        add(ui.link(ui.string(R.string.controls_title), null, routes.controls))
                        hairline()
                        add(ui.link(ui.string(R.string.controls_keyboard), null, routes.keyboard))
                        hairline()
                        add(ui.link(ui.string(R.string.controls_gamepad), null, routes.gamepad))
                        hairline()
                        add(ui.link(ui.string(R.string.controls_macros), null, routes.macros))
                        hairline()
                        add(ui.link(ui.string(R.string.controls_presenter), null, routes.presenter))
                        hairline()
                        add(ui.link(ui.string(R.string.settings_capture), null, routes.capture))
                    }
                },
                {
                    section(ui.string(R.string.settings_appearance))
                    card {
                        val themes =
                            listOf(
                                ui.string(R.string.theme_system),
                                ui.string(R.string.theme_dark),
                                ui.string(R.string.theme_light),
                            )
                        add(
                            ui.segmentedField(
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
                            ui.segmentedField(
                                ui.string(R.string.orientation),
                                ui.segmented(held, if (settings.landscape) 1 else 0) { i -> routes.sideways(i == 1) },
                            ),
                        )
                        languageRow()
                    }
                    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
                        footnote(ui.string(R.string.language_follows_phone))
                    }

                    section(ui.string(R.string.settings_help_about))
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
                        hairline()
                        add(ui.link(ui.string(R.string.uses_title), null, routes.uses))
                        hairline()
                        add(ui.link(ui.string(R.string.licences_title), null, routes.licences))
                    }
                    footnote(version)
                    footnote(ui.string(R.string.about_sends))

                    section(ui.string(R.string.settings_advanced))
                    card {
                        val toggle =
                            ui.toggle(ui.string(R.string.developer_mode_title), developer) { routes.developer(it) }
                        ui.enable(toggle, developerEditable)
                        add(toggle)
                    }
                    footnote(
                        ui.string(
                            if (developerEditable) R.string.developer_mode_hint else R.string.developer_mode_offline,
                        ),
                    )
                },
            )
        }

    /**
     * The app's language: one row, in the Appearance card, naming the current one in its own words, opening a list of
     * every language in its own name, with System default first. From Android 13 the system keeps the choice per app
     * and rebuilds the activity; the link survives that. Before it the system has no per-app language, so there is no
     * row, and a line under the card says the app follows the phone.
     */
    private fun Column.languageRow() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return
        val manager = ui.context.getSystemService(LocaleManager::class.java)
        val current = manager.applicationLocales
        val chosen = if (current.isEmpty) null else AppLanguages.match(current[0].toLanguageTag())
        val system = ui.string(R.string.language_system)
        val names = (listOf(system) + AppLanguages.all.map { it.name }).toTypedArray()
        val checked = if (chosen == null) 0 else AppLanguages.all.indexOf(chosen) + 1
        hairline()
        lateinit var row: View
        row =
            ui.link(ui.string(R.string.settings_language), chosen?.name ?: system, {
                AlertDialog
                    .Builder(ui.context)
                    .setTitle(ui.string(R.string.settings_language))
                    .setSingleChoiceItems(names, checked) { dialog, i ->
                        // Closed first: setting the language rebuilds the activity under the dialog.
                        dialog.dismiss()
                        manager.applicationLocales =
                            if (i == 0) {
                                LocaleList.getEmptyLocaleList()
                            } else {
                                LocaleList.forLanguageTags(AppLanguages.all[i - 1].tag)
                            }
                    }.showOver(row)
            })
        add(row)
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
