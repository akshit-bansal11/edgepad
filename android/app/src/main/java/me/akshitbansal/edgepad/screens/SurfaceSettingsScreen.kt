package me.akshitbansal.edgepad.screens

import android.view.View
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.surface.Perimeter
import me.akshitbansal.edgepad.surface.Shapes
import kotlin.math.roundToInt

/**
 * The surface's own settings, behind its gear and nowhere else: corners, gestures, shapes, dial feel, media
 * layout and what the surface is drawn in. The last row leads on to the app's settings, which hold only what
 * applies everywhere. The keyboard, the gamepad and the macros each keep theirs behind their own gear.
 */
object SurfaceSettingsScreen {
    /** Where the rows lead. */
    class Routes(
        val corners: () -> Unit,
        val gestures: () -> Unit,
        val shapes: () -> Unit,
        val dialFeel: () -> Unit,
        val mediaLayout: () -> Unit,
        val appearance: () -> Unit,
        val app: () -> Unit,
        val back: () -> Unit,
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
            card {
                add(ui.link(ui.string(R.string.corners_title), cornersSummary(ui, settings), routes.corners))
                hairline()
                add(ui.link(ui.string(R.string.gestures_title), null, routes.gestures))
                hairline()
                add(ui.link(ui.string(R.string.shapes_title), shapesSummary(ui, settings), routes.shapes))
                hairline()
                val feel = ui.string(R.string.feel_summary, settings.sensitivity, settings.dialLength.roundToInt())
                add(ui.link(ui.string(R.string.dial_feel_title), feel, routes.dialFeel))
                hairline()
                add(ui.link(ui.string(R.string.media_layout_title), null, routes.mediaLayout))
                hairline()
                add(
                    ui.link(
                        ui.string(R.string.appearance_title),
                        AppearanceScreen.summary(ui, settings),
                        routes.appearance,
                    ),
                )
            }
            card { add(ui.link(ui.string(R.string.settings_app), null, routes.app)) }
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
            (0 until Perimeter.CORNERS).joinToString(", ") { corner ->
                settings.corner(corner)?.let { ui.string(it.nameRes) } ?: NO_DIAL
            }
        }
}
