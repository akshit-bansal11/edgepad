package me.akshitbansal.edgepad.screens

import android.app.UiModeManager
import android.view.View
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.Perimeter
import me.akshitbansal.edgepad.surface.Shapes
import kotlin.math.roundToInt

/**
 * The Settings hub: the remembered laptop, then one row into each group of settings with a summary of
 * what is set there, the theme, and the version. Every change is saved as it is made. Sideways, the
 * groups sit in two columns.
 */
object SettingsScreen {
    /** What the connection row shows: the remembered laptop, if any, and its state. */
    class Connection(
        val name: String?,
        val detail: String,
    )

    /** Where the hub's rows lead. */
    class Routes(
        val forget: () -> Unit,
        val corners: () -> Unit,
        val gestures: () -> Unit,
        val shapes: () -> Unit,
        val dialFeel: () -> Unit,
        val keyboard: () -> Unit,
        val gamepadLayout: () -> Unit,
        val macros: () -> Unit,
        val mediaLayout: () -> Unit,
        val appearance: () -> Unit,
        val guide: () -> Unit,
        val documentation: () -> Unit,
        val sideways: (Boolean) -> Unit,
        val back: () -> Unit,
    )

    private const val MARK_DP = 24f
    private const val NO_DIAL = "—"

    fun build(
        ui: Ui,
        settings: Settings,
        connection: Connection,
        preset: String,
        version: String,
        routes: Routes,
    ): View =
        ui.page(ui.bar(ui.string(R.string.settings_title), routes.back, mark(ui))) {
            columns(
                {
                    val forget =
                        if (connection.name !=
                            null
                        ) {
                            ui.chip(ui.string(R.string.forget), routes.forget)
                        } else {
                            null
                        }
                    add(ui.row(ui.stack(connection.name ?: ui.string(R.string.no_laptop), connection.detail), forget))
                    hairline()
                    add(
                        ui.toggle(ui.string(R.string.reconnect_automatically), settings.reconnect) {
                            settings.reconnect = it
                        },
                    )
                    hairline()

                    section(ui.string(R.string.settings_surface))
                    link(ui.string(R.string.corners_title), cornersSummary(ui, settings), routes.corners)
                    link(ui.string(R.string.gestures_title), null, routes.gestures)
                    link(ui.string(R.string.shapes_title), shapesSummary(ui, settings), routes.shapes)
                    val feel =
                        ui.string(R.string.feel_summary, settings.sensitivity, settings.dialLength.roundToInt())
                    link(ui.string(R.string.dial_feel_title), feel, routes.dialFeel)

                    // Grouped by the thing being set, not by the kind of editor it opens: a layout canvas and a
                    // slider belong together when they configure the same control, and apart when they do not.
                    section(ui.string(R.string.settings_controls))
                    link(
                        ui.string(R.string.keyboard_settings_title),
                        KeyboardSettingsScreen.summary(ui, settings),
                        routes.keyboard,
                    )
                    link(ui.string(R.string.gamepad_layout_title), preset.uppercase(), routes.gamepadLayout)
                    link(
                        ui.string(R.string.macro_buttons),
                        MacroSettingsScreen.summary(ui, settings),
                        routes.macros,
                    )
                    link(ui.string(R.string.media_layout_title), null, routes.mediaLayout)
                },
                {
                    section(ui.string(R.string.settings_appearance))
                    val themes = listOf(ui.string(R.string.theme_dark), ui.string(R.string.theme_light))
                    add(
                        ui.field(
                            ui.string(R.string.theme),
                            ui.segmented(themes, if (ui.palette.dark) 0 else 1) { i -> setTheme(ui, dark = i == 0) },
                        ),
                    )
                    hairline()
                    val held = listOf(ui.string(R.string.orientation_upright), ui.string(R.string.orientation_sideways))
                    add(
                        ui.field(
                            ui.string(R.string.orientation),
                            ui.segmented(held, if (settings.landscape) 1 else 0) { i -> routes.sideways(i == 1) },
                        ),
                    )
                    hairline()
                    link(
                        ui.string(R.string.appearance_title),
                        AppearanceScreen.summary(ui, settings),
                        routes.appearance,
                    )

                    section(ui.string(R.string.settings_help))
                    link(ui.string(R.string.guide_title), null, routes.guide)
                    link(
                        ui.string(R.string.documentation_title),
                        ui.string(R.string.documentation_summary),
                        routes.documentation,
                    )
                    mono(version, Type.MICRO, topDp = Space.L)
                },
            )
        }

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
        return if (count == 0) ui.string(R.string.shapes_none) else ui.string(R.string.shapes_summary, count)
    }

    /** The four corners' short dial names, clockwise from the top left. */
    private fun cornersSummary(
        ui: Ui,
        settings: Settings,
    ): String =
        (0 until Perimeter.CORNERS).joinToString(" · ") { corner ->
            settings.corner(corner)?.let { ui.string(it.shortRes) } ?: NO_DIAL
        }

    private fun Column.link(
        label: String,
        summary: String?,
        onOpen: () -> Unit,
    ) {
        add(ui.link(label, summary, onOpen))
        hairline()
    }

    private fun mark(ui: Ui): View =
        MarkView(ui.context).apply {
            layoutParams =
                LinearLayout.LayoutParams(ui.dp(MARK_DP), ui.dp(MARK_DP)).apply { marginEnd = ui.dp(Space.S) }
        }

    private fun setTheme(
        ui: Ui,
        dark: Boolean,
    ) {
        if (dark == ui.palette.dark) return
        // The system keeps the choice for this app and rebuilds the activity; the link survives the rebuild.
        ui.context.getSystemService(UiModeManager::class.java)?.setApplicationNightMode(
            if (dark) UiModeManager.MODE_NIGHT_YES else UiModeManager.MODE_NIGHT_NO,
        )
    }
}
