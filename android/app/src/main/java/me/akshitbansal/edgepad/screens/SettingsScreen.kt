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
import me.akshitbansal.edgepad.link.DeviceState
import me.akshitbansal.edgepad.link.Transport

/**
 * The app's settings: only what holds everywhere — the laptops and how they stay connected, the theme and
 * orientation, help and updates. A laptop's row here connects it, makes it active or disconnects it; the rest,
 * several at once and forgetting included, is on the Devices screen. Every feature's own settings sit behind that
 * feature's gear instead: the surface's, the keyboard's, the gamepad's and the macros'. Opened from the Devices
 * list's gear and from the last row of the surface's settings. Every change is saved as it is made. Sideways, the
 * sections sit in two columns.
 */
object SettingsScreen {
    /**
     * One laptop's row: its name, [state], the transport it is on or was last on, and the two things a row does.
     * [onTap] makes it active or connects it, as [state] says; [onDisconnect] ends it.
     */
    class Device(
        val name: String,
        val state: DeviceState,
        val via: Transport?,
        val onTap: () -> Unit,
        val onDisconnect: () -> Unit,
    )

    /** What the laptops card shows: every laptop the phone knows, and whether the active one offers the clipboard. */
    class Connections(
        val devices: List<Device>,
        /** The active laptop named the clipboard, so the switch for sharing it is shown. */
        val clipboard: Boolean = false,
    )

    /** Where the rows lead. [showAll] lifts the Devices list's filter to computers, as its own button does. */
    class Routes(
        val devices: () -> Unit,
        val showAll: (Boolean) -> Unit,
        val guide: () -> Unit,
        val documentation: () -> Unit,
        val update: () -> Unit,
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
        connections: Connections,
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
                    section(ui.string(R.string.paired_laptops))
                    card {
                        if (connections.devices.isEmpty()) {
                            add(ui.laptop(ui.string(R.string.no_laptop), null, false, null))
                        }
                        for (device in connections.devices) {
                            add(laptop(ui, device))
                            hairline()
                        }
                        // Connecting, disconnecting and forgetting, and every laptop at once, live on that screen.
                        add(ui.link(ui.string(R.string.pairing_title), null, routes.devices))
                    }

                    card(Space.XL) {
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
                        add(ui.toggle(ui.string(R.string.show_laptops_only), !showingAll) { routes.showAll(!it) })
                    }
                    footnote(ui.string(R.string.stay_connected_caption))

                    if (connections.clipboard) {
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
     * One laptop: its name, its state and transport under it, and Disconnect while anything is going on with it.
     * Forgetting is not here: it is on the Devices screen, which the row below the laptops opens.
     */
    private fun laptop(
        ui: Ui,
        device: Device,
    ): View {
        val on = device.state == DeviceState.ACTIVE || device.state == DeviceState.CONNECTED
        val state =
            ui.string(
                when (device.state) {
                    DeviceState.ACTIVE -> R.string.device_active
                    DeviceState.CONNECTED -> R.string.device_state_connected
                    DeviceState.CONNECTING -> R.string.device_connecting
                    DeviceState.ASKING -> R.string.device_asking
                    DeviceState.NOT_CONNECTED -> R.string.device_not_connected
                },
            )
        // A prompt or an attempt under way says all there is to say; the transport is for a laptop at rest or up.
        val settled = device.state != DeviceState.CONNECTING && device.state != DeviceState.ASKING
        val via = device.via.takeIf { settled }
        val line = if (via != null) ui.string(R.string.device_state_via, state, ui.string(via.label)) else state
        val sub = ui.secondary(line, Type.SMALL, if (on) ui.palette.accent else ui.palette.dim)
        val disconnect =
            if (device.state == DeviceState.NOT_CONNECTED) {
                null
            } else {
                ui.chip(ui.string(R.string.device_disconnect), device.onDisconnect)
            }
        return ui.laptop(device.name, sub, on, disconnect).apply { ui.tappable(this, device.onTap) }
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
