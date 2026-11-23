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
import android.view.ViewGroup.LayoutParams.MATCH_PARENT
import android.view.ViewGroup.LayoutParams.WRAP_CONTENT
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.LaserSizes
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.EndActionToken
import me.akshitbansal.edgepad.surface.Strokes

/**
 * The app's settings, one page in sections: Surface (the trackpad, dials, air mouse, widgets, media layout, look,
 * profiles and blackout), then the Keyboard, Gamepad and Macros, which open their own pages, then the
 * Presenter's laser, Capture and Annotate (including the pen), Clipboard and files, Developer, Devices and
 * connection, the app's own appearance, Language, Help, and Updates and About. A setting here and the same setting
 * on a feature's own screen or sheet edit the same stored value. The laptops themselves are on the Devices screen,
 * which the first row of Devices and connection opens. Opened from the Devices list's gear and from the last row of
 * the surface's settings. Every change is saved as it is made. Sideways, the sections sit in two columns.
 */
object SettingsScreen {
    private const val OPAQUE = 0xFF000000.toInt()
    private const val RGB_MASK = 0xFFFFFF

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
        /** The surface's own pages, the same rows its gear opens. */
        val surface: SurfaceSettingsScreen.Rows,
        /** The notifications switch changed: on asks for the Android 13 permission, and NOTIFY is sent either way. */
        val notify: (Boolean) -> Unit = {},
        /** The Developer mode switch moved: DEVELOPER is sent to the laptop, whose FEATURES then confirm it. */
        val developer: (Boolean) -> Unit = {},
        val keyboard: () -> Unit = {},
        val gamepad: () -> Unit = {},
        val macros: () -> Unit = {},
        /** "To phone" changed: the capture inbox is told and the laptop hears it. */
        val capture: (Boolean) -> Unit = {},
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
                    section(ui.string(R.string.settings_surface))
                    SurfaceSettingsScreen.rows(this, settings, routes.surface)

                    section(ui.string(R.string.controls_keyboard))
                    card { add(ui.link(ui.string(R.string.keyboard_settings_title), null, routes.keyboard)) }

                    section(ui.string(R.string.controls_gamepad))
                    card { add(ui.link(ui.string(R.string.gamepad_settings_title), null, routes.gamepad)) }

                    section(ui.string(R.string.controls_macros))
                    card { add(ui.link(ui.string(R.string.macros_settings), null, routes.macros)) }

                    presenterRows(settings)
                    captureRows(settings, routes)

                    section(ui.string(R.string.settings_clipboard_files))
                    card {
                        add(
                            ui.toggle(ui.string(R.string.clipboard_share), settings.clipboardSync) {
                                settings.clipboardSync = it
                                routes.clipboard()
                            },
                        )
                    }
                    footnote(ui.string(R.string.clipboard_share_caption))

                    section(ui.string(R.string.settings_developer))
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
                {
                    section(ui.string(R.string.settings_devices))
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

                    card(Space.XL) {
                        add(
                            ui.toggle(ui.string(R.string.notify_setting), settings.notifyForward) {
                                settings.notifyForward = it
                                routes.notify(it)
                            },
                        )
                    }
                    footnote(ui.string(R.string.notify_setting_hint))
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

                    languageRows()

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

    /** The Presenter's laser, as its own sheet sets it: size and trail, the same two stored values. */
    private fun Column.presenterRows(settings: Settings) {
        section(ui.string(R.string.controls_presenter))
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

    /**
     * Capture to the phone, and the pen the Annotate toolbar draws with: its colour, width and opacity, the same three
     * stored values the toolbar's own controls edit. Each is read when Annotate opens, so a change here shows there.
     */
    private fun Column.captureRows(
        settings: Settings,
        routes: Routes,
    ) {
        section(ui.string(R.string.settings_capture))
        card {
            add(
                ui.toggle(ui.string(R.string.capture_to_phone), settings.captureToPhone) {
                    settings.captureToPhone = it
                    routes.capture(it)
                },
            )
        }
        card(Space.XL) {
            add(
                ColorPicker.build(
                    ui,
                    ui.string(R.string.settings_pen_colour),
                    OPAQUE or settings.penRgb,
                    editable = true,
                ) { colour -> settings.penRgb = colour and RGB_MASK },
            )
            hairline()
            add(
                ui.slider(
                    ui.string(R.string.annotate_width),
                    Strokes.WIDTH_STEPS,
                    Strokes.widthStep(settings.penWidth),
                    { step -> ui.string(R.string.annotate_width_value, step + 1) },
                ) { step -> settings.penWidth = Strokes.widthAt(step) },
            )
            hairline()
            add(
                ui.slider(
                    ui.string(R.string.annotate_opacity),
                    Strokes.OPACITY_STEPS,
                    Strokes.opacityStep(settings.penOpacity),
                    { step -> ui.string(R.string.percent_value, Strokes.opacityAt(step)) },
                ) { step -> settings.penOpacity = Strokes.opacityAt(step) },
            )
        }
    }

    /**
     * The app's language: one row naming the current one in its own words, opening a list of every language in its own
     * name, with System default first. From Android 13 the system keeps the choice per app and rebuilds the activity;
     * the link survives that. Before it the system has no per-app language, so the row is replaced by a line saying the
     * app follows the phone.
     */
    private fun Column.languageRows() {
        section(ui.string(R.string.settings_language))
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
            footnote(ui.string(R.string.language_follows_phone))
            return
        }
        val manager = ui.context.getSystemService(LocaleManager::class.java)
        val current = manager.applicationLocales
        val chosen = if (current.isEmpty) null else AppLanguages.match(current[0].toLanguageTag())
        val system = ui.string(R.string.language_system)
        val names = (listOf(system) + AppLanguages.all.map { it.name }).toTypedArray()
        val checked = if (chosen == null) 0 else AppLanguages.all.indexOf(chosen) + 1
        card {
            lateinit var row: View
            row =
                ui.link(ui.string(R.string.settings_language), chosen?.name ?: system, {
                    AlertDialog
                        .Builder(ui.context)
                        .setTitle(ui.string(R.string.settings_language))
                        .setSingleChoiceItems(names, checked) { dialog, i ->
                            manager.applicationLocales =
                                if (i == 0) {
                                    LocaleList.getEmptyLocaleList()
                                } else {
                                    LocaleList.forLanguageTags(AppLanguages.all[i - 1].tag)
                                }
                            dialog.dismiss()
                        }.showOver(row)
                })
            add(row)
        }
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
