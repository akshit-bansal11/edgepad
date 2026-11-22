package me.akshitbansal.edgepad.screens

import android.app.UiModeManager
import android.content.res.Configuration
import android.content.res.Resources
import android.net.Uri
import android.view.View
import android.view.ViewGroup.LayoutParams.MATCH_PARENT
import android.view.ViewGroup.LayoutParams.WRAP_CONTENT
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.EndActionToken

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
        val uses: () -> Unit,
        val sideways: (Boolean) -> Unit,
        val clipboard: () -> Unit,
        val stayConnected: () -> Unit,
        val back: () -> Unit,
        /** The notifications switch changed: on asks for the Android 13 permission, and NOTIFY is sent either way. */
        val notify: (Boolean) -> Unit = {},
        /** The Developer mode switch moved: DEVELOPER is sent to the laptop, whose FEATURES then confirm it. */
        val developer: (Boolean) -> Unit = {},
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
     * The laptop's "when this ends" row, moved here from the Controls popup. [offered] is whether the laptop names
     * END_ACTION; [armed] and [hasTimeline] are read afresh on every redraw; [send] sends END_SLEEP, END_SHUTDOWN or
     * END_CANCEL. [onRedraw] is handed the function that redraws the row, for the activity to call when the laptop's
     * armed state changes, since rebuilding the page would throw the scroll position back to the top.
     */
    class End(
        val offered: Boolean = false,
        val armed: () -> EndActionToken = { EndActionToken.NONE },
        val hasTimeline: () -> Boolean = { true },
        val send: (ActionId) -> Unit = {},
        val onRedraw: (redraw: () -> Unit) -> Unit = {},
    )

    /**
     * [backLabel] names the screen the back link returns to: Devices, the surface or the surface's settings.
     * [developer] is developer mode as the laptop last reported it; [developerEditable] is false with no laptop
     * connected, when the switch is dimmed, since only a laptop can change it.
     */
    fun build(
        ui: Ui,
        settings: Settings,
        version: String,
        backLabel: String,
        update: Update,
        routes: Routes,
        developer: Boolean = false,
        developerEditable: Boolean = false,
        end: End = End(),
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

                    card(Space.XL) {
                        add(
                            ui.toggle(ui.string(R.string.notify_setting), settings.notifyForward) {
                                settings.notifyForward = it
                                routes.notify(it)
                            },
                        )
                    }
                    footnote(ui.string(R.string.notify_setting_hint))

                    card(Space.XL) {
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

                    // Issue #56. The surface reads the setting again when it takes the focus back.
                    card(Space.XL) {
                        add(
                            ui.toggle(ui.string(R.string.controls_blackout), settings.blackout) {
                                settings.blackout = it
                            },
                        )
                    }
                    if (end.offered) endRow(ui, end)

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
                        add(ui.link(ui.string(R.string.uses_title), null, routes.uses))
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

    /**
     * AU4, moved from the Controls popup: the choices to sleep or shut the laptop down when what is playing ends, or,
     * while the laptop has one armed, the line saying so and Cancel. The choices are plain text buttons, so nothing
     * here is told by colour alone. Without an end time they are dimmed and the note says why.
     */
    private fun Column.endRow(
        ui: Ui,
        end: End,
    ) {
        section(ui.string(R.string.end_action_title))
        val box = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        card { add(box) }
        val note = footnote("")
        val gap = ui.dp(Space.S)
        val fill = {
            box.removeAllViews()
            note.visibility = View.GONE
            val armed = end.armed()
            if (armed != EndActionToken.NONE) {
                val line = if (armed == EndActionToken.SLEEP) R.string.end_armed_sleep else R.string.end_armed_shutdown
                box.addView(ui.secondary(ui.string(line), Type.BODY, ui.palette.ink))
                val cancel = ui.button(ui.string(R.string.end_cancel), Ui.Style.QUIET) { end.send(ActionId.END_CANCEL) }
                box.addView(cancel)
            } else {
                val timeline = end.hasTimeline()
                val sleep =
                    ui.button(ui.string(R.string.end_sleep), Ui.Style.OUTLINED) { end.send(ActionId.END_SLEEP) }
                val shut =
                    ui.button(ui.string(R.string.end_shutdown), Ui.Style.OUTLINED) { end.send(ActionId.END_SHUTDOWN) }
                for (button in listOf(sleep, shut)) {
                    ui.enable(button, timeline)
                    val params = LinearLayout.LayoutParams(MATCH_PARENT, WRAP_CONTENT)
                    box.addView(button, params.apply { topMargin = gap })
                }
                if (!timeline) {
                    note.text = ui.string(R.string.end_no_timeline)
                    note.visibility = View.VISIBLE
                }
            }
        }
        fill()
        end.onRedraw(fill)
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
