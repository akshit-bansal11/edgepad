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
import me.akshitbansal.edgepad.surface.Perimeter
import me.akshitbansal.edgepad.surface.Shapes
import kotlin.math.roundToInt

/**
 * The Settings hub: the remembered laptop, then grouped cards of settings — a row into each page with a
 * summary of what is set there, and the settings small enough to need no page of their own set right
 * here — the theme, and the version. Every change is saved as it is made. Sideways, the groups sit in two
 * columns.
 */
object SettingsScreen {
    /** What the laptop card shows: the remembered laptop, if any, its state, and whether it is connected now. */
    class Connection(
        val name: String?,
        val detail: String,
        val connected: Boolean,
    )

    /** Where the hub's rows lead. */
    class Routes(
        val forget: () -> Unit,
        val corners: () -> Unit,
        val gestures: () -> Unit,
        val shapes: () -> Unit,
        val dialFeel: () -> Unit,
        val gamepadLayout: () -> Unit,
        val mediaLayout: () -> Unit,
        val appearance: () -> Unit,
        val guide: () -> Unit,
        val documentation: () -> Unit,
        val sideways: (Boolean) -> Unit,
        val back: () -> Unit,
    )

    private const val NO_DIAL = "—"

    private val textRange = StepRange(Settings.MIN_KEY_TEXT_SCALE, Settings.MAX_KEY_TEXT_SCALE, 0.1f)

    /** [backLabel] names the screen the back link returns to: Devices, or the controls it was opened from. */
    fun build(
        ui: Ui,
        settings: Settings,
        connection: Connection,
        preset: String,
        version: String,
        backLabel: String,
        routes: Routes,
    ): View =
        // The nav bar draws no title: the large title under it is the screen's name, and says so to a screen reader.
        ui.page(ui.bar("", routes.back, backLabel = backLabel)) {
            largeTitle(ui.string(R.string.settings_title))
            columns(
                {
                    card(Space.M) {
                        add(laptop(ui, connection, routes.forget))
                        hairline()
                        add(
                            ui.toggle(ui.string(R.string.reconnect_automatically), settings.reconnect) {
                                settings.reconnect = it
                            },
                        )
                    }

                    section(ui.string(R.string.settings_surface))
                    card {
                        add(ui.link(ui.string(R.string.corners_title), cornersSummary(ui, settings), routes.corners))
                        hairline()
                        add(ui.link(ui.string(R.string.gestures_title), null, routes.gestures))
                        hairline()
                        add(ui.link(ui.string(R.string.shapes_title), shapesSummary(ui, settings), routes.shapes))
                        hairline()
                        val feel =
                            ui.string(R.string.feel_summary, settings.sensitivity, settings.dialLength.roundToInt())
                        add(ui.link(ui.string(R.string.dial_feel_title), feel, routes.dialFeel))
                    }

                    // Grouped by the thing being set, not by the kind of editor it opens: a layout canvas and a
                    // slider belong together when they configure the same control, and apart when they do not.
                    // The keyboard's text size and the macro labels were each a page of one control until 2.0;
                    // a page that holds a single row is a tap spent on nothing, so they sit here instead.
                    section(ui.string(R.string.settings_controls))
                    card {
                        add(keyTextSize(ui, settings))
                        hairline()
                        add(ui.link(ui.string(R.string.gamepad_layout_title), preset, routes.gamepadLayout))
                        hairline()
                        add(macroLabels(ui, settings))
                        hairline()
                        add(ui.link(ui.string(R.string.media_layout_title), null, routes.mediaLayout))
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
                        hairline()
                        add(
                            ui.link(
                                ui.string(R.string.appearance_title),
                                AppearanceScreen.summary(ui, settings),
                                routes.appearance,
                            ),
                        )
                    }

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

    /** How large the keyboard's key labels are drawn; the keyboard shrinks the lot if any would leave its key. */
    private fun keyTextSize(
        ui: Ui,
        settings: Settings,
    ): View =
        ui.slider(
            ui.string(R.string.keyboard_text_size),
            textRange.steps,
            textRange.stepOf(settings.keyTextScale),
            { step -> ui.string(R.string.multiplier_value, textRange.valueAt(step)) },
        ) { step ->
            settings.keyTextScale = textRange.valueAt(step)
        }

    /**
     * Whether a macro button carries its name under its picture. Only ever half a choice: a slot the laptop
     * sent no picture for shows its name whichever way this is set, because the alternative is a button with
     * nothing on it at all.
     */
    private fun macroLabels(
        ui: Ui,
        settings: Settings,
    ): View = ui.toggle(ui.string(R.string.macro_labels), settings.macroLabels) { settings.macroLabels = it }

    /**
     * How many shapes are drawn. A set that failed to decode counts as none, because that is exactly what
     * the pad will do with it, and a hub row claiming four shapes over a trackpad that recognises none
     * would send the owner looking in the wrong place.
     */
    private fun shapesSummary(
        ui: Ui,
        settings: Settings,
    ): String {
        val count = Shapes.decode(settings.shapes)?.size ?: 0
        if (count == 0) return ui.string(R.string.shapes_none)
        return ui.context.resources.getQuantityString(R.plurals.shapes_summary, count, count)
    }

    /** The four corners' dials, clockwise from the top left. */
    private fun cornersSummary(
        ui: Ui,
        settings: Settings,
    ): String =
        (0 until Perimeter.CORNERS).joinToString(", ") { corner ->
            settings.corner(corner)?.let { ui.string(it.nameRes) } ?: NO_DIAL
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
