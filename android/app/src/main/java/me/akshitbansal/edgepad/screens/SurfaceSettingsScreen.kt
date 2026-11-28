package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.surface.Perimeter
import me.akshitbansal.edgepad.surface.Profiles
import me.akshitbansal.edgepad.surface.Shapes
import me.akshitbansal.edgepad.surface.SurfaceWidget
import kotlin.math.roundToInt

/**
 * The surface's own settings, behind its gear and, as its row under Screens, from the app's Settings page. Three
 * groups: Dials (corners, app profiles, dial feel, floating and hiding), Trackpad (the trackpad and its shapes) and
 * Surface (widgets, media layout, look, blackout). The last row leads on to the app's settings, unless this page was
 * opened from there. The keyboard, the gamepad and the macros each keep theirs behind their own gear.
 */
object SurfaceSettingsScreen {
    /** Where the rows lead. */
    class Rows(
        val corners: () -> Unit,
        val gestures: () -> Unit,
        val shapes: () -> Unit,
        val dialFeel: () -> Unit,
        val mediaLayout: () -> Unit,
        val appearance: () -> Unit,
        val profiles: () -> Unit,
        val widgets: () -> Unit,
    )

    /**
     * Where this page's own rows lead: the shared [rows], the app's settings ([system], null when this page was opened
     * from there) and back. [dialsHidden] and [onHideDials] are the surface's switch, which the activity keeps across
     * rebuilds; [laptopConnected] and [onFloatingChanged] are what the floating dials need and what follows a change.
     */
    class Routes(
        val rows: Rows,
        val system: (() -> Unit)?,
        val back: () -> Unit,
        val dialsHidden: Boolean,
        val onHideDials: (Boolean) -> Unit,
        val laptopConnected: () -> Boolean,
        val onFloatingChanged: () -> Unit,
    )

    private const val NO_DIAL = "—"

    fun build(
        ui: Ui,
        settings: Settings,
        backLabel: String,
        routes: Routes,
    ): View =
        // The nav bar draws no title: the large title under it is the screen's name, and says so to a screen reader.
        ui.page(ui.bar("", routes.back, backLabel = backLabel)) {
            largeTitle(ui.string(R.string.settings_surface_title))
            val rows = routes.rows
            section(ui.string(R.string.settings_dials))
            card {
                add(ui.link(ui.string(R.string.corners_title), cornersSummary(ui, settings), rows.corners))
                hairline()
                add(ui.link(ui.string(R.string.settings_profiles), profilesSummary(ui, settings), rows.profiles))
                hairline()
                val feel = ui.string(R.string.feel_summary, settings.sensitivity, settings.dialLength.roundToInt())
                add(ui.link(ui.string(R.string.dial_feel_title), feel, rows.dialFeel))
                hairline()
                add(DialFeelScreen.floatingToggle(ui, settings, routes.laptopConnected, routes.onFloatingChanged))
                hairline()
                add(ui.toggle(ui.string(R.string.controls_hide_dials), routes.dialsHidden) { routes.onHideDials(it) })
            }
            footnote(ui.string(R.string.floating_caption))

            section(ui.string(R.string.gestures_title))
            card {
                val pointer = ui.string(R.string.multiplier_value, settings.pointerSpeed)
                add(ui.link(ui.string(R.string.gestures_title), pointer, rows.gestures))
                hairline()
                add(ui.link(ui.string(R.string.shapes_title), shapesSummary(ui, settings), rows.shapes))
            }

            section(ui.string(R.string.settings_display))
            card {
                add(ui.link(ui.string(R.string.widgets_title), widgetsSummary(ui, settings), rows.widgets))
                hairline()
                add(ui.link(ui.string(R.string.media_layout_title), null, rows.mediaLayout))
                hairline()
                add(
                    ui.link(
                        ui.string(R.string.appearance_title),
                        AppearanceScreen.summary(ui, settings),
                        rows.appearance,
                    ),
                )
                hairline()
                // Issue #56. The surface reads the setting again when it takes the focus back.
                add(
                    ui.toggle(ui.string(R.string.controls_blackout), settings.blackout) {
                        settings.blackout = it
                    },
                )
            }
            routes.system?.let { systemSettings(it) }
        }

    /** Off, the number of apps with a profile of their own, or Automatic when only the kinds of app are followed. */
    private fun profilesSummary(
        ui: Ui,
        settings: Settings,
    ): String {
        if (!settings.profilesOn) return ui.string(R.string.profiles_summary_off)
        val apps = Profiles(settings.profiles).all.size
        return when {
            apps > 0 -> ui.context.resources.getQuantityString(R.plurals.profiles_summary_apps, apps, apps)
            settings.autoProfiles -> ui.string(R.string.profiles_summary_auto)
            else -> ui.string(R.string.profiles_summary_off)
        }
    }

    /** How many widgets are drawn behind the trackpad. */
    private fun widgetsSummary(
        ui: Ui,
        settings: Settings,
    ): String {
        val count = SurfaceWidget.entries.count { settings.widgetOn(it) }
        return ui.context.resources.getQuantityString(R.plurals.widgets_summary, count, count)
    }

    /**
     * How many shapes are drawn. A set that failed to decode counts as none, because that is exactly what
     * the pad will do with it, and a row claiming four shapes over a trackpad that recognises none would
     * send the owner looking in the wrong place.
     */
    private fun shapesSummary(
        ui: Ui,
        settings: Settings,
    ): String {
        val count = Shapes.decode(settings.shapes)?.size ?: 0
        if (count == 0) return ui.string(R.string.shapes_none)
        return ui.context.resources.getQuantityString(R.plurals.shapes_summary, count, count)
    }

    /** The four corners' dials, clockwise from the top left, or a count when each corner is split in two. */
    private fun cornersSummary(
        ui: Ui,
        settings: Settings,
    ): String =
        if (settings.splitCorners) {
            ui.string(R.string.corners_summary_split)
        } else {
            (0 until Perimeter.CORNERS).joinToString(ui.string(R.string.list_separator)) { corner ->
                settings.corner(corner)?.let { ui.string(it.nameRes) } ?: NO_DIAL
            }
        }
}
